using Dokpod.ControlPlane.Application.Agents;
using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class PostgresAgentIdentityRegistry(ControlPlaneDbContext dbContext) : IAgentIdentityRegistry
{
    public async ValueTask<AgentIdentity?> FindByFingerprintAsync(
        string certificateFingerprint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(certificateFingerprint))
        {
            return null;
        }

        var normalizedFingerprint = certificateFingerprint.Trim().ToUpperInvariant();
        var identity = await dbContext.AgentIdentities
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CertificateFingerprint == normalizedFingerprint &&
                    candidate.RevokedAtUtc == null,
                cancellationToken)
            .ConfigureAwait(false);

        return identity is null
            ? null
            : new AgentIdentity(identity.EnvironmentId, identity.CertificateFingerprint);
    }
}