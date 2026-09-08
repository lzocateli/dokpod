using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class RejectingAgentSessionStore : IAgentSessionStore
{
    public ValueTask<AgentSession> ActivateAsync(Guid environmentId, CancellationToken cancellationToken) =>
        ValueTask.FromException<AgentSession>(new InvalidOperationException("agent_session_store_not_configured"));
}