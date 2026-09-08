using System.Collections.Concurrent;
using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<Guid, long> fencingTokens = new();

    public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var fencingToken = checked((ulong)fencingTokens.AddOrUpdate(environmentId, 1, (_, current) => checked(current + 1)));
        return ValueTask.FromResult(new AgentSession(environmentId, Guid.NewGuid(), fencingToken));
    }
}