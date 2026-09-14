namespace Dokpod.Bff.Configuration;

public sealed class DownstreamApiOptions
{
    public const string SectionName = "Downstream:Api";
    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 15;
    public long MaxRequestContentLengthBytes { get; init; } = 4 * 1024 * 1024;
    public long MaxResponseContentLengthBytes { get; init; } = 4 * 1024 * 1024;
}