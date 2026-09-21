using System.Collections.ObjectModel;
using Dokpod.Domain.Inventory;

namespace Dokpod.ControlPlane.Application.Inventory;

public sealed record InventorySnapshotPage(
    string SnapshotId,
    ulong InventoryRevision,
    uint PageNumber,
    bool IsLastPage,
    IReadOnlyList<ContainerInventory> Containers);

public enum InventorySnapshotPageOutcome
{
    Accepted,
    Completed,
    Invalid,
}

public sealed record InventorySnapshotPageResult(
    InventorySnapshotPageOutcome Outcome,
    InventorySnapshot? Snapshot = null,
    string? FailureCode = null);

public sealed class InventorySnapshotAccumulator(
    Guid environmentId,
    int maximumPages = 1_000,
    int maximumContainers = 10_000)
{
    private readonly Dictionary<string, ContainerInventory> containers = new(StringComparer.Ordinal);
    private string? snapshotId;
    private ulong inventoryRevision;
    private uint nextPageNumber;
    private bool completed;

    public InventorySnapshotPageResult AddPage(InventorySnapshotPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (completed || page.PageNumber != nextPageNumber || page.PageNumber >= maximumPages)
        {
            return Invalid("snapshot_page_out_of_order");
        }

        if (!Guid.TryParseExact(page.SnapshotId, "D", out _) || page.InventoryRevision == 0)
        {
            return Invalid("snapshot_identity_invalid");
        }

        if (snapshotId is null)
        {
            snapshotId = page.SnapshotId;
            inventoryRevision = page.InventoryRevision;
        }
        else if (!string.Equals(snapshotId, page.SnapshotId, StringComparison.Ordinal) ||
            inventoryRevision != page.InventoryRevision)
        {
            return Invalid("snapshot_identity_changed");
        }

        if (page.Containers is null || containers.Count + page.Containers.Count > maximumContainers)
        {
            return Invalid("snapshot_container_limit_exceeded");
        }

        var pageContainers = new Dictionary<string, ContainerInventory>(StringComparer.Ordinal);
        foreach (var container in page.Containers)
        {
            if (string.IsNullOrWhiteSpace(container.ContainerId) ||
                containers.ContainsKey(container.ContainerId) ||
                !pageContainers.TryAdd(container.ContainerId, container))
            {
                return Invalid("snapshot_container_duplicate");
            }
        }

        foreach (var container in pageContainers)
        {
            containers.Add(container.Key, container.Value);
        }

        nextPageNumber++;
        if (!page.IsLastPage)
        {
            return new InventorySnapshotPageResult(InventorySnapshotPageOutcome.Accepted);
        }

        completed = true;
        var snapshot = new InventorySnapshot(
            environmentId,
            inventoryRevision,
            new ReadOnlyDictionary<string, ContainerInventory>(containers));
        return new InventorySnapshotPageResult(InventorySnapshotPageOutcome.Completed, snapshot);
    }

    private static InventorySnapshotPageResult Invalid(string failureCode) =>
        new(InventorySnapshotPageOutcome.Invalid, FailureCode: failureCode);
}