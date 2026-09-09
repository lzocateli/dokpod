using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Application.Agents;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Agents;

public sealed class AgentSessionNegotiatorTests
{
    [Fact]
    public async Task NegotiateAsync_ResolvesEnvironmentFromCertificateFingerprint()
    {
        using var certificate = CreateCertificate(includeClientAuthenticationEku: true);
        var environmentId = Guid.Parse("40a4b18b-16d4-479b-beba-6e922e2731d7");
        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var registry = new FakeIdentityRegistry(new AgentIdentity(environmentId, fingerprint));
        var negotiator = new AgentSessionNegotiator(registry, new FakeSessionStore());

        var session = await negotiator.NegotiateAsync(
            certificate,
            CreateHello("1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(environmentId, session.EnvironmentId);
        Assert.Equal(fingerprint, registry.RequestedFingerprint);
    }

    [Fact]
    public async Task NegotiateAsync_RejectsCertificateWithoutClientAuthenticationEku()
    {
        using var certificate = CreateCertificate(includeClientAuthenticationEku: false);
        var negotiator = new AgentSessionNegotiator(new FakeIdentityRegistry(null), new FakeSessionStore());

        var exception = await Assert.ThrowsAsync<AgentSessionRejectedException>(() => negotiator.NegotiateAsync(
            certificate,
            CreateHello("1"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("client_certificate_eku_invalid", exception.FailureCode);
    }

    [Fact]
    public async Task NegotiateAsync_RejectsUnsupportedProtocol()
    {
        using var certificate = CreateCertificate(includeClientAuthenticationEku: true);
        var negotiator = new AgentSessionNegotiator(new FakeIdentityRegistry(null), new FakeSessionStore());

        var exception = await Assert.ThrowsAsync<AgentSessionRejectedException>(() => negotiator.NegotiateAsync(
            certificate,
            CreateHello("2"),
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("protocol_version_unsupported", exception.FailureCode);
    }

    [Fact]
    public async Task NegotiateAsync_RejectsUnknownHelloEnums()
    {
        using var certificate = CreateCertificate(includeClientAuthenticationEku: true);
        var negotiator = new AgentSessionNegotiator(
            new FakeIdentityRegistry(null),
            new FakeSessionStore());
        var hello = CreateHello("1");
        hello.Engine = (EngineKind)99;
        hello.Capabilities.Add((Capability)99);

        var exception = await Assert.ThrowsAsync<AgentSessionRejectedException>(() => negotiator.NegotiateAsync(
            certificate,
            hello,
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("agent_hello_invalid", exception.FailureCode);
    }

    private static AgentHello CreateHello(string protocolVersion)
    {
        var hello = new AgentHello
        {
            AgentVersion = "0.1.0",
            Engine = EngineKind.Docker,
            EngineVersion = "29.0",
            OperatingSystem = Agent.Contracts.V1.OperatingSystem.Linux,
            OperatingSystemVersion = "test",
            Architecture = Architecture.Amd64,
        };
        hello.Capabilities.Add(Capability.Inventory);
        hello.SupportedProtocolVersions.Add(protocolVersion);
        return hello;
    }

    private static X509Certificate2 CreateCertificate(bool includeClientAuthenticationEku)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=synthetic-agent", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (includeClientAuthenticationEku)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.2") },
                critical: true));
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
    }

    private sealed class FakeIdentityRegistry(AgentIdentity? identity) : IAgentIdentityRegistry
    {
        public string? RequestedFingerprint { get; private set; }

        public ValueTask<AgentIdentity?> FindByFingerprintAsync(
            string certificateFingerprint,
            CancellationToken cancellationToken)
        {
            RequestedFingerprint = certificateFingerprint;
            return ValueTask.FromResult(identity);
        }
    }

    private sealed class FakeSessionStore : IAgentSessionStore
    {
        private ulong fencingToken;

        public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AgentSession(environmentId, Guid.NewGuid(), ++fencingToken));

        public bool IsActive(AgentSession session) => true;

        public Task WaitUntilInactiveAsync(AgentSession session, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        public ValueTask DeactivateAsync(AgentSession session, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}