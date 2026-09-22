using Dokpod.ControlPlane.Api.Authorization;
using Microsoft.Extensions.Hosting;
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
    public void Validate_AcceptsDevelopmentBackchannelWithMatchingRealmPath()
    {
        var result = Validate(new KeycloakAuthorizationOptions
        {
            Authority = "https://localhost:7443/realms/dokpod",
            BackchannelAuthority = "http://keycloak:8080/realms/dokpod",
            Audience = "dokpod-api"
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsBackchannelWithDifferentRealmPath()
    {
        var result = Validate(new KeycloakAuthorizationOptions
        {
            Authority = "https://localhost:7443/realms/dokpod",
            BackchannelAuthority = "http://keycloak:8080/realms/other",
            Audience = "dokpod-api"
        });

        Assert.False(result.Succeeded);
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
    public void Validate_AllowsPrivateHttpAuthorityForDevelopmentConfiguration()
    {
        var result = Validate(new KeycloakAuthorizationOptions
        {
            Authority = "http://keycloak:8080/realms/dokpod",
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
        new KeycloakAuthorizationOptionsValidator(new TestHostEnvironment()).Validate(null, options);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Dokpod.ControlPlane.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
