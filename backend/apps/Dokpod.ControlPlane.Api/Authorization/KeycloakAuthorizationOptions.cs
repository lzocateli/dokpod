using Microsoft.Extensions.Options;

namespace Dokpod.ControlPlane.Api.Authorization;

public sealed class KeycloakAuthorizationOptions
{
    public const string SectionName = "Authentication:Keycloak";

    public required string Authority { get; init; }
    public required string Audience { get; init; }
    public int DecisionTimeoutSeconds { get; init; } = 15;
}

public sealed class KeycloakAuthorizationOptionsValidator(IHostEnvironment environment) : IValidateOptions<KeycloakAuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAuthorizationOptions options)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
            || string.IsNullOrWhiteSpace(options.Audience)
            || options.DecisionTimeoutSeconds is < 1 or > 30
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment)
            || (authority.Scheme != Uri.UriSchemeHttps
                && !(authority.IsLoopback || (environment.IsDevelopment() && authority.Scheme == Uri.UriSchemeHttp))))
        {
            return ValidateOptionsResult.Fail(
                "Authentication:Keycloak deve conter Authority absoluta segura, Audience e timeout positivo.");
        }

        return ValidateOptionsResult.Success;
    }
}
