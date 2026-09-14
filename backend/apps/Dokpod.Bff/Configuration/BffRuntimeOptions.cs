namespace Dokpod.Bff.Configuration;

public sealed class BffRuntimeOptions
{
    public const string SectionName = "Bff";
    public string DataProtectionApplicationName { get; init; } = "Dokpod.Bff";
    public long MemoryCacheSizeLimit { get; init; } = 10_000;
    public int LoginRateLimitPermitLimit { get; init; } = 10;
    public int LoginRateLimitWindowSeconds { get; init; } = 60;
    public int LoginRateLimitQueueLimit { get; init; }
    public int TokenRefreshTimeoutSeconds { get; init; } = 10;
    public long TokenRefreshMaxResponseContentBufferSize { get; init; } = 64 * 1024;
    public long DownstreamApiMaxResponseContentBufferSize { get; init; } = 4 * 1024 * 1024;
    public int KeycloakDiscoveryTimeoutSeconds { get; init; } = 3;
    public string AntiforgeryCookieName { get; init; } = "__Host-Dokpod.Antiforgery";
    public string AntiforgeryHeaderName { get; init; } = "X-Dokpod-Antiforgery";
}