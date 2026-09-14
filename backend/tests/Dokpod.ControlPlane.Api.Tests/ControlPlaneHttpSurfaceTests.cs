using Dokpod.ControlPlane.Api;
using Dokpod.ControlPlane.Api.Realtime;
using Dokpod.ControlPlane.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
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
        Assert.Contains("/hubs/control-plane", endpoints.Keys);
        Assert.NotNull(endpoints["/api/v1/session"].Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(endpoints["/hubs/control-plane"].Metadata.GetMetadata<IAuthorizeData>());
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
