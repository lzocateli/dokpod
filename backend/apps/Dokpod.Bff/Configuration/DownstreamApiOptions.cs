namespace Dokpod.Bff.Configuration;

public sealed class DownstreamApiOptions
{
    public const string SectionName = "Downstream:Api";
    public string BaseUrl { get; init; } = string.Empty;
}