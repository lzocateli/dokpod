using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests.Agents;

public sealed class AgentControlServiceTests
{
    [Fact]
    public async Task Connect_RejectsConnectionWithoutClientCertificateBeforeReadingStream()
    {
        var service = new AgentControlService(new AgentSessionNegotiator(
            new RejectingIdentityRegistry(),
            new RejectingSessionStore()));
        var context = new TestServerCallContext(new DefaultHttpContext());

        var exception = await Assert.ThrowsAsync<RpcException>(() => service.Connect(
            new ThrowingRequestStreamReader(),
            new ThrowingResponseStreamWriter(),
            context));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
        Assert.Equal("client_certificate_required", exception.Status.Detail);
    }

    private sealed class RejectingIdentityRegistry : IAgentIdentityRegistry
    {
        public ValueTask<AgentIdentity?> FindByFingerprintAsync(
            string certificateFingerprint,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AgentIdentity?>(new InvalidOperationException("not_expected"));
    }

    private sealed class RejectingSessionStore : IAgentSessionStore
    {
        public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken) =>
            ValueTask.FromException<AgentSession>(new InvalidOperationException("not_expected"));
    }

    private sealed class ThrowingRequestStreamReader : IAsyncStreamReader<Dokpod.Agent.Contracts.V1.AgentMessage>
    {
        public Dokpod.Agent.Contracts.V1.AgentMessage Current => throw new InvalidOperationException("not_expected");

        public Task<bool> MoveNext(CancellationToken cancellationToken) =>
            Task.FromException<bool>(new InvalidOperationException("not_expected"));
    }

    private sealed class ThrowingResponseStreamWriter : IServerStreamWriter<Dokpod.Agent.Contracts.V1.ControlPlaneMessage>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(Dokpod.Agent.Contracts.V1.ControlPlaneMessage message) =>
            Task.FromException(new InvalidOperationException("not_expected"));
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        private readonly Dictionary<object, object> userState = new()
        {
            ["__HttpContext"] = new DefaultHttpContext(),
        };

        public TestServerCallContext(HttpContext httpContext)
        {
            userState["__HttpContext"] = httpContext;
        }

        protected override string MethodCore => "dokpod.agent.v1.AgentControl/Connect";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:127.0.0.1:5001";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("insecure", []);
        protected override IDictionary<object, object> UserStateCore => userState;

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}