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
        CancellationToken cancellationToken) =>
        await ProcessAsync(
            command,
            activeFencingToken,
            static (_, _, _) => Task.CompletedTask,
            cancellationToken);

    public async Task<JournaledCommandResult> ProcessAsync(
        AgentCommand command,
        long activeFencingToken,
        Func<CommandAdmission, string?, CancellationToken, Task> admissionCallback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admissionCallback);

        var admission = await gate.AdmitAsync(command, activeFencingToken, cancellationToken);
        var admissionFailureCode = admission is CommandAdmission.Accepted or CommandAdmission.Duplicate
            ? null
            : MapAdmissionFailure(admission);
        await admissionCallback(admission, admissionFailureCode, cancellationToken);

        if (admission == CommandAdmission.Duplicate)
        {
            var persistedResult = await journal.FindResultAsync(
                command.EnvironmentId,
                command.CommandId,
                cancellationToken);
            if (persistedResult is not null)
            {
                return persistedResult;
            }
        }

        if (admission is not (CommandAdmission.Accepted or CommandAdmission.Duplicate))
        {
            var rejection = CreateResult(
                command,
                CommandExecutionState.Failed,
                admissionFailureCode,
                null);
            if (admission != CommandAdmission.ConflictingPayload)
            {
                await journal.SaveResultAsync(rejection, CancellationToken.None);
            }

            return rejection;
        }

        var mutationLock = mutationLocks.GetOrAdd(
            (command.EnvironmentId, command.ContainerId),
            static _ => new SemaphoreSlim(1, 1));
        await mutationLock.WaitAsync(cancellationToken);

        try
        {
            if (command.DeadlineUtc <= timeProvider.GetUtcNow())
            {
                return await SaveAsync(CreateResult(command, CommandExecutionState.Failed, "expired_command", null));
            }

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
            await journal.SaveResultAsync(result, CancellationToken.None);
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