using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, AgentSession> activeSessions = [];
    private long fencingToken;

    public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var session = new AgentSession(
                environmentId,
                Guid.NewGuid(),
                checked((ulong)Interlocked.Increment(ref fencingToken)));
            activeSessions[environmentId] = session;
            return ValueTask.FromResult(session);
        }
    }

    public bool IsActive(AgentSession session)
    {
        lock (gate)
        {
            return activeSessions.TryGetValue(session.EnvironmentId, out var active) &&
                active.SessionId == session.SessionId &&
                active.FencingToken == session.FencingToken;
        }
    }
}