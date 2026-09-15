using Dokpod.Bff.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Dokpod.Bff.Tests;

public sealed class DownstreamApiOptionsValidatorTests
{
    [Fact]
    public void Validate_AllowsHttpsBaseUrlWithPositiveLimits()
    {
        var result = Validate(
            new DownstreamApiOptions { BaseUrl = "https://api.example.test/" },
            isDevelopment: false);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsHttpBaseUrlOutsideDevelopment()
    {
        var result = Validate(
            new DownstreamApiOptions { BaseUrl = "http://api.example.test/" },
            isDevelopment: false);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_AllowsLoopbackHttpOnlyInDevelopment()
    {
        var result = Validate(
            new DownstreamApiOptions { BaseUrl = "http://localhost:8080/" },
            isDevelopment: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_AllowsPrivateHttpBaseUrlInDevelopment()
    {
        var result = Validate(
            new DownstreamApiOptions { BaseUrl = "http://api:8080/" },
            isDevelopment: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsNonPositiveTimeoutOrBodyLimit()
    {
        var result = Validate(
            new DownstreamApiOptions
            {
                BaseUrl = "https://api.example.test/",
                TimeoutSeconds = 0
            },
            isDevelopment: false);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsQueryOrFragmentInBaseUrl()
    {
        var result = Validate(
            new DownstreamApiOptions { BaseUrl = "https://api.example.test/?tenant=one" },
            isDevelopment: false);

        Assert.False(result.Succeeded);
    }

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(
        DownstreamApiOptions options,
        bool isDevelopment) => new DownstreamApiOptionsValidator(new TestHostEnvironment(isDevelopment))
        .Validate(null, options);

    private sealed class TestHostEnvironment(bool isDevelopment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = isDevelopment ? Environments.Development : Environments.Production;
        public string ApplicationName { get; set; } = "Dokpod.Bff.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
