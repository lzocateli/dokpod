using System.Net.Security;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Contracts.V1;
using Dokpod.Agent.Infrastructure.Protocol;
using Dokpod.ControlPlane.Api;
using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests.Agents;

public sealed class TransportBoundaryTests
{
    [Fact]
    public void AgentControlClient_RejectsNonHttpsEndpoint()
    {
        using var certificate = CreateCertificate();

        var exception = Assert.Throws<ArgumentException>(() => AgentControlClient.Create(
            new Uri("http://localhost:7443"),
            certificate));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task InMemoryAgentSessionStore_InvalidatesPreviousEnvironmentSession()
    {
        var store = new InMemoryAgentSessionStore();
        var environmentId = Guid.NewGuid();

        var first = await store.ActivateAsync(environmentId, TestContext.Current.CancellationToken);
        var second = await store.ActivateAsync(environmentId, TestContext.Current.CancellationToken);

        Assert.False(store.IsActive(first));
        Assert.True(store.IsActive(second));

        await store.DeactivateAsync(first, TestContext.Current.CancellationToken);
        Assert.True(store.IsActive(second));
        await store.DeactivateAsync(second, TestContext.Current.CancellationToken);
        Assert.False(store.IsActive(second));
    }

    [Fact]
    public async Task InMemoryAgentSessionStore_AllowsOnlyOneActiveSessionUnderConcurrency()
    {
        var store = new InMemoryAgentSessionStore();
        var environmentId = Guid.NewGuid();

        var sessions = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(async () => await store.ActivateAsync(
                environmentId,
                TestContext.Current.CancellationToken).AsTask())));

        Assert.Single(sessions, store.IsActive);
    }

    [Fact]
    public async Task KestrelMtlsHandshake_EstablishesAgentSession()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        var endpoint = GetGrpcEndpoint(app);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        await call.RequestStream.WriteAsync(new AgentMessage
        {
            Hello = new AgentHello
            {
                Engine = EngineKind.Docker,
                OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                Architecture = Architecture.Amd64,
                Capabilities = { Capability.Inventory },
                SupportedProtocolVersions = { "1" },
            },
        }, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ControlPlaneMessage.PayloadOneofCase.SessionEstablished, call.ResponseStream.Current.PayloadCase);
        Assert.NotEqual(string.Empty, call.ResponseStream.Current.SessionEstablished.SessionId);
        Assert.NotEqual(0UL, call.ResponseStream.Current.SessionEstablished.FencingToken);
    }

    [Fact]
    public async Task KestrelMtlsHandshake_RejectsUnknownClientCertificate()
    {
        using var certificates = TestCertificates.Create();
        using var unknownAgent = TestCertificates.CreateAgentCertificate(certificates.CertificateAuthority);
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), "unknown");
        using var handler = CreateHttpHandler(certificates, unknownAgent);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(new AgentMessage
            {
                Hello = new AgentHello
                {
                    Engine = EngineKind.Docker,
                    OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                    Architecture = Architecture.Amd64,
                    Capabilities = { Capability.Inventory },
                    SupportedProtocolVersions = { "1" },
                },
            }, TestContext.Current.CancellationToken);
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        });
        Assert.Equal(StatusCode.PermissionDenied, exception.StatusCode);
    }

    [Fact]
    public async Task KestrelMtlsHandshake_RejectsClientOutsideTrustedAuthority()
    {
        using var certificates = TestCertificates.Create();
        using var untrustedAgent = TestCertificates.CreateUntrustedAgentCertificate();
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), "unused");
        using var handler = CreateHttpHandler(certificates, untrustedAgent);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await call.RequestStream.WriteAsync(new AgentMessage
            {
                Hello = new AgentHello
                {
                    Engine = EngineKind.Docker,
                    OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                    Architecture = Architecture.Amd64,
                    Capabilities = { Capability.Inventory },
                    SupportedProtocolVersions = { "1" },
                },
            }, TestContext.Current.CancellationToken);
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task KestrelSession_ReconnectFencesPreviousStream()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var firstCall = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var firstSession = await EstablishSessionAsync(firstCall);
        using var secondCall = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var secondSession = await EstablishSessionAsync(secondCall);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await firstCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Aborted, exception.StatusCode);
        Assert.Equal("agent_session_fenced", exception.Status.Detail);
        Assert.NotEqual(firstSession.FencingToken, secondSession.FencingToken);
    }

    [Fact]
    public async Task KestrelSession_RejectsReplayedSequence()
    {
        using var certificates = TestCertificates.Create();
        var environmentId = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, environmentId, fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(
            GetGrpcEndpoint(app),
            new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);

        var heartbeat = CreateHeartbeat(session, 1);
        await call.RequestStream.WriteAsync(heartbeat, TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(heartbeat, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("agent_message_metadata_invalid", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsSequenceGap()
    {
        using var certificates = TestCertificates.Create();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);

        await call.RequestStream.WriteAsync(CreateHeartbeat(session, 1), TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(CreateHeartbeat(session, 3), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("agent_message_metadata_invalid", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsMissingMetadata()
    {
        using var certificates = TestCertificates.Create();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        await EstablishSessionAsync(call);

        await call.RequestStream.WriteAsync(
            new AgentMessage { Heartbeat = new Heartbeat() },
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
        Assert.Equal("agent_message_metadata_invalid", exception.Status.Detail);
    }

    [Fact]
    public async Task KestrelSession_RejectsUnsupportedPayload()
    {
        using var certificates = TestCertificates.Create();
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificates.Agent.RawData));
        await using var app = await StartApiAsync(certificates, Guid.NewGuid(), fingerprint);
        using var handler = CreateHttpHandler(certificates);
        using var channel = GrpcChannel.ForAddress(GetGrpcEndpoint(app), new GrpcChannelOptions { HttpHandler = handler });
        var client = new AgentControl.AgentControlClient(channel);
        using var call = client.Connect(cancellationToken: TestContext.Current.CancellationToken);
        var session = await EstablishSessionAsync(call);
        var message = CreateHeartbeat(session, 1);
        message.Heartbeat = null;
        message.InventoryDelta = new InventoryDelta { InventoryRevision = 1 };

        await call.RequestStream.WriteAsync(message, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unimplemented, exception.StatusCode);
        Assert.Equal("agent_payload_not_supported", exception.Status.Detail);
    }

    private static AgentMessage CreateHeartbeat(SessionEstablished session, ulong sequence)
        => new()
        {
            Metadata = new MessageMetadata
            {
                ProtocolVersion = "1",
                SessionId = session.SessionId,
                FencingToken = session.FencingToken,
                Sequence = sequence,
                OccurredAt = Timestamp.FromDateTime(DateTime.UtcNow),
                CorrelationId = Guid.NewGuid().ToString("D"),
            },
            Heartbeat = new Heartbeat(),
        };

    private static async Task<SessionEstablished> EstablishSessionAsync(
        AsyncDuplexStreamingCall<AgentMessage, ControlPlaneMessage> call)
    {
        await call.RequestStream.WriteAsync(new AgentMessage
        {
            Hello = new AgentHello
            {
                Engine = EngineKind.Docker,
                OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
                Architecture = Architecture.Amd64,
                Capabilities = { Capability.Inventory },
                SupportedProtocolVersions = { "1" },
            },
        }, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        return call.ResponseStream.Current.SessionEstablished;
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=synthetic-agent",
            key,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private static async Task<WebApplication> StartApiAsync(
        TestCertificates certificates,
        Guid environmentId,
        string fingerprint)
    {
        var builder = ApiHost.CreateBuilder(
            [],
            new ApiHostOptions(
                0,
                0,
                certificates.Server,
                certificates.ValidateClient,
                LoopbackOnly: true,
                ConfigureServices: services => services.AddSingleton<IAgentIdentityRegistry>(
                    new FixedAgentIdentityRegistry(environmentId, fingerprint))));
        var app = builder.Build();
        ApiHost.MapEndpoints(app);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class FixedAgentIdentityRegistry(Guid environmentId, string fingerprint) : IAgentIdentityRegistry
    {
        public ValueTask<AgentIdentity?> FindByFingerprintAsync(
            string certificateFingerprint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<AgentIdentity?>(
                string.Equals(certificateFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                    ? new AgentIdentity(environmentId, fingerprint)
                    : null);
        }
    }

    private static Uri GetGrpcEndpoint(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        var address = addresses.Single(address => address.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        return new Uri(address);
    }

    private static HttpClientHandler CreateHttpHandler(TestCertificates certificates, X509Certificate2? agent = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && certificates.ValidateServer(certificate),
        };
        handler.ClientCertificates.Add(agent ?? certificates.Agent);
        return handler;
    }

    private sealed class TestCertificates : IDisposable
    {
        private TestCertificates(X509Certificate2 certificateAuthority, X509Certificate2 server, X509Certificate2 agent)
        {
            CertificateAuthority = certificateAuthority;
            Server = server;
            Agent = agent;
        }

        public X509Certificate2 CertificateAuthority { get; }
        public X509Certificate2 Server { get; }
        public X509Certificate2 Agent { get; }
        public Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool> ValidateClient => ValidateClientCertificate;

        public static TestCertificates Create()
        {
            using var authorityKey = RSA.Create(2048);
            var authorityRequest = new CertificateRequest("CN=Dokpod test CA", authorityKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 1, true));
            authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            var authority = authorityRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30));
            return new TestCertificates(authority, CreateSignedCertificate(authority, "localhost", false), CreateAgentCertificate(authority));
        }

        public static X509Certificate2 CreateAgentCertificate(X509Certificate2 authority)
            => CreateSignedCertificate(authority, "dokpod-agent", true);

        public static X509Certificate2 CreateUntrustedAgentCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=untrusted-agent",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") },
                true));
            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5));
        }

        public bool ValidateServer(X509Certificate certificate)
            => BuildChain(certificate, X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.1");

        private bool ValidateClientCertificate(X509Certificate2 certificate, X509Chain? _, SslPolicyErrors __)
            => BuildChain(certificate, X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.2");

        private bool BuildChain(X509Certificate certificate, X509KeyUsageFlags _, string eku)
        {
            using var chain = new X509Chain();
            using var presentedCertificate = new X509Certificate2(certificate);
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(CertificateAuthority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            return chain.Build(presentedCertificate) &&
                presentedCertificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                    .Single().EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == eku);
        }

        private static X509Certificate2 CreateSignedCertificate(X509Certificate2 authority, string commonName, bool client)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var usages = new OidCollection { new Oid(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            if (!client)
            {
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                request.CertificateExtensions.Add(names.Build());
            }

            using var certificate = request.Create(
                authority,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5),
                RandomNumberGenerator.GetBytes(16));
            return certificate.CopyWithPrivateKey(key);
        }

        public void Dispose()
        {
            CertificateAuthority.Dispose();
            Server.Dispose();
            Agent.Dispose();
        }
    }
}