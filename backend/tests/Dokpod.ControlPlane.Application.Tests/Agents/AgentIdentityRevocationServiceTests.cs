using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Auditing;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Agents;

public sealed class AgentIdentityRevocationServiceTests
{
    [Fact]
    public async Task RevokeAsync_WhenAuthorizationIsDenied_DoesNotRevokeIdentity()
    {
        var registry = new RecordingIdentityRegistry(true);
        var sessions = new RecordingSessionStore();
        var service = CreateService(
            registry,
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "scope_denied"),
            sessions);

        var result = await service.RevokeAsync(
            Guid.NewGuid(),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Allowed);
        Assert.False(result.Revoked);
        Assert.False(registry.WasCalled);
        Assert.False(sessions.WasInvalidated);
    }

    [Fact]
    public async Task RevokeAsync_WhenAuthorized_RevokesIdentity()
    {
        var registry = new RecordingIdentityRegistry(true);
        var sessions = new RecordingSessionStore();
        var service = CreateService(
            registry,
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null),
            sessions);

        var result = await service.RevokeAsync(
            Guid.NewGuid(),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Allowed);
        Assert.True(result.Revoked);
        Assert.True(registry.WasCalled);
        Assert.True(sessions.WasInvalidated);
    }

    [Fact]
    public async Task RevokeAsync_WhenIdentityDoesNotExist_DoesNotInvalidateSession()
    {
        var registry = new RecordingIdentityRegistry(false);
        var sessions = new RecordingSessionStore();
        var service = CreateService(
            registry,
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null),
            sessions);

        var result = await service.RevokeAsync(
            Guid.NewGuid(),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Revoked);
        Assert.False(sessions.WasInvalidated);
    }

    private static AgentIdentityRevocationService CreateService(
        IAgentIdentityRegistry registry,
        EnvironmentAuthorizationDecision decision,
        IAgentSessionStore sessionStore) =>
        new(
            new EnvironmentAccessService(
                new RecordingAuditEventWriter(),
                new FixedAuthorizationDecider(decision)),
            registry,
            sessionStore);

    private sealed class FixedAuthorizationDecider(EnvironmentAuthorizationDecision decision)
        : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken) => Task.FromResult(decision);
    }

    private sealed class RecordingAuditEventWriter : IAuditEventWriter
    {
        public Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingIdentityRegistry(bool revokeResult) : IAgentIdentityRegistry
    {
        public bool WasCalled { get; private set; }

        public ValueTask<AgentIdentity?> FindByFingerprintAsync(
            string certificateFingerprint,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<AgentIdentity?>(null);

        public Task<bool> RevokeEnvironmentAsync(
            Guid environmentId,
            DateTimeOffset revokedAtUtc,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(revokeResult);
        }
    }

    private sealed class RecordingSessionStore : IAgentSessionStore
    {
        public bool WasInvalidated { get; private set; }

        public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentSession?> FindActiveAsync(Guid environmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask InvalidateEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken)
        {
            WasInvalidated = true;
            return ValueTask.CompletedTask;
        }

        public bool IsActive(AgentSession session) => throw new NotSupportedException();

        public Task WaitUntilInactiveAsync(AgentSession session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DeactivateAsync(AgentSession session, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}