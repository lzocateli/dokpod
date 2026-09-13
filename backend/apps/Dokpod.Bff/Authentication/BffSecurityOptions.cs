namespace Dokpod.Bff.Authentication;

public sealed class BffSecurityOptions
{
    public const string SectionName = "Security";
    public string DataProtectionKeysPath { get; init; } = string.Empty;
    public string DataProtectionCertificatePath { get; init; } = string.Empty;
    public string DataProtectionCertificatePassword { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
    public IReadOnlyList<string> TrustedProxies { get; init; } = [];
    public IReadOnlyList<string> TrustedNetworks { get; init; } = [];
}