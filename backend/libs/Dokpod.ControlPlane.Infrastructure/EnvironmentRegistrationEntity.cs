namespace Dokpod.ControlPlane.Infrastructure;

public sealed class EnvironmentRegistrationEntity
{
    public Guid EnvironmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Scopes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
