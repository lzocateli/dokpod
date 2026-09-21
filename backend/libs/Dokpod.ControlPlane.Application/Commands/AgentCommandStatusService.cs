namespace Dokpod.ControlPlane.Application.Commands;

public sealed class AgentCommandStatusService(IAgentCommandStore commandStore)
{
    public Task<AgentCommandStatusUpdateResult> MarkDispatchedAsync(
        Guid environmentId,
        Guid commandId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            environmentId,
            commandId,
            ControlPlaneCommandState.Dispatched,
            null,
            null,
            updatedAtUtc,
            cancellationToken);

    public Task<AgentCommandStatusUpdateResult> RecordAcceptanceAsync(
        Guid environmentId,
        Guid commandId,
        bool accepted,
        string? failureCode,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!accepted && string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("Rejected command acceptance requires a failure code.", nameof(failureCode));
        }

        return ApplyAsync(
            environmentId,
            commandId,
            accepted ? ControlPlaneCommandState.Accepted : ControlPlaneCommandState.Failed,
            accepted ? null : failureCode,
            null,
            updatedAtUtc,
            cancellationToken);
    }

    public Task<AgentCommandStatusUpdateResult> RecordResultAsync(
        Guid environmentId,
        Guid commandId,
        ControlPlaneCommandState state,
        string? failureCode,
        string? observedContainerRevision,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        if (state is not (ControlPlaneCommandState.Succeeded or
            ControlPlaneCommandState.Failed or
            ControlPlaneCommandState.Indeterminate))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (state != ControlPlaneCommandState.Succeeded && string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("Failed or indeterminate command result requires a failure code.", nameof(failureCode));
        }

        return ApplyAsync(
            environmentId,
            commandId,
            state,
            failureCode,
            observedContainerRevision,
            completedAtUtc,
            cancellationToken);
    }

    private Task<AgentCommandStatusUpdateResult> ApplyAsync(
        Guid environmentId,
        Guid commandId,
        ControlPlaneCommandState state,
        string? failureCode,
        string? observedContainerRevision,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        if (environmentId == Guid.Empty || commandId == Guid.Empty || updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Command status identity and UTC timestamp are required.");
        }

        if (failureCode?.Length > 128 || observedContainerRevision?.Length > 255)
        {
            throw new ArgumentException("Command status fields exceed their limits.");
        }

        return commandStore.ApplyStatusAsync(
            new AgentCommandStatusUpdate(
                environmentId,
                commandId,
                state,
                failureCode,
                observedContainerRevision,
                updatedAtUtc),
            cancellationToken);
    }
}