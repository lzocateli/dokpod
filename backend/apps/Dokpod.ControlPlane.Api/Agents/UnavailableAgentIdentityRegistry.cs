using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class UnavailableAgentIdentityRegistry : IAgentIdentityRegistry
{
    public ValueTask<AgentIdentity?> FindByFingerprintAsync(
        string certificateFingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<AgentIdentity?>(null);
    }

    public Task<bool> RevokeEnvironmentAsync(
        Guid environmentId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken) =>
        Task.FromException<bool>(new InvalidOperationException("Agent identity persistence is unavailable."));
}