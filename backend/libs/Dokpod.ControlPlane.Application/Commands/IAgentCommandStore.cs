using Dokpod.Domain.Commands;
using Dokpod.Domain.Auditing;

namespace Dokpod.ControlPlane.Application.Commands;

public enum ControlPlaneCommandState
{
    Pending = 0,
    Dispatched = 1,
    Accepted = 2,
    Succeeded = 3,
    Failed = 4,
    Indeterminate = 5,
}

public sealed record PersistedAgentCommand(
    AgentCommand Command,
    ControlPlaneCommandState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AgentCommandStatusSnapshot(
    Guid EnvironmentId,
    Guid CommandId,
    AgentCommandKind Kind,
    string ContainerId,
    string ExpectedContainerRevision,
    ControlPlaneCommandState State,
    string? FailureCode,
    string? ObservedContainerRevision,
    DateTimeOffset DeadlineUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public enum AgentCommandEnqueueResult
{
    Created,
    Duplicate,
    ConflictingPayload,
}

public sealed record AgentCommandStatusUpdate(
    Guid EnvironmentId,
    Guid CommandId,
    ControlPlaneCommandState State,
    string? FailureCode,
    string? ObservedContainerRevision,
    DateTimeOffset UpdatedAtUtc);

public enum AgentCommandStatusUpdateResult
{
    Applied,
    Duplicate,
    NotFound,
    InvalidTransition,
}

public interface IAgentCommandStore
{
    Task<AgentCommandStatusSnapshot?> GetAsync(
        Guid environmentId,
        Guid commandId,
        CancellationToken cancellationToken);

    Task<AgentCommandEnqueueResult> EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken);

    Task<AgentCommandEnqueueResult> EnqueueAuditedAsync(
        PersistedAgentCommand command,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<AgentCommandStatusUpdateResult> ApplyStatusAsync(
        AgentCommandStatusUpdate update,
        CancellationToken cancellationToken);

    Task<AgentCommandStatusUpdateResult> ApplyStatusAuditedAsync(
        AgentCommandStatusUpdate update,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<int> ExpireNonTerminalAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PersistedAgentCommand>> ClaimDispatchableAsync(
        Guid environmentId,
        long activeFencingToken,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}