namespace Dokpod.ControlPlane.Infrastructure;

public sealed class AgentIdentityEntity
{
    public Guid EnvironmentId { get; set; }

    public string CertificateFingerprint { get; set; } = string.Empty;

    public DateTimeOffset? RevokedAtUtc { get; set; }
}