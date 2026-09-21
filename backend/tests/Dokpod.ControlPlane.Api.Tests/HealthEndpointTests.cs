using Dokpod.ControlPlane.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public void CreateBuilder_WithoutServerCertificate_FailsClosed()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ApiHost.CreateBuilder(
            [],
            new ApiHostOptions(
                GrpcPort: 7443,
                HealthPort: 8080,
                ServerCertificate: null!,
                ClientCertificateValidation: null,
                LoopbackOnly: true)));

        Assert.Equal("ServerCertificate", exception.ParamName);
    }

    [Fact]
    public void MapEndpoints_RegistersHealthRoutes()
    {
        using var serverCertificate = TestCertificateFactory.CreateServerCertificate();
        var builder = ApiHost.CreateBuilder(
            [],
            new ApiHostOptions(
                GrpcPort: 7443,
                HealthPort: 8080,
                ServerCertificate: serverCertificate,
                ClientCertificateValidation: null,
                LoopbackOnly: true));
        var app = builder.Build();

        ApiHost.MapEndpoints(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/health/live", routes);
        Assert.Contains("/health/ready", routes);
        Assert.Contains("/health/ready/database", routes);
        Assert.Contains("/health/ready/keycloak", routes);
    }

    [Fact]
    public async Task CreateBuilder_WithoutDependenciesConfigured_FailsReadinessClosed()
    {
        using var serverCertificate = TestCertificateFactory.CreateServerCertificate();
        var builder = ApiHost.CreateBuilder(
            ["--urls=http://127.0.0.1:0"],
            new ApiHostOptions(
                GrpcPort: 7443,
                HealthPort: 8080,
                ServerCertificate: serverCertificate,
                ClientCertificateValidation: null,
                LoopbackOnly: true));

        var provider = builder.Services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var readiness = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"),
            TestContext.Current.CancellationToken);
        var liveness = await healthCheckService.CheckHealthAsync(
            _ => false,
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, readiness.Status);
        Assert.Equal(HealthStatus.Healthy, liveness.Status);
        Assert.Equal(HealthStatus.Unhealthy, readiness.Entries["postgresql"].Status);
    }
}