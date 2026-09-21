namespace Dokpod.ControlPlane.Application.Commands;

public interface IAgentCommandDeliveryQueue
{
    ValueTask EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken);

    ValueTask<PersistedAgentCommand> DequeueAsync(
        Guid environmentId,
        CancellationToken cancellationToken);
}