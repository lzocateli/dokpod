using Dokpod.ControlPlane.Api;
using Dokpod.ControlPlane.Api.Realtime;
using Dokpod.ControlPlane.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class ControlPlaneHttpSurfaceTests
{
    [Fact]
    public void MapEndpoints_RegistersProtectedSessionAndHubRoutes()
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

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var routes = endpoints
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/api/v1/session", routes);
        Assert.Contains("/api/v1/environments", routes);
        Assert.Contains("/api/v1/environments/{environmentId:guid}", routes);
        Assert.Contains("/api/v1/environments/{environmentId:guid}/containers", routes);
        Assert.Contains(
            "/api/v1/environments/{environmentId:guid}/containers/{containerId}/commands",
            routes);
        Assert.Contains(
            "/api/v1/environments/{environmentId:guid}/commands/{commandId:guid}",
            routes);
        Assert.Contains("/hubs/control-plane", routes);
        Assert.Contains("/health/live", routes);
        Assert.Contains("/health/ready", routes);
        Assert.Contains("/health/ready/database", routes);
        Assert.Contains("/health/ready/keycloak", routes);
        Assert.All(
            endpoints.Where(endpoint => endpoint.RoutePattern.RawText is
                "/api/v1/session"
                or "/api/v1/environments"
                or "/api/v1/environments/{environmentId:guid}"
                or "/api/v1/environments/{environmentId:guid}/containers"
                or "/api/v1/environments/{environmentId:guid}/containers/{containerId}/commands"
                or "/api/v1/environments/{environmentId:guid}/commands/{commandId:guid}"
                or "/hubs/control-plane"),
            endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>()));

        var healthChecks = app.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations
            .ToDictionary(registration => registration.Name, StringComparer.Ordinal);
        Assert.Contains("controlplane-api", healthChecks.Keys);
        Assert.Contains("postgresql", healthChecks.Keys);
        Assert.Contains("keycloak", healthChecks.Keys);
        Assert.Contains("api", healthChecks["controlplane-api"].Tags);
        Assert.Contains("database", healthChecks["postgresql"].Tags);
        Assert.Contains("keycloak", healthChecks["keycloak"].Tags);
    }

    [Fact]
    public void GroupFor_UsesStableEnvironmentGroupName()
    {
        Assert.Equal("environment-lab-0001", ControlPlaneHub.GroupFor("lab-0001"));
    }

    [Fact]
    public async Task JoinEnvironmentAsync_RejectsInvalidEnvironmentId()
    {
        var hub = new ControlPlaneHub(new StubAuthorizationDecider());

        var exception = await Assert.ThrowsAsync<HubException>(() =>
            hub.JoinEnvironmentAsync("../host"));

        Assert.Equal("Identificador de ambiente inválido.", exception.Message);
    }

    private sealed class StubAuthorizationDecider : IEnvironmentAuthorizationDecider
    {
        public Task<EnvironmentAuthorizationDecision> DecideAsync(
            string resource,
            string scope,
            AuthenticatedActor actor,
            string accessToken,
            Guid correlationId,
            CancellationToken cancellationToken) => Task.FromResult(
                new EnvironmentAuthorizationDecision(AuthorizationDecisionOutcome.Denied, "denied"));
    }
}
