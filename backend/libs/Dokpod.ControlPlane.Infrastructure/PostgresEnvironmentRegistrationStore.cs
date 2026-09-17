using System.Text.Json;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Environments;
using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class PostgresEnvironmentRegistrationStore(ControlPlaneDbContext dbContext)
    : IEnvironmentRegistrationStore
{
    public async Task<EnvironmentRegistrationStoreResult> CreateAsync(
        EnvironmentRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var existing = await dbContext.EnvironmentRegistrations
            .SingleOrDefaultAsync(
                environment => environment.EnvironmentId == registration.EnvironmentId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return EnvironmentRegistrationStoreResult.AlreadyExists;
        }

        dbContext.EnvironmentRegistrations.Add(new EnvironmentRegistrationEntity
        {
            EnvironmentId = registration.EnvironmentId,
            Name = registration.Name,
            Host = registration.Host,
            Enabled = registration.Enabled,
            Scopes = JsonSerializer.Serialize(registration.Scopes.Order(StringComparer.Ordinal)),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return EnvironmentRegistrationStoreResult.Created;
        }
        catch (DbUpdateException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException("Environment registration could not be persisted.", exception);
        }
    }
}
