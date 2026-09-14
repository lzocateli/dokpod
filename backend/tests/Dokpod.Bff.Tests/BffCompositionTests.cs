using Dokpod.Bff;
using Dokpod.Bff.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.RateLimiting;
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
        var loginEndpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Single(endpoint => endpoint is RouteEndpoint route && route.RoutePattern.RawText == "/bff/login");

        Assert.Contains("/health/live", routes);
        Assert.Contains("/health/ready", routes);
        Assert.Contains("/bff/login", routes);
        Assert.Contains("/bff/logout", routes);
        Assert.Contains("/bff/session", routes);
        Assert.Contains("/bff/antiforgery", routes);
        Assert.Contains("/api/v1/{**path}", routes);
        Assert.Contains("/hubs/{**path}", routes);
        Assert.Equal("login", loginEndpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
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

    [Fact]
    public void CreateBuilder_ConfiguresSecureOidcCookieAndLoginRateLimit()
    {
        var builder = BffHost.CreateBuilder(["--environment=Development"]);
        builder.Configuration["Authentication:Keycloak:ClientSecret"] = "synthetic-test-secret";
        using var provider = builder.Services.BuildServiceProvider();

        var oidc = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.True(oidc.UsePkce);
        Assert.False(oidc.RequireHttpsMetadata);
        Assert.IsType<HttpClientHandler>(oidc.BackchannelHttpHandler);
        Assert.False(((HttpClientHandler)oidc.BackchannelHttpHandler).AllowAutoRedirect);
        Assert.Equal(typeof(CookieTokenRefreshEvents), cookie.EventsType);
        Assert.IsType<ServerSideTicketStore>(cookie.SessionStore);
        Assert.Contains("__Host-Dokpod.Session", cookie.Cookie.Name);
    }
}