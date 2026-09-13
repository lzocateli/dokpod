namespace Dokpod.Bff.Configuration;

public sealed class BffRuntimeOptions
{
    public const string SectionName = "Bff";
    public string DataProtectionApplicationName { get; init; } = "Dokpod.Bff";
    public long MemoryCacheSizeLimit { get; init; } = 10_000;
    public int KeycloakDiscoveryTimeoutSeconds { get; init; } = 3;
    public string AntiforgeryCookieName { get; init; } = "__Host-Dokpod.Antiforgery";
    public string AntiforgeryHeaderName { get; init; } = "X-Dokpod-Antiforgery";
}