using Dokpod.Agent.Application.Engines;
using Dokpod.Domain.Commands;
using System.Collections.Concurrent;

namespace Dokpod.Agent.Application.Commands;

public sealed class AgentCommandProcessor(
    AgentCommandGate gate,
    ICommandJournal journal,
    IContainerEngine engine,
    TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<(Guid EnvironmentId, string ContainerId), SemaphoreSlim> mutationLocks = [];

    public async Task<JournaledCommandResult> ProcessAsync(
        AgentCommand command,
        long activeFencingToken,
        CancellationToken cancellationToken)
    {
        var admission = await gate.AdmitAsync(command, activeFencingToken, cancellationToken);
        if (admission == CommandAdmission.Duplicate)
        {
            return await journal.FindResultAsync(command.EnvironmentId, command.CommandId, cancellationToken)
                ?? CreateResult(command, CommandExecutionState.Indeterminate, "result_pending", null);
        }

        if (admission != CommandAdmission.Accepted)
        {
            return CreateResult(command, CommandExecutionState.Failed, MapAdmissionFailure(admission), null);
        }

        var mutationLock = mutationLocks.GetOrAdd(
            (command.EnvironmentId, command.ContainerId),
            static _ => new SemaphoreSlim(1, 1));
        await mutationLock.WaitAsync(cancellationToken);

        try
        {
            var container = await engine.InspectContainerAsync(command.ContainerId, cancellationToken);
            if (container is null)
            {
                return await SaveAsync(CreateResult(command, CommandExecutionState.Failed, "target_not_found", null));
            }

            if (!string.Equals(container.Revision, command.ExpectedContainerRevision, StringComparison.Ordinal))
            {
                return await SaveAsync(CreateResult(command, CommandExecutionState.Failed, "stale_target", container.Revision));
            }

            try
            {
                var mutation = await engine.ExecuteAsync(command.Kind, command.ContainerId, cancellationToken);
                return await SaveAsync(CreateResult(
                    command,
                    mutation.Succeeded ? CommandExecutionState.Succeeded : CommandExecutionState.Failed,
                    mutation.FailureCode,
                    container.Revision));
            }
            catch (Exception exception) when (exception is not ArgumentException)
            {
                var result = CreateResult(
                    command,
                    CommandExecutionState.Indeterminate,
                    "engine_result_unknown",
                    container.Revision);
                await journal.SaveResultAsync(result, CancellationToken.None);
                return result;
            }
        }
        finally
        {
            mutationLock.Release();
        }

        async Task<JournaledCommandResult> SaveAsync(JournaledCommandResult result)
        {
            await journal.SaveResultAsync(result, cancellationToken);
            return result;
        }
    }

    private JournaledCommandResult CreateResult(
        AgentCommand command,
        CommandExecutionState state,
        string? failureCode,
        string? observedRevision) =>
        new(
            command.EnvironmentId,
            command.CommandId,
            state,
            failureCode,
            observedRevision,
            timeProvider.GetUtcNow());

    private static string MapAdmissionFailure(CommandAdmission admission) => admission switch
    {
        CommandAdmission.Expired => "expired_command",
        CommandAdmission.StaleSession => "stale_session",
        CommandAdmission.ConflictingPayload => "conflicting_payload",
        CommandAdmission.Unsupported => "unsupported_command",
        _ => "command_rejected",
    };
}