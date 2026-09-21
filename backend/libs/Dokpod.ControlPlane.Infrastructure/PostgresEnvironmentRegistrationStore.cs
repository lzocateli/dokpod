using System.Text.Json;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.Domain.Environments;
using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class PostgresEnvironmentRegistrationStore(ControlPlaneDbContext dbContext)
    : IEnvironmentRegistrationStore
{
    public async Task<EnvironmentRegistrationPage> ListAsync(
        Guid? afterEnvironmentId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.EnvironmentRegistrations.AsNoTracking();
        if (afterEnvironmentId is not null)
        {
            query = query.Where(environment =>
                environment.EnvironmentId.CompareTo(afterEnvironmentId.Value) > 0);
        }

        var entities = await query
            .OrderBy(environment => environment.EnvironmentId)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var hasMore = entities.Count > limit;
        if (hasMore)
        {
            entities.RemoveAt(limit);
        }

        var registrations = entities
            .Select(ToRegistration)
            .ToArray();
        return new EnvironmentRegistrationPage(
            registrations,
            hasMore ? registrations[^1].EnvironmentId : null);
    }

    public async Task<EnvironmentRegistration?> GetAsync(
        Guid environmentId,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.EnvironmentRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                environment => environment.EnvironmentId == environmentId,
                cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        return ToRegistration(entity);
    }

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

    private static EnvironmentRegistration ToRegistration(EnvironmentRegistrationEntity entity)
    {
        var scopes = JsonSerializer.Deserialize<string[]>(entity.Scopes) ?? [];
        return EnvironmentRegistration.Create(
            entity.EnvironmentId,
            entity.Name,
            entity.Host,
            entity.Enabled,
            scopes);
    }
}
