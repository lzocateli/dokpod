using Dokpod.ControlPlane.Application.Commands;
using Dokpod.Domain.Auditing;
using Dokpod.Domain.Commands;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Commands;

public sealed class AgentCommandQueueServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly string PayloadHash = new('A', 64);

    [Fact]
    public async Task EnqueueAsync_WhenCommandIsNew_CreatesPendingCommand()
    {
        var store = new RecordingAgentCommandStore(AgentCommandEnqueueResult.Created);
        var deliveryQueue = new RecordingDeliveryQueue();
        var service = new AgentCommandQueueService(store, deliveryQueue, new FixedTimeProvider(Now));
        var command = CreateCommand();

        var auditEvent = CreateAuditEvent(command);

        var result = await service.EnqueueAsync(command, auditEvent, TestContext.Current.CancellationToken);

        Assert.Equal(AgentCommandEnqueueResult.Created, result);
        Assert.NotNull(store.Command);
        Assert.Equal(command, store.Command.Command);
        Assert.Equal(ControlPlaneCommandState.Pending, store.Command.State);
        Assert.Equal(Now, store.Command.CreatedAtUtc);
        Assert.Equal(Now, store.Command.UpdatedAtUtc);
        Assert.Same(auditEvent, store.AuditEvent);
        Assert.Equal(store.Command, deliveryQueue.Command);
    }

    [Theory]
    [InlineData(AgentCommandEnqueueResult.Duplicate)]
    [InlineData(AgentCommandEnqueueResult.ConflictingPayload)]
    public async Task EnqueueAsync_WhenCommandIsNotCreated_DoesNotPublishDelivery(
        AgentCommandEnqueueResult storeResult)
    {
        var store = new RecordingAgentCommandStore(storeResult);
        var deliveryQueue = new RecordingDeliveryQueue();
        var service = new AgentCommandQueueService(store, deliveryQueue, new FixedTimeProvider(Now));

        var command = CreateCommand();
        var result = await service.EnqueueAsync(
            command,
            CreateAuditEvent(command),
            TestContext.Current.CancellationToken);

        Assert.Equal(storeResult, result);
        Assert.Null(deliveryQueue.Command);
    }

    [Fact]
    public async Task EnqueueAsync_WhenDeadlineExpired_DoesNotPersistCommand()
    {
        var store = new RecordingAgentCommandStore(AgentCommandEnqueueResult.Created);
        var service = new AgentCommandQueueService(store, new RecordingDeliveryQueue(), new FixedTimeProvider(Now));
        var command = CreateCommand() with { DeadlineUtc = Now };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnqueueAsync(
                command,
                CreateAuditEvent(command),
                TestContext.Current.CancellationToken));

        Assert.Equal("command", exception.ParamName);
        Assert.Null(store.Command);
    }

    [Fact]
    public async Task EnqueueAsync_WhenPayloadHashIsNotCanonical_DoesNotPersistCommand()
    {
        var store = new RecordingAgentCommandStore(AgentCommandEnqueueResult.Created);
        var service = new AgentCommandQueueService(store, new RecordingDeliveryQueue(), new FixedTimeProvider(Now));
        var command = CreateCommand() with { PayloadHash = "sha256:abc" };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.EnqueueAsync(
                command,
                CreateAuditEvent(command),
                TestContext.Current.CancellationToken));

        Assert.Equal("command", exception.ParamName);
        Assert.Null(store.Command);
    }

    private static AgentCommand CreateCommand() =>
        new(
            Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901"),
            Guid.Parse("555c96bb-19dc-4b35-ac39-a83dbb65bc67"),
            AgentCommandKind.RestartContainer,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "revision-01",
            PayloadHash,
            Now.AddMinutes(1),
            8);

    private static AuditEvent CreateAuditEvent(AgentCommand command) =>
        AuditEvent.Create(
            Guid.NewGuid(),
            Now,
            Guid.NewGuid(),
            AuditActorKind.User,
            "user-1",
            "container.restart",
            command.EnvironmentId,
            AuditOutcome.Succeeded,
            commandId: command.CommandId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingAgentCommandStore(AgentCommandEnqueueResult result) : IAgentCommandStore
    {
        public PersistedAgentCommand? Command { get; private set; }
        public AuditEvent? AuditEvent { get; private set; }

        public Task<AgentCommandStatusSnapshot?> GetAsync(
            Guid environmentId,
            Guid commandId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentCommandEnqueueResult> EnqueueAsync(
            PersistedAgentCommand command,
            CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(result);
        }

        public Task<AgentCommandEnqueueResult> EnqueueAuditedAsync(
            PersistedAgentCommand command,
            AuditEvent auditEvent,
            CancellationToken cancellationToken)
        {
            AuditEvent = auditEvent;
            return EnqueueAsync(command, cancellationToken);
        }

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
}