namespace Dokpod.Domain.Commands;

public enum AgentCommandKind
{
    Unknown = 0,
    StartContainer = 1,
    StopContainer = 2,
    RestartContainer = 3,
    DeleteContainer = 4,
}

public sealed record AgentCommand(
    Guid EnvironmentId,
    Guid CommandId,
    AgentCommandKind Kind,
    string ContainerId,
    string ExpectedContainerRevision,
    string PayloadHash,
    DateTimeOffset DeadlineUtc,
    long FencingToken);

public enum CommandAdmission
{
    Accepted,
    Duplicate,
    Expired,
    StaleSession,
    ConflictingPayload,
    Unsupported,
}

public sealed record JournaledCommand(
    Guid EnvironmentId,
    Guid CommandId,
    string PayloadHash,
    CommandAdmission Admission);

public enum CommandExecutionState
{
    Succeeded,
    Failed,
    Indeterminate,
}

public sealed record JournaledCommandResult(
    Guid EnvironmentId,
    Guid CommandId,
    CommandExecutionState State,
    string? FailureCode,
    string? ObservedContainerRevision,
    DateTimeOffset CompletedAtUtc);

public interface ICommandJournal
{
    Task<JournaledCommand?> AppendIfAbsentAsync(
        JournaledCommand command,
        CancellationToken cancellationToken);

    Task<JournaledCommandResult?> FindResultAsync(
        Guid environmentId,
        Guid commandId,
        CancellationToken cancellationToken);

    Task SaveResultAsync(
        JournaledCommandResult result,
        CancellationToken cancellationToken);
}

public sealed class AgentCommandGate(ICommandJournal journal, TimeProvider timeProvider)
{
    public async Task<CommandAdmission> AdmitAsync(
        AgentCommand command,
        long activeFencingToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var admission = command.Kind switch
        {
            AgentCommandKind.Unknown => CommandAdmission.Unsupported,
            _ when command.DeadlineUtc <= timeProvider.GetUtcNow() => CommandAdmission.Expired,
            _ when command.FencingToken != activeFencingToken => CommandAdmission.StaleSession,
            _ => CommandAdmission.Accepted,
        };

        var existing = await journal.AppendIfAbsentAsync(
            new JournaledCommand(command.EnvironmentId, command.CommandId, command.PayloadHash, admission),
            cancellationToken);

        if (existing is null)
        {
            return admission;
        }

        return string.Equals(existing.PayloadHash, command.PayloadHash, StringComparison.Ordinal)
            ? CommandAdmission.Duplicate
            : CommandAdmission.ConflictingPayload;
    }
}