namespace Dokpod.ControlPlane.Infrastructure;

public sealed class InventoryProjectionEntity
{
    public Guid EnvironmentId { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public List<InventoryContainerEntity> Containers { get; set; } = [];
}

public sealed class InventoryContainerEntity
{
    public Guid EnvironmentId { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ImageReference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; set; }
    public InventoryProjectionEntity Projection { get; set; } = null!;
}