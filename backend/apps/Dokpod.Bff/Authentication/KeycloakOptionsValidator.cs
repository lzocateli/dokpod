using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Authentication;

public sealed class KeycloakOptionsValidator(IHostEnvironment environment) : IValidateOptions<KeycloakOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakOptions options)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority))
            return ValidateOptionsResult.Fail("Authentication:Keycloak:Authority deve ser uma URL absoluta.");
        var path = authority.AbsolutePath.TrimEnd('/');
        if (!path.Contains("/realms/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/realms/master", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(authority.Query) || !string.IsNullOrEmpty(authority.Fragment))
            return ValidateOptionsResult.Fail("O BFF deve usar um realm Dokpod dedicado diferente de master.");
        if (!string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !(environment.IsDevelopment() && authority.IsLoopback))
            return ValidateOptionsResult.Fail("O issuer do Keycloak deve usar HTTPS fora do desenvolvimento local.");
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
            return ValidateOptionsResult.Fail("ClientId e ClientSecret do BFF são obrigatórios.");
        return IsLocalPath(options.CallbackPath) && IsLocalPath(options.SignedOutCallbackPath)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Os callbacks OIDC devem ser paths locais absolutos.");
    }

    private static bool IsLocalPath(string path) =>
        path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\');
}