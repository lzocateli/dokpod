using Dokpod.ControlPlane.Api.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class KeycloakAuthorizationOptionsTests
{
    [Fact]
    public void Validate_AcceptsHttpsAuthorityWithAudienceAndPositiveTimeout()
    {
        var result = Validate(new KeycloakAuthorizationOptions
        {
            Authority = "https://keycloak.example.test/realms/dokpod",
            Audience = "dokpod-api",
            DecisionTimeoutSeconds = 15
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_AllowsLoopbackHttpOnlyForDevelopmentConfiguration()
    {
        var result = Validate(new KeycloakAuthorizationOptions
        {
            Authority = "http://localhost:8080/realms/dokpod",
            Audience = "dokpod-api"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsQueryAudienceOrInvalidTimeout()
    {
        var result = Validate(new KeycloakAuthorizationOptions
        {
            Authority = "https://keycloak.example.test/realms/dokpod?x=1",
            Audience = "",
            DecisionTimeoutSeconds = 31
        });

        Assert.False(result.Succeeded);
    }

    private static ValidateOptionsResult Validate(KeycloakAuthorizationOptions options) =>
        new KeycloakAuthorizationOptionsValidator().Validate(null, options);
}
