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

    public async Task<bool> RevokeEnvironmentAsync(
        Guid environmentId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken)
    {
        var identities = await dbContext.AgentIdentities
            .Where(identity => identity.EnvironmentId == environmentId && identity.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (identities.Count == 0)
        {
            return false;
        }

        foreach (var identity in identities)
        {
            identity.RevokedAtUtc = revokedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

}