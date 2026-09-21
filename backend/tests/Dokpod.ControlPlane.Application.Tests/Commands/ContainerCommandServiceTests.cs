using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Commands;
using Dokpod.Domain.Environments;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Commands;

public sealed class ContainerCommandServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 22, 0, 0, TimeSpan.Zero);
    private static readonly Guid EnvironmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CommandId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ContainerId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task SubmitAsync_AuthorizesSpecificScopeAndUsesActiveFencing()
    {
        var authorization = new RecordingAuthorizationDecider(AuthorizationDecisionOutcome.Allowed);
        var commandStore = new RecordingCommandStore(AgentCommandEnqueueResult.Created);
        var deliveryQueue = new RecordingDeliveryQueue();
        var service = CreateService(authorization, commandStore, deliveryQueue);

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.RestartContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.Accepted, result.Outcome);
        Assert.Equal(EnvironmentResourceScopes.RestartContainer, authorization.Scope);
        Assert.Equal(17, commandStore.Command?.Command.FencingToken);
        Assert.Equal(64, commandStore.Command?.Command.PayloadHash.Length);
        Assert.NotNull(deliveryQueue.Command);
    }

    [Fact]
    public async Task SubmitAsync_DeniedAuthorizationDoesNotReadOrEnqueueEnvironment()
    {
        var registrationStore = new RecordingRegistrationStore();
        var commandStore = new RecordingCommandStore(AgentCommandEnqueueResult.Created);
        var service = CreateService(
            new RecordingAuthorizationDecider(AuthorizationDecisionOutcome.Denied, "access_denied"),
            commandStore,
            new RecordingDeliveryQueue(),
            registrationStore);

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.StartContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.AuthorizationDenied, result.Outcome);
        Assert.False(registrationStore.WasRead);
        Assert.Null(commandStore.Command);
    }

    [Fact]
    public async Task SubmitAsync_ReplaysSameCommandWithoutRepublishing()
    {
        var commandStore = new RecordingCommandStore(AgentCommandEnqueueResult.Duplicate);
        var deliveryQueue = new RecordingDeliveryQueue();
        var service = CreateService(
            new RecordingAuthorizationDecider(AuthorizationDecisionOutcome.Allowed),
            commandStore,
            deliveryQueue);

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.StopContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.Duplicate, result.Outcome);
        Assert.Null(deliveryQueue.Command);
    }

    [Fact]
    public async Task SubmitAsync_AuthorizationFailureFailsClosedBeforeReadingEnvironment()
    {
        var registrationStore = new RecordingRegistrationStore();
        var commandStore = new RecordingCommandStore(AgentCommandEnqueueResult.Created);
        var service = new ContainerCommandService(
            new ThrowingAuthorizationDecider(),
            registrationStore,
            new FixedSessionStore(new AgentSession(EnvironmentId, Guid.NewGuid(), 17)),
            new AgentCommandQueueService(
                commandStore,
                new RecordingDeliveryQueue(),
                new FixedTimeProvider(Now)));

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.StartContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.AuthorizationUnavailable, result.Outcome);
        Assert.Equal("authorization_unavailable", result.FailureCode);
        Assert.False(registrationStore.WasRead);
        Assert.Null(commandStore.Command);
    }

    [Fact]
    public async Task SubmitAsync_MissingEnvironmentScopeDoesNotReadSessionOrEnqueue()
    {
        var commandStore = new RecordingCommandStore(AgentCommandEnqueueResult.Created);
        var sessionStore = new RecordingSessionStore(new AgentSession(EnvironmentId, Guid.NewGuid(), 17));
        var service = CreateService(
            new RecordingAuthorizationDecider(AuthorizationDecisionOutcome.Allowed),
            commandStore,
            new RecordingDeliveryQueue(),
            new RecordingRegistrationStore([EnvironmentResourceScopes.Read]),
            sessionStore);

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.DeleteContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.EnvironmentUnavailable, result.Outcome);
        Assert.False(sessionStore.WasRead);
        Assert.Null(commandStore.Command);
    }

    [Fact]
    public async Task SubmitAsync_OfflineAgentDoesNotEnqueue()
    {
        var commandStore = new RecordingCommandStore(AgentCommandEnqueueResult.Created);
        var service = CreateService(
            new RecordingAuthorizationDecider(AuthorizationDecisionOutcome.Allowed),
            commandStore,
            new RecordingDeliveryQueue(),
            sessionStore: new RecordingSessionStore(null));

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.StopContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.AgentOffline, result.Outcome);
        Assert.Equal("agent_offline", result.FailureCode);
        Assert.Null(commandStore.Command);
    }

    [Fact]
    public async Task SubmitAsync_ConflictingCommandIdReturnsStableFailure()
    {
        var deliveryQueue = new RecordingDeliveryQueue();
        var service = CreateService(
            new RecordingAuthorizationDecider(AuthorizationDecisionOutcome.Allowed),
            new RecordingCommandStore(AgentCommandEnqueueResult.ConflictingPayload),
            deliveryQueue);

        var result = await service.SubmitAsync(
            EnvironmentId,
            CommandId,
            AgentCommandKind.RestartContainer,
            ContainerId,
            "revision-01",
            Now.AddMinutes(1),
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Equal(ContainerCommandSubmissionOutcome.ConflictingPayload, result.Outcome);
        Assert.Equal("command_id_conflict", result.FailureCode);
        Assert.Null(deliveryQueue.Command);
    }

    private static ContainerCommandService CreateService(
        RecordingAuthorizationDecider authorization,
        RecordingCommandStore commandStore,
        RecordingDeliveryQueue deliveryQueue,
        RecordingRegistrationStore? registrationStore = null,
        IAgentSessionStore? sessionStore = null) =>
        new(
            authorization,
            registrationStore ?? new RecordingRegistrationStore(),
            sessionStore ?? new FixedSessionStore(new AgentSession(EnvironmentId, Guid.NewGuid(), 17)),
            new AgentCommandQueueService(commandStore, deliveryQueue, new FixedTimeProvider(Now)));

    private sealed class RecordingAuthorizationDecider(
        AuthorizationDecisionOutcome outcome,
        string? failureCode = null) : IEnvironmentAuthorizationDecider
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
            return Task.FromResult(new EnvironmentAuthorizationDecision(outcome, failureCode));
        }
    }

    private sealed class ThrowingAuthorizationDecider : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Authorization service unavailable.");
    }

    private sealed class RecordingRegistrationStore(
        IReadOnlyCollection<string>? scopes = null) : IEnvironmentRegistrationStore
    {
        public bool WasRead { get; private set; }

        public Task<EnvironmentRegistrationStoreResult> CreateAsync(
            EnvironmentRegistration registration,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EnvironmentRegistration?> GetAsync(
            Guid environmentId,
            CancellationToken cancellationToken)
        {
            WasRead = true;
            return Task.FromResult<EnvironmentRegistration?>(EnvironmentRegistration.Create(
                environmentId,
                "lab",
                "lab-host",
                true,
                scopes ?? EnvironmentResourceScopes.Supported));
        }
    }

    private sealed class RecordingSessionStore(AgentSession? session) : IAgentSessionStore
    {
        public bool WasRead { get; private set; }

        public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentSession?> FindActiveAsync(Guid environmentId, CancellationToken cancellationToken)
        {
            WasRead = true;
            return ValueTask.FromResult(session);
        }

        public ValueTask InvalidateEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool IsActive(AgentSession candidate) => candidate == session;

        public Task WaitUntilInactiveAsync(AgentSession candidate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DeactivateAsync(AgentSession candidate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedSessionStore(AgentSession? session) : IAgentSessionStore
    {
        public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<AgentSession?> FindActiveAsync(Guid environmentId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(session);

        public ValueTask InvalidateEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool IsActive(AgentSession candidate) => candidate == session;

        public Task WaitUntilInactiveAsync(AgentSession candidate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DeactivateAsync(AgentSession candidate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingCommandStore(AgentCommandEnqueueResult result) : IAgentCommandStore
    {
        public PersistedAgentCommand? Command { get; private set; }

        public Task<AgentCommandEnqueueResult> EnqueueAsync(
            PersistedAgentCommand command,
            CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(result);
        }

        public Task<AgentCommandStatusUpdateResult> ApplyStatusAsync(
            AgentCommandStatusUpdate update,
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

    private sealed class RecordingDeliveryQueue : IAgentCommandDeliveryQueue
    {
        public PersistedAgentCommand? Command { get; private set; }

        public ValueTask EnqueueAsync(
            PersistedAgentCommand command,
            CancellationToken cancellationToken)
        {
            Command = command;
            return ValueTask.CompletedTask;
        }

        public ValueTask<PersistedAgentCommand> DequeueAsync(
            Guid environmentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}