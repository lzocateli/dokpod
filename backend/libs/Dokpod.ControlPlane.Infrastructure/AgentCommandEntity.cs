using Dokpod.ControlPlane.Application.Commands;
using Dokpod.Domain.Commands;

namespace Dokpod.ControlPlane.Infrastructure;

public sealed class AgentCommandEntity
{
    public Guid EnvironmentId { get; set; }
    public Guid CommandId { get; set; }
    public AgentCommandKind Kind { get; set; }
    public string ContainerId { get; set; } = string.Empty;
    public string ExpectedContainerRevision { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; set; }
    public long FencingToken { get; set; }
    public ControlPlaneCommandState State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}