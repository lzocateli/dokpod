using System.Data;
using Dokpod.ControlPlane.Application.Inventory;
using Dokpod.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class PostgresInventoryProjectionStore(ControlPlaneDbContext dbContext)
    : IInventoryProjectionStore
{
    public async Task<InventoryProjectionPage?> GetPageAsync(
        Guid environmentId,
        string? afterContainerId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var projection = await dbContext.InventoryProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.EnvironmentId == environmentId, cancellationToken)
            .ConfigureAwait(false);
        if (projection is null)
        {
            return null;
        }

        var query = dbContext.InventoryContainers
            .AsNoTracking()
            .Where(container => container.EnvironmentId == environmentId);
        if (!string.IsNullOrWhiteSpace(afterContainerId))
        {
            query = query.Where(container => string.Compare(container.ContainerId, afterContainerId) > 0);
        }

        var entities = await query
            .OrderBy(container => container.ContainerId)
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var hasMore = entities.Count > limit;
        var page = entities.Take(limit).Select(ToDomain).ToArray();
        return new InventoryProjectionPage(
            checked((ulong)projection.Revision),
            projection.ObservedAtUtc,
            page,
            hasMore ? page[^1].ContainerId : null);
    }

    public async Task<InventoryReconciliationResult> ApplyDeltaAsync(
        Guid environmentId,
        InventoryDelta delta,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var projection = await dbContext.InventoryProjections
            .Include(item => item.Containers)
            .SingleOrDefaultAsync(item => item.EnvironmentId == environmentId, cancellationToken)
            .ConfigureAwait(false);

        var current = projection is null
            ? InventorySnapshot.Empty(environmentId, 0)
            : new InventorySnapshot(
                environmentId,
                checked((ulong)projection.Revision),
                projection.Containers.ToDictionary(
                    container => container.ContainerId,
                    container => new ContainerInventory(
                        container.ContainerId,
                        container.Name,
                        container.ImageReference,
                        container.State,
                        container.Revision,
                        container.ObservedAtUtc),
                    StringComparer.Ordinal));

        var result = InventoryReconciler.Apply(current, delta);
        if (result.Outcome != InventoryReconciliationOutcome.Accepted)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        var isNewProjection = projection is null;
        projection ??= new InventoryProjectionEntity
        {
            EnvironmentId = environmentId,
        };
        projection.Revision = checked((long)result.Snapshot.Revision);
        projection.ObservedAtUtc = result.Snapshot.Containers.Count == 0
            ? DateTimeOffset.UtcNow
            : result.Snapshot.Containers.Values.Max(container => container.ObservedAtUtc);

        if (isNewProjection)
        {
            dbContext.InventoryProjections.Add(projection);
        }

        var targetContainers = result.Snapshot.Containers;
        foreach (var existing in projection.Containers.ToArray())
        {
            if (!targetContainers.ContainsKey(existing.ContainerId))
            {
                dbContext.InventoryContainers.Remove(existing);
            }
        }

        foreach (var container in targetContainers.Values)
        {
            var entity = projection.Containers.SingleOrDefault(item => item.ContainerId == container.ContainerId);
            if (entity is null)
            {
                projection.Containers.Add(ToEntity(environmentId, container));
                continue;
            }

            entity.Name = container.Name;
            entity.ImageReference = container.ImageReference;
            entity.State = container.State;
            entity.Revision = container.Revision;
            entity.ObservedAtUtc = container.ObservedAtUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<InventoryReconciliationResult> ReplaceSnapshotAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        var projection = await dbContext.InventoryProjections
            .Include(item => item.Containers)
            .SingleOrDefaultAsync(item => item.EnvironmentId == snapshot.EnvironmentId, cancellationToken)
            .ConfigureAwait(false);

        if (projection is not null && snapshot.Revision < checked((ulong)projection.Revision))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new InventoryReconciliationResult(
                ToSnapshot(projection),
                InventoryReconciliationOutcome.StaleBase,
                "stale_snapshot");
        }

        var isNewProjection = projection is null;
        projection ??= new InventoryProjectionEntity { EnvironmentId = snapshot.EnvironmentId };
        projection.Revision = checked((long)snapshot.Revision);
        projection.ObservedAtUtc = snapshot.Containers.Count == 0
            ? DateTimeOffset.UtcNow
            : snapshot.Containers.Values.Max(container => container.ObservedAtUtc);
        var existingContainers = projection.Containers.ToDictionary(
            container => container.ContainerId,
            StringComparer.Ordinal);
        foreach (var container in snapshot.Containers.Values)
        {
            if (existingContainers.Remove(container.ContainerId, out var entity))
            {
                ApplyToEntity(entity, container);
            }
            else
            {
                projection.Containers.Add(ToEntity(snapshot.EnvironmentId, container));
            }
        }

        foreach (var removed in existingContainers.Values)
        {
            projection.Containers.Remove(removed);
        }

        if (isNewProjection)
        {
            dbContext.InventoryProjections.Add(projection);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new InventoryReconciliationResult(snapshot, InventoryReconciliationOutcome.Accepted);
    }

    private static InventoryContainerEntity ToEntity(Guid environmentId, ContainerInventory container) =>
        new()
        {
            EnvironmentId = environmentId,
            ContainerId = container.ContainerId,
            Name = container.Name,
            ImageReference = container.ImageReference,
            State = container.State,
            Revision = container.Revision,
            ObservedAtUtc = container.ObservedAtUtc,
        };

    private static void ApplyToEntity(InventoryContainerEntity entity, ContainerInventory container)
    {
        entity.Name = container.Name;
        entity.ImageReference = container.ImageReference;
        entity.State = container.State;
        entity.Revision = container.Revision;
        entity.ObservedAtUtc = container.ObservedAtUtc;
    }

    private static ContainerInventory ToDomain(InventoryContainerEntity container) =>
        new(
            container.ContainerId,
            container.Name,
            container.ImageReference,
            container.State,
            container.Revision,
            container.ObservedAtUtc);

    private static InventorySnapshot ToSnapshot(InventoryProjectionEntity projection) =>
        new(
            projection.EnvironmentId,
            checked((ulong)projection.Revision),
            projection.Containers.ToDictionary(
                container => container.ContainerId,
                ToDomain,
                StringComparer.Ordinal));
}