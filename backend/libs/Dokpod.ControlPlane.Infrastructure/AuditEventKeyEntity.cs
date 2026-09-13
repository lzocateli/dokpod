namespace Dokpod.ControlPlane.Infrastructure;

public sealed class AuditEventKeyEntity
{
    public Guid EventId { get; set; }
    public byte[] PayloadHash { get; set; } = [];
}