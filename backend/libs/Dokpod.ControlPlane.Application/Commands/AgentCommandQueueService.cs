using Dokpod.Domain.Commands;

namespace Dokpod.ControlPlane.Application.Commands;

public sealed class AgentCommandQueueService(
    IAgentCommandStore commandStore,
    IAgentCommandDeliveryQueue deliveryQueue,
    TimeProvider timeProvider)
{
    public async Task<AgentCommandEnqueueResult> EnqueueAsync(
        AgentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        if (command.EnvironmentId == Guid.Empty || command.CommandId == Guid.Empty)
        {
            throw new ArgumentException("Command and environment IDs are required.", nameof(command));
        }

        if (command.Kind is < AgentCommandKind.StartContainer or > AgentCommandKind.DeleteContainer)
        {
            throw new ArgumentException("Command kind is not supported.", nameof(command));
        }

        if (command.ContainerId.Length != 64 || !command.ContainerId.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Command container ID is invalid.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.ExpectedContainerRevision) ||
            command.ExpectedContainerRevision.Length > 255)
        {
            throw new ArgumentException("Command container revision is invalid.", nameof(command));
        }

        if (command.PayloadHash.Length != 64 ||
            !command.PayloadHash.All(character =>
                character is >= '0' and <= '9' or >= 'A' and <= 'F'))
        {
            throw new ArgumentException("Command payload hash must be canonical SHA-256 hexadecimal.", nameof(command));
        }

        if (command.FencingToken <= 0 || command.DeadlineUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Command fencing token and UTC deadline are invalid.", nameof(command));
        }

        if (command.DeadlineUtc <= now)
        {
            throw new ArgumentException("Command deadline must be in the future.", nameof(command));
        }

        var persistedCommand = new PersistedAgentCommand(
            command,
            ControlPlaneCommandState.Pending,
            now,
            now);

        var result = await commandStore.EnqueueAsync(persistedCommand, cancellationToken)
            .ConfigureAwait(false);
        if (result == AgentCommandEnqueueResult.Created)
        {
            await deliveryQueue.EnqueueAsync(persistedCommand, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }
}