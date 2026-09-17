using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Net;
using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Dokpod.ControlPlane.Api.Realtime;
using Dokpod.ControlPlane.Api.Authorization;
using Dokpod.ControlPlane.Application.Authorization;
using Dokpod.ControlPlane.Application.Auditing;
using Dokpod.ControlPlane.Application.Environments;
using System.Security.Claims;
using Dokpod.ControlPlane.Api.Endpoints;

namespace Dokpod.ControlPlane.Api;

public sealed record ApiHostOptions(
    int GrpcPort,
    int HealthPort,
    X509Certificate2? ServerCertificate = null,
    Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>? ClientCertificateValidation = null,
    bool LoopbackOnly = false,
    Action<IServiceCollection>? ConfigureServices = null);

public static class ApiHost
{
    public static WebApplicationBuilder CreateBuilder(
        string[] args,
        ApiHostOptions? options = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        options ??= LoadOptionsFromEnvironment();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            var listenHealth = options.LoopbackOnly
                ? (Action<int, Action<ListenOptions>>)((port, configure) => serverOptions.Listen(IPAddress.Loopback, port, configure))
                : serverOptions.ListenAnyIP;
            var listenGrpc = options.LoopbackOnly
                ? (Action<int, Action<ListenOptions>>)((port, configure) => serverOptions.Listen(IPAddress.Loopback, port, configure))
                : serverOptions.ListenAnyIP;

            listenHealth(options.HealthPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });

            listenGrpc(options.GrpcPort, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificate = options.ServerCertificate;
                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    httpsOptions.CheckCertificateRevocation = options.ClientCertificateValidation is null;
                    httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    if (options.ClientCertificateValidation is not null)
                    {
                        httpsOptions.ClientCertificateValidation = options.ClientCertificateValidation;
                    }
                });
            });
        });

        builder.Configuration["Authentication:Keycloak:Authority"] ??= "https://keycloak.invalid/realms/dokpod";
        builder.Configuration["Authentication:Keycloak:Audience"] ??= "dokpod-api";
        builder.Configuration["Authentication:Keycloak:DecisionTimeoutSeconds"] ??= "3";

        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = 1_048_576;
            options.MaxSendMessageSize = 1_048_576;
        });
        var authority = builder.Configuration["Authentication:Keycloak:Authority"];
        var audience = builder.Configuration["Authentication:Keycloak:Audience"];
        builder.Services.AddOptions<KeycloakAuthorizationOptions>()
            .BindConfiguration(KeycloakAuthorizationOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.MapInboundClaims = false;
            });
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IValidateOptions<KeycloakAuthorizationOptions>, KeycloakAuthorizationOptionsValidator>();
        builder.Services.AddHttpClient<IEnvironmentAuthorizationDecider, KeycloakAuthorizationDecisionService>(
            (provider, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(
                provider.GetRequiredService<IOptions<KeycloakAuthorizationOptions>>().Value.DecisionTimeoutSeconds);
        });
        builder.Services.AddHealthChecks()
            .AddCheck("controlplane-api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["ready"]);
        builder.Services.AddSingleton<IAgentIdentityRegistry, UnavailableAgentIdentityRegistry>();
        builder.Services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        builder.Services.AddSingleton<AgentSessionNegotiator>();
        builder.Services.AddScoped<IAuditEventWriter, UnavailableAuditEventWriter>();
        builder.Services.AddScoped<IEnvironmentRegistrationStore, UnavailableEnvironmentRegistrationStore>();
        builder.Services.AddScoped<EnvironmentAccessService>();
        builder.Services.AddScoped<EnvironmentRegistrationService>();
        var databaseConnectionString = builder.Configuration.GetConnectionString("ControlPlane");
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            builder.Services.AddControlPlaneInfrastructure(databaseConnectionString);
        }

        options.ConfigureServices?.Invoke(builder.Services);
        return builder;
    }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGrpcService<AgentControlService>().AllowAnonymous();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }).AllowAnonymous();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/v1/session", (ClaimsPrincipal principal) => Results.Ok(new
        {
            subject = principal.FindFirstValue("sub")
        })).RequireAuthorization();
        EnvironmentEndpoints.Map(app);
        app.MapHub<ControlPlaneHub>("/hubs/control-plane").RequireAuthorization();
    }

    private static ApiHostOptions LoadOptionsFromEnvironment()
    {
        var certificatePath = Environment.GetEnvironmentVariable("DOKPOD_API_CERTIFICATE_PATH")
            ?? throw new InvalidOperationException("DOKPOD_API_CERTIFICATE_PATH is required.");
        var certificatePassword = Environment.GetEnvironmentVariable("DOKPOD_API_CERTIFICATE_PASSWORD");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword);
        var port = int.TryParse(Environment.GetEnvironmentVariable("DOKPOD_API_GRPC_PORT"), out var configuredPort)
            ? configuredPort
            : 7443;
        var healthPort = int.TryParse(Environment.GetEnvironmentVariable("DOKPOD_API_HEALTH_PORT"), out var configuredHealthPort)
            ? configuredHealthPort
            : 8080;

        return new ApiHostOptions(port, healthPort, certificate);
    }
}