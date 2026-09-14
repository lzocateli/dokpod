using System.Net;
using System.Net.Http.Headers;
using Dokpod.Bff.Configuration;
using Dokpod.Bff.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dokpod.Bff.Tests;

public sealed class DownstreamProxyTests
{
    [Fact]
    public async Task ProxyAsync_ReplacesBrowserCredentialsAndDoesNotForwardDownstreamCookies()
    {
        var handler = new CapturingHandler();
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new StubAuthenticationService("server-token"))
            .BuildServiceProvider();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/environments";
        context.Request.QueryString = new QueryString("?page=2");
        context.Request.Headers["authorization"] = "Bearer browser-token";
        context.Request.Headers["cookie"] = "__Host-Dokpod.Session=opaque";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";
        context.Request.ContentLength = 7;
        context.Request.Body = new MemoryStream("payload"u8.ToArray());
        context.Response.Body = new MemoryStream();

        var factory = new StubHttpClientFactory(handler);
        var options = Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" });

        await DownstreamProxy.ProxyAsync(context, factory, options);

        Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)context.Response.StatusCode);
        Assert.Equal("Bearer server-token", handler.Request!.Headers.Authorization?.ToString());
        Assert.False(handler.Request.Headers.Contains("Cookie"));
        Assert.False(handler.Request.Headers.Contains("Set-Cookie"));
        Assert.False(handler.Request.Headers.Contains("X-Forwarded-For"));
        Assert.Equal("?page=2", handler.Request.RequestUri!.Query);
        Assert.Equal("correlation-1", context.Response.Headers["X-Correlation-Id"].ToString());
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task ProxyAsync_ReturnsBadGatewayWhenDownstreamIsUnavailable()
    {
        var context = CreateContext();

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(new ThrowingHandler(new HttpRequestException())),
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" }));

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_ReturnsGatewayTimeoutWhenDownstreamTimesOut()
    {
        var context = CreateContext();

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(new ThrowingHandler(new TaskCanceledException())),
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" }));

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_PreservesDownstreamUnauthorizedStatusWithoutRedirect()
    {
        var context = CreateContext();

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(new StatusHandler(HttpStatusCode.Unauthorized)),
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" }));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_PreservesDownstreamForbiddenStatusWithoutRedirect()
    {
        var context = CreateContext();

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(new StatusHandler(HttpStatusCode.Forbidden)),
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" }));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_RejectsTraversalPathBeforeCallingDownstream()
    {
        var handler = new CapturingHandler();
        var context = CreateContext();
        context.Request.Path = "/api/v1/%2e%2e/admin";

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(handler),
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" }));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task ProxyAsync_RejectsAbsoluteDownstreamPathBeforeCallingDownstream()
    {
        var handler = new CapturingHandler();
        var context = CreateContext();
        context.Request.Path = "/api/v1/https://attacker.example.test/collect";

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(handler),
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.example.test/" }));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task ProxyAsync_RejectsRequestBodyAboveConfiguredLimit()
    {
        var handler = new CapturingHandler();
        var context = CreateContext();
        context.Request.ContentLength = 2;
        context.Request.Body = new MemoryStream("xx"u8.ToArray());

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(handler),
            Options.Create(new DownstreamApiOptions
            {
                BaseUrl = "https://api.example.test/",
                MaxRequestContentLengthBytes = 1
            }));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task ProxyAsync_RejectsKnownResponseAboveConfiguredLimit()
    {
        var context = CreateContext();

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(new CapturingHandler()),
            Options.Create(new DownstreamApiOptions
            {
                BaseUrl = "https://api.example.test/",
                MaxResponseContentLengthBytes = 1
            }));

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_RejectsChunkedRequestBodyAboveConfiguredLimit()
    {
        var handler = new ReadingHandler();
        var context = CreateContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers.TransferEncoding = "chunked";
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream("xx"u8.ToArray());

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(handler),
            Options.Create(new DownstreamApiOptions
            {
                BaseUrl = "https://api.example.test/",
                MaxRequestContentLengthBytes = 1
            }));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProxyAsync_ForwardsMutableBodyWithoutFramingHeadersThroughLimit()
    {
        var handler = new ReadingHandler();
        var context = CreateContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentLength = null;
        context.Request.Body = new MemoryStream("payload"u8.ToArray());

        await DownstreamProxy.ProxyAsync(
            context,
            new StubHttpClientFactory(handler),
            Options.Create(new DownstreamApiOptions
            {
                BaseUrl = "https://api.example.test/",
                MaxRequestContentLengthBytes = 32
            }));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("payload", handler.Body);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(new StubAuthenticationService("server-token"))
                .BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/v1/environments";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
            response.Headers.TryAddWithoutValidation("X-Correlation-Id", "correlation-1");
            response.Headers.TryAddWithoutValidation("set-cookie", "downstream=secret");
            response.Headers.TryAddWithoutValidation("location", "https://attacker.example.test/");
            return Task.FromResult(response);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}")
            });
    }

    private sealed class ReadingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = System.Text.Encoding.UTF8.GetString(
                await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        }
    }

    private sealed class StubAuthenticationService(string accessToken) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = accessToken }]);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(context.User, properties, scheme ?? "Cookies")));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            System.Security.Claims.ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }
}
