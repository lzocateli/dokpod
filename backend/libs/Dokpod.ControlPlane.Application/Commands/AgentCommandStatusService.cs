using Dokpod.Domain.Auditing;

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

        var state = accepted ? ControlPlaneCommandState.Accepted : ControlPlaneCommandState.Failed;
        return accepted
            ? ApplyAsync(
                environmentId,
                commandId,
                state,
                null,
                null,
                updatedAtUtc,
                cancellationToken)
            : ApplyTerminalAsync(
                environmentId,
                commandId,
                state,
                failureCode,
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

        return ApplyTerminalAsync(
            environmentId,
            commandId,
            state,
            failureCode,
            observedContainerRevision,
            completedAtUtc,
            cancellationToken);
    }

    private Task<AgentCommandStatusUpdateResult> ApplyTerminalAsync(
        Guid environmentId,
        Guid commandId,
        ControlPlaneCommandState state,
        string? failureCode,
        string? observedContainerRevision,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        ValidateUpdate(environmentId, commandId, failureCode, observedContainerRevision, updatedAtUtc);
        var update = new AgentCommandStatusUpdate(
            environmentId,
            commandId,
            state,
            failureCode,
            observedContainerRevision,
            updatedAtUtc);
        var auditEvent = AuditEvent.Create(
            Guid.NewGuid(),
            updatedAtUtc,
            Guid.NewGuid(),
            AuditActorKind.Agent,
            $"agent:{environmentId:D}",
            "container.command.result",
            environmentId,
            state switch
            {
                ControlPlaneCommandState.Succeeded => AuditOutcome.Succeeded,
                ControlPlaneCommandState.Failed => AuditOutcome.Failed,
                ControlPlaneCommandState.Indeterminate => AuditOutcome.Indeterminate,
                _ => throw new ArgumentOutOfRangeException(nameof(state)),
            },
            failureCode,
            commandId);
        return commandStore.ApplyStatusAuditedAsync(update, auditEvent, cancellationToken);
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
        ValidateUpdate(environmentId, commandId, failureCode, observedContainerRevision, updatedAtUtc);

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

    private static void ValidateUpdate(
        Guid environmentId,
        Guid commandId,
        string? failureCode,
        string? observedContainerRevision,
        DateTimeOffset updatedAtUtc)
    {
        if (environmentId == Guid.Empty || commandId == Guid.Empty || updatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Command status identity and UTC timestamp are required.");
        }

        if (failureCode?.Length > 128 || observedContainerRevision?.Length > 255)
        {
            throw new ArgumentException("Command status fields exceed their limits.");
        }
    }
}