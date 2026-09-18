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

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToDictionary(endpoint => endpoint.RoutePattern.RawText!, StringComparer.Ordinal);

        Assert.Contains("/api/v1/session", endpoints.Keys);
        Assert.Contains("/api/v1/environments", endpoints.Keys);
        Assert.Contains("/api/v1/environments/{environmentId:guid}", endpoints.Keys);
        Assert.Contains("/hubs/control-plane", endpoints.Keys);
        Assert.Contains("/health/live", endpoints.Keys);
        Assert.Contains("/health/ready", endpoints.Keys);
        Assert.Contains("/health/ready/database", endpoints.Keys);
        Assert.Contains("/health/ready/keycloak", endpoints.Keys);
        Assert.NotNull(endpoints["/api/v1/session"].Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(endpoints["/api/v1/environments"].Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(endpoints["/api/v1/environments/{environmentId:guid}"].Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(endpoints["/hubs/control-plane"].Metadata.GetMetadata<IAuthorizeData>());

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
