using Dokpod.ControlPlane.Application.Commands;

namespace Dokpod.ControlPlane.Api.Commands;

public sealed class UnavailableAgentCommandStore : IAgentCommandStore
{
    public Task<AgentCommandEnqueueResult> EnqueueAsync(
        PersistedAgentCommand command,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Agent command storage is not configured.");
}