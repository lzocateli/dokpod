using Dokpod.Bff;
using Dokpod.Bff.Authentication;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dokpod.Bff.Tests;

public sealed class BffCompositionTests
{
    [Fact]
    public void MapEndpoints_RegistersLocalBffAndHealthRoutes()
    {
        var builder = BffHost.CreateBuilder(["--environment=Development"]);
        var app = builder.Build();
        BffHost.MapEndpoints(app);
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/health/live", routes);
        Assert.Contains("/health/ready", routes);
        Assert.Contains("/bff/login", routes);
        Assert.Contains("/bff/logout", routes);
        Assert.Contains("/bff/session", routes);
        Assert.Contains("/bff/antiforgery", routes);
        Assert.DoesNotContain("/api/{**path}", routes);
    }

    [Fact]
    public async Task CreateBuilder_RegistersKeycloakReadinessCheck()
    {
        var builder = BffHost.CreateBuilder(["--environment=Development"]);
        using var provider = builder.Services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);
        Assert.Contains("keycloak", report.Entries.Keys);
    }

    [Fact]
    public void CreateBuilder_BindsDokpodConfiguration()
    {
        var builder = BffHost.CreateBuilder(["--environment=Development"]);
        builder.Configuration["Authentication:Keycloak:ClientSecret"] = "synthetic-test-secret";
        using var provider = builder.Services.BuildServiceProvider();
        var security = provider.GetRequiredService<IOptions<BffSecurityOptions>>().Value;
        var keycloak = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        Assert.Equal("dokpod-bff", keycloak.ClientId);
        Assert.Contains("https://localhost", security.AllowedOrigins);
    }
}