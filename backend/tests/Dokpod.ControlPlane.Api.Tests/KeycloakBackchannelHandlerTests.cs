using System.Net;
using Dokpod.ControlPlane.Api.Authorization;
using Xunit;

namespace Dokpod.ControlPlane.Api.Tests;

public sealed class KeycloakBackchannelHandlerTests
{
    [Fact]
    public async Task SendAsync_RewritesPublicAuthorityToBackchannelAuthority()
    {
        var capture = new CapturingHandler();
        using var handler = new KeycloakBackchannelHandler(
            new Uri("https://localhost:7443/realms/dokpod"),
            new Uri("http://keycloak:8080/realms/dokpod"))
        {
            InnerHandler = capture
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(
            "https://localhost:7443/realms/dokpod/protocol/openid-connect/certs",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "http://keycloak:8080/realms/dokpod/protocol/openid-connect/certs",
            capture.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task SendAsync_DoesNotRewriteDifferentOrigin()
    {
        var capture = new CapturingHandler();
        using var handler = new KeycloakBackchannelHandler(
            new Uri("https://localhost:7443/realms/dokpod"),
            new Uri("http://keycloak:8080/realms/dokpod"))
        {
            InnerHandler = capture
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(
            "https://identity.example.test/realms/dokpod/protocol/openid-connect/certs",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "https://identity.example.test/realms/dokpod/protocol/openid-connect/certs",
            capture.RequestUri?.AbsoluteUri);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}