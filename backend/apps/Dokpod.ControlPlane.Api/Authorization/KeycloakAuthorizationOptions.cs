using Microsoft.Extensions.Options;

namespace Dokpod.ControlPlane.Api.Authorization;

public sealed class KeycloakAuthorizationOptions
{
    public const string SectionName = "Authentication:Keycloak";

    public required string Authority { get; init; }
    public string? BackchannelAuthority { get; init; }
    public required string Audience { get; init; }
    public int DecisionTimeoutSeconds { get; init; } = 15;

    public string EffectiveBackchannelAuthority => BackchannelAuthority ?? Authority;
}

public sealed class KeycloakAuthorizationOptionsValidator(IHostEnvironment environment) : IValidateOptions<KeycloakAuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAuthorizationOptions options)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
            || !Uri.TryCreate(options.EffectiveBackchannelAuthority, UriKind.Absolute, out var backchannelAuthority)
            || string.IsNullOrWhiteSpace(options.Audience)
            || options.DecisionTimeoutSeconds is < 1 or > 30
            || !string.IsNullOrEmpty(authority.Query)
            || !string.IsNullOrEmpty(authority.Fragment)
            || !string.IsNullOrEmpty(backchannelAuthority.Query)
            || !string.IsNullOrEmpty(backchannelAuthority.Fragment)
            || !string.Equals(authority.AbsolutePath, backchannelAuthority.AbsolutePath, StringComparison.Ordinal)
            || (authority.Scheme != Uri.UriSchemeHttps
                && !(authority.IsLoopback || (environment.IsDevelopment() && authority.Scheme == Uri.UriSchemeHttp)))
            || (backchannelAuthority.Scheme != Uri.UriSchemeHttps
                && !(backchannelAuthority.IsLoopback
                    || (environment.IsDevelopment() && backchannelAuthority.Scheme == Uri.UriSchemeHttp))))
        {
            return ValidateOptionsResult.Fail(
                "Authentication:Keycloak deve conter autoridades absolutas seguras com o mesmo path, Audience e timeout positivo.");
        }

        return ValidateOptionsResult.Success;
    }
}
