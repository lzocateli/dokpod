using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dokpod.Bff.Authentication;

public sealed class ConfigureOpenIdConnectOptions(
    IOptions<KeycloakOptions> keycloakOptions,
    IHostEnvironment environment)
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options) => Configure(OpenIdConnectDefaults.AuthenticationScheme, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (!string.Equals(name, OpenIdConnectDefaults.AuthenticationScheme, StringComparison.Ordinal)) return;
        var keycloak = keycloakOptions.Value;
        options.Authority = keycloak.Authority.TrimEnd('/');
        options.ClientId = keycloak.ClientId;
        options.ClientSecret = keycloak.ClientSecret;
        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.UsePkce = true;
        options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.CallbackPath = keycloak.CallbackPath;
        options.SignedOutCallbackPath = keycloak.SignedOutCallbackPath;
        options.BackchannelHttpHandler = KeycloakHttpMessageHandlerFactory.Create(
            keycloak.Authority,
            environment.IsDevelopment());
        options.RequireHttpsMetadata = Uri.TryCreate(keycloak.Authority, UriKind.Absolute, out var authority)
            && string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username",
            ValidateIssuer = true
        };
    }
}