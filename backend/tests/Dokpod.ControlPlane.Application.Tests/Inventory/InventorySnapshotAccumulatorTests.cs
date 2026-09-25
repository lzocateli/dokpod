using Dokpod.ControlPlane.Application.Inventory;
using Dokpod.Domain.Inventory;
using Xunit;

namespace Dokpod.ControlPlane.Application.Tests.Inventory;

public sealed class InventorySnapshotAccumulatorTests
{
    [Fact]
    public void AddPage_WhenPagesAreSequential_CompletesSnapshot()
    {
        var environmentId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid().ToString("D");
        var accumulator = new InventorySnapshotAccumulator(environmentId);

        var first = accumulator.AddPage(new InventorySnapshotPage(
            snapshotId,
            7,
            0,
            false,
            [CreateContainer("container-1")]));
        var last = accumulator.AddPage(new InventorySnapshotPage(
            snapshotId,
            7,
            1,
            true,
            [CreateContainer("container-2")]));

        Assert.Equal(InventorySnapshotPageOutcome.Accepted, first.Outcome);
        Assert.Equal(InventorySnapshotPageOutcome.Completed, last.Outcome);
        Assert.Equal(7UL, last.Snapshot!.Revision);
        Assert.Equal(2, last.Snapshot.Containers.Count);
    }

    [Fact]
    public void AddPage_WhenPageIsOutOfOrder_RejectsSnapshot()
    {
        var accumulator = new InventorySnapshotAccumulator(Guid.NewGuid());
        var page = new InventorySnapshotPage(
            Guid.NewGuid().ToString("D"),
            1,
            1,
            true,
            []);

        var result = accumulator.AddPage(page);

        Assert.Equal(InventorySnapshotPageOutcome.Invalid, result.Outcome);
        Assert.Equal("snapshot_page_out_of_order", result.FailureCode);
    }

    [Fact]
    public void AddPage_WhenSnapshotIdentityChanges_RejectsSnapshot()
    {
        var accumulator = new InventorySnapshotAccumulator(Guid.NewGuid());
        accumulator.AddPage(new InventorySnapshotPage(
            Guid.NewGuid().ToString("D"),
            1,
            0,
            false,
            []));

        var result = accumulator.AddPage(new InventorySnapshotPage(
            Guid.NewGuid().ToString("D"),
            1,
            1,
            true,
            []));

        Assert.Equal(InventorySnapshotPageOutcome.Invalid, result.Outcome);
        Assert.Equal("snapshot_identity_changed", result.FailureCode);
    }

    [Fact]
    public void AddPage_WhenPageContainsDuplicateContainer_RejectsWithoutPartialMutation()
    {
        var accumulator = new InventorySnapshotAccumulator(Guid.NewGuid());
        var snapshotId = Guid.NewGuid().ToString("D");
        var duplicate = CreateContainer("container-1");

        var invalid = accumulator.AddPage(new InventorySnapshotPage(
            snapshotId,
            1,
            0,
            false,
            [duplicate, duplicate]));
        var valid = accumulator.AddPage(new InventorySnapshotPage(
            snapshotId,
            1,
            0,
            true,
            [duplicate]));

        Assert.Equal(InventorySnapshotPageOutcome.Invalid, invalid.Outcome);
        Assert.Equal("snapshot_container_duplicate", invalid.FailureCode);
        Assert.Equal(InventorySnapshotPageOutcome.Completed, valid.Outcome);
        Assert.Single(valid.Snapshot!.Containers);
    }

    [Fact]
    public void AddPage_AfterCompletedSnapshot_StartsNextSnapshot()
    {
        var accumulator = new InventorySnapshotAccumulator(Guid.NewGuid());
        var first = accumulator.AddPage(new InventorySnapshotPage(
            Guid.NewGuid().ToString("D"),
            1,
            0,
            true,
            [CreateContainer("container-1")]));

        var second = accumulator.AddPage(new InventorySnapshotPage(
            Guid.NewGuid().ToString("D"),
            2,
            0,
            true,
            [CreateContainer("container-2")]));

        Assert.Equal(InventorySnapshotPageOutcome.Completed, first.Outcome);
        Assert.Equal(InventorySnapshotPageOutcome.Completed, second.Outcome);
        Assert.Equal(2UL, second.Snapshot!.Revision);
        Assert.DoesNotContain("container-1", second.Snapshot.Containers);
        Assert.Contains("container-2", second.Snapshot.Containers);
    }

    private static ContainerInventory CreateContainer(string containerId) =>
        new(containerId, containerId, "fixture:latest", "Running", "revision-1", DateTimeOffset.UtcNow);
}