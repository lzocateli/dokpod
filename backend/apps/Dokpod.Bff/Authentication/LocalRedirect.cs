namespace Dokpod.Bff.Authentication;

public static class LocalRedirect
{
    public static string Normalize(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.Contains('\\') ? returnUrl : "/";
}