using Dokpod.ControlPlane.Application.Inventory;
using Dokpod.Domain.Inventory;

namespace Dokpod.ControlPlane.Api.Agents;

public sealed class UnavailableInventoryProjectionStore : IInventoryProjectionStore
{
    public Task<InventoryReconciliationResult> ApplyDeltaAsync(
        Guid environmentId,
        InventoryDelta delta,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Inventory projection storage is not configured.");

    public Task<InventoryReconciliationResult> ReplaceSnapshotAsync(
        InventorySnapshot snapshot,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Inventory projection storage is not configured.");

    public Task<InventoryProjectionPage?> GetPageAsync(
        Guid environmentId,
        string? afterContainerId,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Inventory projection storage is not configured.");
}