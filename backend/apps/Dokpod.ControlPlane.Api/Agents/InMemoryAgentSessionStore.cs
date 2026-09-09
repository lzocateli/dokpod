using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, AgentSession> activeSessions = [];
    private readonly Dictionary<Guid, TaskCompletionSource> invalidatedSessions = [];
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
            if (activeSessions.TryGetValue(environmentId, out var previousSession) &&
                invalidatedSessions.Remove(previousSession.SessionId, out var invalidated))
            {
                invalidated.TrySetResult();
            }

            activeSessions[environmentId] = session;
            invalidatedSessions[session.SessionId] = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
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

    public Task WaitUntilInactiveAsync(AgentSession session, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!IsActive(session))
            {
                return Task.CompletedTask;
            }

            return invalidatedSessions[session.SessionId].Task.WaitAsync(cancellationToken);
        }
    }

    public ValueTask DeactivateAsync(AgentSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (activeSessions.TryGetValue(session.EnvironmentId, out var active) &&
                active.SessionId == session.SessionId &&
                active.FencingToken == session.FencingToken)
            {
                activeSessions.Remove(session.EnvironmentId);
                if (invalidatedSessions.Remove(session.SessionId, out var invalidated))
                {
                    invalidated.TrySetResult();
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}