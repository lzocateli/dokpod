using System.Collections.Concurrent;
using System.Threading.Channels;
using Dokpod.ControlPlane.Application.Commands;

namespace Dokpod.ControlPlane.Api.Commands;

public sealed class InMemoryAgentCommandDeliveryQueue : IAgentCommandDeliveryQueue
{
    private readonly ConcurrentDictionary<Guid, Channel<PersistedAgentCommand>> queues = [];

    public ValueTask EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return GetQueue(command.Command.EnvironmentId).Writer.WriteAsync(command, cancellationToken);
    }

    public ValueTask<PersistedAgentCommand> DequeueAsync(
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        if (environmentId == Guid.Empty)
        {
            throw new ArgumentException("Environment ID is required.", nameof(environmentId));
        }

        return GetQueue(environmentId).Reader.ReadAsync(cancellationToken);
    }

    private Channel<PersistedAgentCommand> GetQueue(Guid environmentId) =>
        queues.GetOrAdd(
            environmentId,
            static _ => Channel.CreateUnbounded<PersistedAgentCommand>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                }));
}