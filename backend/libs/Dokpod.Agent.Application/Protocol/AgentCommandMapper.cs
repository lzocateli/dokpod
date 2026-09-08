using Dokpod.Agent.Contracts.V1;
using DomainCommand = Dokpod.Domain.Commands.AgentCommand;
using DomainCommandKind = Dokpod.Domain.Commands.AgentCommandKind;

namespace Dokpod.Agent.Application.Protocol;

public static class AgentCommandMapper
{
    public static bool TryMap(
        Guid environmentId,
        MessageMetadata metadata,
        Dokpod.Agent.Contracts.V1.AgentCommand message,
        out DomainCommand? command,
        out string failureCode)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(message);

        command = null;
        failureCode = string.Empty;

        if (environmentId == Guid.Empty || metadata.FencingToken == 0 || metadata.FencingToken > long.MaxValue)
        {
            failureCode = "command_metadata_invalid";
            return false;
        }

        if (!Guid.TryParse(message.CommandId, out var commandId))
        {
            failureCode = "command_id_invalid";
            return false;
        }

        if (!IsImmutableContainerId(message.ContainerId))
        {
            failureCode = "container_id_invalid";
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.ExpectedContainerRevision))
        {
            failureCode = "container_revision_invalid";
            return false;
        }

        if (message.PayloadHash.Length != 32)
        {
            failureCode = "payload_hash_invalid";
            return false;
        }

        DateTimeOffset deadlineUtc;
        try
        {
            deadlineUtc = message.Deadline.ToDateTimeOffset();
        }
        catch (ArgumentException)
        {
            failureCode = "deadline_invalid";
            return false;
        }

        command = new DomainCommand(
            environmentId,
            commandId,
            MapKind(message.Kind),
            message.ContainerId,
            message.ExpectedContainerRevision,
            Convert.ToHexString(message.PayloadHash.Span),
            deadlineUtc,
            (long)metadata.FencingToken);
        return true;
    }

    private static bool IsImmutableContainerId(string containerId) =>
        containerId.Length == 64 && containerId.All(Uri.IsHexDigit);

    private static DomainCommandKind MapKind(CommandKind kind) => kind switch
    {
        CommandKind.StartContainer => DomainCommandKind.StartContainer,
        CommandKind.StopContainer => DomainCommandKind.StopContainer,
        CommandKind.RestartContainer => DomainCommandKind.RestartContainer,
        CommandKind.DeleteContainer => DomainCommandKind.DeleteContainer,
        _ => DomainCommandKind.Unknown,
    };
}