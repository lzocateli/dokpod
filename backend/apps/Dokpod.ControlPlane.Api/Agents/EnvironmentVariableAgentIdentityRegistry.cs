using Dokpod.ControlPlane.Application.Agents;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class EnvironmentVariableAgentIdentityRegistry : IAgentIdentityRegistry
{
    public ValueTask<AgentIdentity?> FindByFingerprintAsync(
        string certificateFingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuredFingerprint = Environment.GetEnvironmentVariable("DOKPOD_AGENT_CERTIFICATE_FINGERPRINT");
        var configuredEnvironmentId = Environment.GetEnvironmentVariable("DOKPOD_AGENT_ENVIRONMENT_ID");

        if (!string.Equals(configuredFingerprint, certificateFingerprint, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(configuredEnvironmentId, out var environmentId))
        {
            return ValueTask.FromResult<AgentIdentity?>(null);
        }

        return ValueTask.FromResult<AgentIdentity?>(new AgentIdentity(environmentId, certificateFingerprint));
    }
}