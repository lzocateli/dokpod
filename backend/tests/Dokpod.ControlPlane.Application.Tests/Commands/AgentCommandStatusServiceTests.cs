using Dokpod.ControlPlane.Application.Commands;
using Dokpod.Domain.Auditing;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Commands;

public sealed class AgentCommandStatusServiceTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("555c96bb-19dc-4b35-ac39-a83dbb65bc67");
    private static readonly Guid CommandId = Guid.Parse("9dd93625-9d6a-4c04-94da-d04122028901");
    private static readonly DateTimeOffset CompletedAtUtc = new(2026, 9, 21, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ControlPlaneCommandState.Succeeded, null, AuditOutcome.Succeeded)]
    [InlineData(ControlPlaneCommandState.Failed, "engine-error", AuditOutcome.Failed)]
    [InlineData(ControlPlaneCommandState.Indeterminate, "response-lost", AuditOutcome.Indeterminate)]
    public async Task RecordResultAsync_WhenTerminal_AuditsCorrelatedOutcome(
        ControlPlaneCommandState state,
        string? failureCode,
        AuditOutcome expectedOutcome)
    {
        var store = new RecordingAgentCommandStore();
        var service = new AgentCommandStatusService(store);

        var result = await service.RecordResultAsync(
            EnvironmentId,
            CommandId,
            state,
            failureCode,
            "revision-02",
            CompletedAtUtc,
            TestContext.Current.CancellationToken);

        Assert.Equal(AgentCommandStatusUpdateResult.Applied, result);
        Assert.Equal(state, store.StatusUpdate?.State);
        Assert.Equal(CommandId, store.AuditEvent?.CommandId);
        Assert.Equal(EnvironmentId, store.AuditEvent?.EnvironmentId);
        Assert.Equal("container.command.result", store.AuditEvent?.Action);
        Assert.Equal($"agent:{EnvironmentId:D}", store.AuditEvent?.ActorId);
        Assert.Equal(expectedOutcome, store.AuditEvent?.Outcome);
        Assert.Equal(failureCode, store.AuditEvent?.FailureCode);
    }

    private sealed class RecordingAgentCommandStore : IAgentCommandStore
    {
        public AgentCommandStatusUpdate? StatusUpdate { get; private set; }
        public AuditEvent? AuditEvent { get; private set; }

        public Task<AgentCommandStatusSnapshot?> GetAsync(
            Guid environmentId,
            Guid commandId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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
            CancellationToken cancellationToken)
        {
            StatusUpdate = update;
            AuditEvent = auditEvent;
            return Task.FromResult(AgentCommandStatusUpdateResult.Applied);
        }

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