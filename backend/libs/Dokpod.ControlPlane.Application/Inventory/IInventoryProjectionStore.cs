using Dokpod.Domain.Inventory;

namespace Dokpod.ControlPlane.Application.Inventory;

public interface IInventoryProjectionStore
{
    Task<InventoryReconciliationResult> ApplyDeltaAsync(
        Guid environmentId,
        InventoryDelta delta,
        CancellationToken cancellationToken);

    Task<InventoryReconciliationResult> ReplaceSnapshotAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken);

    Task<InventoryProjectionPage?> GetPageAsync(
        Guid environmentId,
        string? afterContainerId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record InventoryProjectionPage(
    ulong Revision,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<ContainerInventory> Containers,
    string? NextContainerId);

public sealed class InventoryProjectionService(IInventoryProjectionStore store)
{
    public Task<InventoryReconciliationResult> ApplyDeltaAsync(
        Guid environmentId,
        InventoryDelta delta,
        CancellationToken cancellationToken) =>
        store.ApplyDeltaAsync(environmentId, delta, cancellationToken);

    public Task<InventoryReconciliationResult> ReplaceSnapshotAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken) =>
        store.ReplaceSnapshotAsync(snapshot, cancellationToken);

    public Task<InventoryProjectionPage?> GetPageAsync(
        Guid environmentId,
        string? afterContainerId,
        int limit,
        CancellationToken cancellationToken) =>
        store.GetPageAsync(environmentId, afterContainerId, limit, cancellationToken);
}