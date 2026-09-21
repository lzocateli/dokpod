using Dokpod.Domain.Inventory;
using Xunit;

namespace Dokpod.Domain.Tests.Inventory;

public sealed class InventoryReconcilerTests
{
    [Fact]
    public void Apply_WhenBaseRevisionMatches_UpdatesSnapshot()
    {
        var environmentId = Guid.NewGuid();
        var current = InventorySnapshot.Empty(environmentId, 3);
        var delta = new InventoryDelta(
            3,
            4,
            [
                new InventoryChange(
                    InventoryChangeKind.Added,
                    "container-1",
                    new ContainerInventory(
                        "container-1",
                        "api",
                        "nginx:latest",
                        "running",
                        "rev-1",
                        DateTimeOffset.UtcNow)),
            ]);

        var result = InventoryReconciler.Apply(current, delta);

        Assert.Equal(InventoryReconciliationOutcome.Accepted, result.Outcome);
        Assert.Equal(4UL, result.Snapshot.Revision);
        Assert.Contains("container-1", result.Snapshot.Containers.Keys);
    }

    [Fact]
    public void Apply_WhenBaseRevisionIsStale_ReturnsStaleBase()
    {
        var environmentId = Guid.NewGuid();
        var current = InventorySnapshot.Empty(environmentId, 5);
        var delta = new InventoryDelta(3, 6, []);

        var result = InventoryReconciler.Apply(current, delta);

        Assert.Equal(InventoryReconciliationOutcome.StaleBase, result.Outcome);
        Assert.Equal(5UL, result.Snapshot.Revision);
    }

    [Fact]
    public void Apply_WhenSequenceGapDetected_ReturnsGapDetected()
    {
        var environmentId = Guid.NewGuid();
        var current = InventorySnapshot.Empty(environmentId, 1);
        var delta = new InventoryDelta(1, 3, []);

        var result = InventoryReconciler.Apply(current, delta);

        Assert.Equal(InventoryReconciliationOutcome.SequenceGap, result.Outcome);
        Assert.Equal(1UL, result.Snapshot.Revision);
    }

    [Fact]
    public void Apply_WhenRevisionDoesNotAdvance_ReturnsInvalidDelta()
    {
        var environmentId = Guid.NewGuid();
        var current = InventorySnapshot.Empty(environmentId, 3);
        var delta = new InventoryDelta(3, 3, []);

        var result = InventoryReconciler.Apply(current, delta);

        Assert.Equal(InventoryReconciliationOutcome.InvalidDelta, result.Outcome);
        Assert.Equal("non_monotonic_revision", result.FailureCode);
        Assert.Equal(3UL, result.Snapshot.Revision);
    }
}