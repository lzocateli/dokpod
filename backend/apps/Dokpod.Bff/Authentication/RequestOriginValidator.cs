using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Authentication;

public sealed class RequestOriginValidator(IOptions<BffSecurityOptions> securityOptions)
{
    private readonly HashSet<string> allowedOrigins = securityOptions.Value.AllowedOrigins.Select(Normalize)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string? origin) => Uri.TryCreate(origin, UriKind.Absolute, out var value)
        && value.AbsolutePath == "/" && string.IsNullOrEmpty(value.Query) && string.IsNullOrEmpty(value.Fragment)
        && allowedOrigins.Contains(Normalize(value));

    private static string Normalize(string value) => Normalize(new Uri(value, UriKind.Absolute));
    private static string Normalize(Uri value) => value.GetLeftPart(UriPartial.Authority);
}