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
    public void MapEndpoints_RegistersHealthRoutes()
    {
        var builder = ApiHost.CreateBuilder(
            [],
            new ApiHostOptions(
                GrpcPort: 7443,
                HealthPort: 8080,
                ServerCertificate: null,
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
    }

    [Fact]
    public async Task CreateBuilder_RegistersReadinessCheck()
    {
        var builder = ApiHost.CreateBuilder(
            ["--urls=http://127.0.0.1:0"],
            new ApiHostOptions(
                GrpcPort: 7443,
                HealthPort: 8080,
                ServerCertificate: null,
                ClientCertificateValidation: null,
                LoopbackOnly: true));

        var provider = builder.Services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Status);
    }
}