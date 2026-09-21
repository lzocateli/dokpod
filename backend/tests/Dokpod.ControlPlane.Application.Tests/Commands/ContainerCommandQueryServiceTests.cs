using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Commands;

public sealed class ContainerCommandQueryServiceTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CommandId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetAsync_DeniedAuthorizationDoesNotReadCommand()
    {
        var store = new RecordingCommandStore(null);
        var service = new ContainerCommandQueryService(
            store,
            new FixedAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "access_denied")));

        var result = await service.GetAsync(
            EnvironmentId,
            CommandId,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandQueryOutcome.AuthorizationDenied, result.Outcome);
        Assert.False(store.WasRead);
    }

    [Fact]
    public async Task GetAsync_AuthorizedQueryReturnsSafeSnapshot()
    {
        var now = new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.Zero);
        var snapshot = new AgentCommandStatusSnapshot(
            EnvironmentId,
            CommandId,
            AgentCommandKind.RestartContainer,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "revision-01",
            ControlPlaneCommandState.Succeeded,
            null,
            "revision-02",
            now.AddMinutes(1),
            now,
            now.AddSeconds(2),
            now.AddSeconds(2));
        var store = new RecordingCommandStore(snapshot);
        var authorization = new FixedAuthorizationDecider(
            new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null));
        var service = new ContainerCommandQueryService(store, authorization);

        var result = await service.GetAsync(
            EnvironmentId,
            CommandId,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandQueryOutcome.Found, result.Outcome);
        Assert.Same(snapshot, result.Command);
        Assert.Equal("environment:read", authorization.Scope);
        Assert.True(store.WasRead);
    }

    [Fact]
    public async Task GetAsync_AuthorizedMissingCommandReturnsNotFound()
    {
        var store = new RecordingCommandStore(null);
        var service = new ContainerCommandQueryService(
            store,
            new FixedAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Allowed, null)));

        var result = await service.GetAsync(
            EnvironmentId,
            CommandId,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandQueryOutcome.NotFound, result.Outcome);
        Assert.Equal("command_not_found", result.FailureCode);
        Assert.True(store.WasRead);
    }

    [Fact]
    public async Task GetAsync_InvalidAuthorizationDecisionFailsClosed()
    {
        var store = new RecordingCommandStore(null);
        var service = new ContainerCommandQueryService(
            store,
            new FixedAuthorizationDecider(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, null)));

        var result = await service.GetAsync(
            EnvironmentId,
            CommandId,
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandQueryOutcome.AuthorizationUnavailable, result.Outcome);
        Assert.Equal("authorization_invalid", result.FailureCode);
        Assert.False(store.WasRead);
    }

    private sealed class FixedAuthorizationDecider(
        EnvironmentAuthorizationDecision decision) : IEnvironmentAuthorizationDecider
    {
        public string? Scope { get; private set; }

        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            Scope = scope;
            return Task.FromResult(decision);
        }
    }

    private sealed class RecordingCommandStore(
        AgentCommandStatusSnapshot? snapshot) : IAgentCommandStore
    {
        public bool WasRead { get; private set; }

        public Task<AgentCommandStatusSnapshot?> GetAsync(
            Guid environmentId,
            Guid commandId,
            CancellationToken cancellationToken)
        {
            WasRead = true;
            return Task.FromResult(snapshot);
        }

        public Task<AgentCommandEnqueueResult> EnqueueAsync(
            PersistedAgentCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandEnqueueResult> EnqueueAuditedAsync(
            PersistedAgentCommand command,
            AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandStatusUpdateResult> ApplyStatusAsync(
            AgentCommandStatusUpdate update,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandStatusUpdateResult> ApplyStatusAuditedAsync(
            AgentCommandStatusUpdate update,
            AuditEvent auditEvent,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ExpireNonTerminalAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PersistedAgentCommand>> ClaimDispatchableAsync(
            Guid environmentId,
            long activeFencingToken,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}