using Dokpod.Bff.Authentication;
using Dokpod.Bff.Configuration;
using Dokpod.Bff.Endpoints;
using Dokpod.Bff.Health;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;

namespace Dokpod.Bff;

public static class BffHost
{
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOptions<KeycloakOptions>().BindConfiguration(KeycloakOptions.SectionName).ValidateOnStart();
        builder.Services.AddOptions<BffSecurityOptions>().BindConfiguration(BffSecurityOptions.SectionName).ValidateOnStart();
        builder.Services.AddOptions<BffRuntimeOptions>().BindConfiguration(BffRuntimeOptions.SectionName).ValidateOnStart();
        builder.Services.AddOptions<DownstreamApiOptions>().BindConfiguration(DownstreamApiOptions.SectionName).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<KeycloakOptions>, KeycloakOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<BffSecurityOptions>, BffSecurityOptionsValidator>();
        builder.Services.AddSingleton<IValidateOptions<DownstreamApiOptions>, DownstreamApiOptionsValidator>();

        var runtime = builder.Configuration.GetSection(BffRuntimeOptions.SectionName).Get<BffRuntimeOptions>() ?? new();
        var security = builder.Configuration.GetSection(BffSecurityOptions.SectionName).Get<BffSecurityOptions>() ?? new();
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName(runtime.DataProtectionApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(security.DataProtectionKeysPath));
        if (!string.IsNullOrWhiteSpace(security.DataProtectionCertificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                security.DataProtectionCertificatePath,
                security.DataProtectionCertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
            builder.Services.AddSingleton(certificate);
            dataProtection.ProtectKeysWithCertificate(certificate);
        }
        builder.Services.AddMemoryCache(options => options.SizeLimit = runtime.MemoryCacheSizeLimit);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ServerSideTicketStore>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IAccessTokenRefreshCoordinator, AccessTokenRefreshCoordinator>();
        builder.Services.AddScoped<CookieTokenRefreshEvents>();
        builder.Services.AddSingleton<RequestOriginValidator>();
        builder.Services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>, ConfigureForwardedHeadersOptions>();
        builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureCookieOptions>();
        builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureOpenIdConnectOptions>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect();
        builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = runtime.LoginRateLimitPermitLimit,
                    Window = TimeSpan.FromSeconds(runtime.LoginRateLimitWindowSeconds),
                    QueueLimit = runtime.LoginRateLimitQueueLimit,
                }));
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = runtime.AntiforgeryCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.HeaderName = runtime.AntiforgeryHeaderName;
        });
        builder.Services.AddHealthChecks().AddCheck<KeycloakDiscoveryHealthCheck>("keycloak", tags: ["ready"]);
        builder.Services.AddHttpClient(KeycloakDiscoveryHealthCheck.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(runtime.KeycloakDiscoveryTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var keycloak = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
                return KeycloakHttpMessageHandlerFactory.Create(keycloak.Authority);
            });
        builder.Services.AddHttpClient(AccessTokenRefreshCoordinator.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(runtime.TokenRefreshTimeoutSeconds);
            client.MaxResponseContentBufferSize = runtime.TokenRefreshMaxResponseContentBufferSize;
        }).ConfigurePrimaryHttpMessageHandler(provider =>
        {
            var keycloak = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
            return KeycloakHttpMessageHandlerFactory.Create(keycloak.Authority);
        });
        builder.Services.AddHttpClient("dokpod-bff-downstream", client =>
        {
            var downstream = builder.Configuration
                .GetSection(DownstreamApiOptions.SectionName)
                .Get<DownstreamApiOptions>() ?? new();
            client.BaseAddress = string.IsNullOrWhiteSpace(downstream.BaseUrl)
                ? new Uri("https://localhost")
                : new Uri(downstream.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(downstream.TimeoutSeconds);
            client.MaxResponseContentBufferSize = runtime.DownstreamApiMaxResponseContentBufferSize;
        }).ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        });
        return builder;
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseWebSockets();
        app.UseMiddleware<OriginValidationMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        }).AllowAnonymous();
        endpoints.MapGet("/bff/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = LocalRedirect.Normalize(returnUrl) },
            [OpenIdConnectDefaults.AuthenticationScheme])).AllowAnonymous().RequireRateLimiting("login");
        endpoints.MapGet("/bff/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { requestToken = tokens.RequestToken });
        }).RequireAuthorization();
        endpoints.MapGet("/bff/session", (HttpContext context) =>
            Results.Ok(BffSessionResponse.FromPrincipal(context.User))).AllowAnonymous();
        endpoints.MapMethods("/api/v1/{**path}", [HttpMethods.Get, HttpMethods.Head, HttpMethods.Options], async (HttpContext context, IHttpClientFactory httpClientFactory) =>
            await DownstreamProxy.ProxyAsync(context, httpClientFactory, context.RequestServices.GetRequiredService<IOptions<DownstreamApiOptions>>()))
            .RequireAuthorization();
        endpoints.MapMethods("/api/v1/{**path}", [HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete, HttpMethods.Patch], async (HttpContext context, IHttpClientFactory httpClientFactory) =>
            await DownstreamProxy.ProxyAsync(context, httpClientFactory, context.RequestServices.GetRequiredService<IOptions<DownstreamApiOptions>>()))
            .RequireAuthorization()
            .AddEndpointFilter(async (invocationContext, next) =>
            {
                await invocationContext.HttpContext.RequestServices
                    .GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(invocationContext.HttpContext);
                return await next(invocationContext);
            });
        endpoints.MapGet("/hubs/{**path}", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
            await DownstreamProxy.ProxyAsync(context, httpClientFactory, context.RequestServices.GetRequiredService<IOptions<DownstreamApiOptions>>()))
            .RequireAuthorization();
        endpoints.MapPost("/hubs/{**path}", async (HttpContext context, IHttpClientFactory httpClientFactory) =>
            await DownstreamProxy.ProxyAsync(
                context,
                httpClientFactory,
                context.RequestServices.GetRequiredService<IOptions<DownstreamApiOptions>>()))
            .RequireAuthorization()
            .AddEndpointFilter(async (invocationContext, next) =>
            {
                await invocationContext.HttpContext.RequestServices
                    .GetRequiredService<IAntiforgery>()
                    .ValidateRequestAsync(invocationContext.HttpContext);
                return await next(invocationContext);
            });
        endpoints.MapPost("/bff/logout", async (IAntiforgery antiforgery, HttpContext context) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
        }).RequireAuthorization();
    }
}