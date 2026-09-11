using System.ComponentModel.DataAnnotations.Schema;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class AuditEventEntity
{
    public Guid EventId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid CorrelationId { get; set; }
    public int ActorKind { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Guid EnvironmentId { get; set; }
    public int Outcome { get; set; }
    public string? FailureCode { get; set; }
}
