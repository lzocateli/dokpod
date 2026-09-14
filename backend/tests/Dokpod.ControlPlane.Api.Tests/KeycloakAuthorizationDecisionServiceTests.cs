using System.Net;
using System.Text;
using Dokpod.ControlPlane.Api.Authorization;
using Dokpod.ControlPlane.Application.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class KeycloakAuthorizationDecisionServiceTests
{
    [Fact]
    public async Task DecideAsync_WithGrantedDecisionReturnsAllowedAndSendsUmaPermission()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, "{\"result\":true}");
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var decision = await service.DecideAsync(
            "urn:dokpod:environment:00000000-0000-0000-0000-000000000001",
            "environment:read",
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(AuthorizationDecisionOutcome.Allowed, decision.Outcome);
        Assert.Equal("Bearer access-token", handler.AuthorizationHeader);
        Assert.Equal("dokpod-api", handler.Form["audience"]);
        Assert.Equal(
            "urn:dokpod:environment:00000000-0000-0000-0000-000000000001#environment:read",
            handler.Form["permission"]);
    }

    [Fact]
    public async Task DecideAsync_WithDeniedHttpStatusReturnsDenied()
    {
        using var handler = new StubHandler(HttpStatusCode.Forbidden, "{}");
        using var client = new HttpClient(handler);
        var decision = await CreateService(client).DecideAsync(
            "urn:dokpod:environment:00000000-0000-0000-0000-000000000001",
            "environment:read",
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(AuthorizationDecisionOutcome.Denied, decision.Outcome);
        Assert.Equal("keycloak_http_403", decision.FailureCode);
    }

    [Fact]
    public async Task DecideAsync_WithTransportFailureFailsClosed()
    {
        using var client = new HttpClient(new FailingHandler());
        var decision = await CreateService(client).DecideAsync(
            "urn:dokpod:environment:00000000-0000-0000-0000-000000000001",
            "environment:read",
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(AuthorizationDecisionOutcome.Indeterminate, decision.Outcome);
        Assert.Equal("authorization_unavailable", decision.FailureCode);
    }

    [Fact]
    public async Task DecideAsync_RejectsUnsupportedResourceBeforeCallingKeycloak()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, "{\"result\":true}");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(client).DecideAsync(
            "urn:other:resource:1",
            "environment:read",
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Null(handler.AuthorizationHeader);
    }

    [Fact]
    public async Task DecideAsync_RejectsUnsupportedScopeBeforeCallingKeycloak()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, "{\"result\":true}");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateService(client).DecideAsync(
            "urn:dokpod:environment:00000000-0000-0000-0000-000000000001",
            "environment:unknown",
            AuthenticatedActor.FromSubject("user-1"),
            "access-token",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Null(handler.AuthorizationHeader);
    }

    private static KeycloakAuthorizationDecisionService CreateService(HttpClient client) =>
        new(
            client,
            Options.Create(new KeycloakAuthorizationOptions
            {
                Authority = "http://localhost:8080/realms/dokpod",
                Audience = "dokpod-api"
            }),
            NullLogger<KeycloakAuthorizationDecisionService>.Instance);

    private sealed class StubHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }
        public Dictionary<string, string> Form { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                Form[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException());
    }
}
