using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class RejectingAgentIdentityRegistry : IAgentIdentityRegistry
{
    public ValueTask<AgentIdentity?> FindByFingerprintAsync(
        string certificateFingerprint,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<AgentIdentity?>(null);
}