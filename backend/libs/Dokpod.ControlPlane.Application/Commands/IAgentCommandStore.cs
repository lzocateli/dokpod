using Dokpod.Domain.Commands;

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

public enum AgentCommandEnqueueResult
{
    Created,
    Duplicate,
    ConflictingPayload,
}

public interface IAgentCommandStore
{
    Task<AgentCommandEnqueueResult> EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken);
}