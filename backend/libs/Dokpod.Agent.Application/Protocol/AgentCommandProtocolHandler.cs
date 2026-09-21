using Dokpod.Agent.Application.Commands;
using Dokpod.Agent.Contracts.V1;
using Dokpod.Domain.Commands;
using Google.Protobuf.WellKnownTypes;

namespace Dokpod.Agent.Application.Protocol;

public sealed class AgentCommandProtocolHandler(AgentCommandProcessor processor)
{
    public async Task HandleAsync(
        Guid environmentId,
        MessageMetadata metadata,
        Dokpod.Agent.Contracts.V1.AgentCommand message,
        Func<CommandAccepted, CancellationToken, Task> sendAcceptanceAsync,
        Func<CommandResult, CancellationToken, Task> sendResultAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendAcceptanceAsync);
        ArgumentNullException.ThrowIfNull(sendResultAsync);

        if (!AgentCommandMapper.TryMap(environmentId, metadata, message, out var command, out var failureCode))
        {
            throw new ArgumentException(failureCode, nameof(message));
        }

        var admission = CommandAdmission.Unsupported;
        var result = await processor.ProcessAsync(
            command!,
            command!.FencingToken,
            async (currentAdmission, currentFailureCode, callbackCancellationToken) =>
            {
                admission = currentAdmission;
                await sendAcceptanceAsync(
                    new CommandAccepted
                    {
                        CommandId = command.CommandId.ToString("D"),
                        Acceptance = MapAcceptance(currentAdmission),
                        FailureCode = currentFailureCode ?? string.Empty,
                    },
                    callbackCancellationToken);
            },
            cancellationToken);

        if (admission is not (CommandAdmission.Accepted or CommandAdmission.Duplicate))
        {
            return;
        }

        await sendResultAsync(
            new CommandResult
            {
                CommandId = result.CommandId.ToString("D"),
                State = MapResultState(result.State),
                FailureCode = result.FailureCode ?? string.Empty,
                ObservedContainerRevision = result.ObservedContainerRevision ?? string.Empty,
                CompletedAt = Timestamp.FromDateTimeOffset(result.CompletedAtUtc),
            },
            cancellationToken);
    }

    private static CommandAcceptance MapAcceptance(CommandAdmission admission) => admission switch
    {
        CommandAdmission.Accepted => CommandAcceptance.Accepted,
        CommandAdmission.Duplicate => CommandAcceptance.Duplicate,
        _ => CommandAcceptance.Rejected,
    };

    private static CommandResultState MapResultState(CommandExecutionState state) => state switch
    {
        CommandExecutionState.Succeeded => CommandResultState.Succeeded,
        CommandExecutionState.Failed => CommandResultState.Failed,
        CommandExecutionState.Indeterminate => CommandResultState.Indeterminate,
        _ => CommandResultState.Unspecified,
    };
}