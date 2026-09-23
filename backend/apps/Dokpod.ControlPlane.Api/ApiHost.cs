using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net.Security;
using System.Net;
using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Api.Commands;
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
using Dokpod.ControlPlane.Application.Commands;
using Dokpod.ControlPlane.Application.Environments;
using Dokpod.ControlPlane.Application.Inventory;
using System.Security.Claims;
using Dokpod.ControlPlane.Api.Endpoints;
using Dokpod.ControlPlane.Api.Health;

namespace Dokpod.ControlPlane.Api;

public sealed record ApiHostOptions(
    int GrpcPort,
    int HealthPort,
    X509Certificate2 ServerCertificate,
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
        ArgumentNullException.ThrowIfNull(
            options.ServerCertificate,
            nameof(ApiHostOptions.ServerCertificate));

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
        builder.Configuration["Authentication:Keycloak:BackchannelAuthority"] ??=
            builder.Configuration["Authentication:Keycloak:Authority"];
        builder.Configuration["Authentication:Keycloak:Audience"] ??= "dokpod-api";
        builder.Configuration["Authentication:Keycloak:DecisionTimeoutSeconds"] ??= "3";

        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = 1_048_576;
            options.MaxSendMessageSize = 1_048_576;
        });
        var authority = builder.Configuration["Authentication:Keycloak:Authority"];
        var backchannelAuthority = builder.Configuration["Authentication:Keycloak:BackchannelAuthority"];
        var audience = builder.Configuration["Authentication:Keycloak:Audience"];
        builder.Services.AddOptions<KeycloakAuthorizationOptions>()
            .BindConfiguration(KeycloakAuthorizationOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.MetadataAddress = $"{backchannelAuthority!.TrimEnd('/')}/.well-known/openid-configuration";
                options.Audience = audience;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = authority!.TrimEnd('/');
                options.BackchannelHttpHandler = new KeycloakBackchannelHandler(
                    new Uri(authority, UriKind.Absolute),
                    new Uri(backchannelAuthority, UriKind.Absolute));
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
        builder.Services.AddHttpClient(nameof(KeycloakReadinessHealthCheck), (provider, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(
                provider.GetRequiredService<IOptions<KeycloakAuthorizationOptions>>().Value.DecisionTimeoutSeconds);
        });
        var healthChecks = builder.Services.AddHealthChecks()
            .AddCheck("controlplane-api", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["ready", "api"])
            .AddCheck<KeycloakReadinessHealthCheck>("keycloak", tags: ["ready", "keycloak"]);
        builder.Services.AddSingleton<IAgentIdentityRegistry, UnavailableAgentIdentityRegistry>();
        builder.Services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        builder.Services.AddSingleton<IAgentCommandDeliveryQueue, InMemoryAgentCommandDeliveryQueue>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddScoped<AgentSessionNegotiator>();
        builder.Services.AddScoped<IAuditEventWriter, UnavailableAuditEventWriter>();
        builder.Services.AddScoped<IAgentCommandStore, UnavailableAgentCommandStore>();
        builder.Services.AddScoped<IEnvironmentRegistrationStore, UnavailableEnvironmentRegistrationStore>();
        builder.Services.AddScoped<IInventoryProjectionStore, UnavailableInventoryProjectionStore>();
        builder.Services.AddScoped<EnvironmentAccessService>();
        builder.Services.AddScoped<EnvironmentRegistrationService>();
        builder.Services.AddScoped<AgentIdentityRevocationService>();
        builder.Services.AddScoped<AgentCommandQueueService>();
        builder.Services.AddScoped<ContainerCommandService>();
        builder.Services.AddScoped<ContainerCommandQueryService>();
        builder.Services.AddScoped<AgentCommandStatusService>();
        builder.Services.AddHostedService<AgentCommandExpirationWorker>();
        builder.Services.AddScoped<InventoryProjectionService>();
        builder.Services.AddScoped<InventoryQueryService>();
        var databaseConnectionString = builder.Configuration.GetConnectionString("ControlPlane");
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            builder.Services.AddControlPlaneInfrastructure(databaseConnectionString);
            healthChecks.AddCheck<PostgresReadinessHealthCheck>("postgresql", tags: ["ready", "database"]);
        }
        else
        {
            healthChecks.AddCheck(
                "postgresql",
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("PostgreSQL não configurado."),
                tags: ["ready", "database"]);
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
        app.MapHealthChecks("/health/ready/database", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("database"),
        }).AllowAnonymous();
        app.MapHealthChecks("/health/ready/keycloak", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("keycloak"),
        }).AllowAnonymous();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/api/v1/session", (ClaimsPrincipal principal) => Results.Ok(new
        {
            subject = principal.FindFirstValue("sub")
        })).RequireAuthorization();
        EnvironmentEndpoints.Map(app);
        InventoryEndpoints.Map(app);
        ContainerCommandEndpoints.Map(app);
        app.MapHub<ControlPlaneHub>("/hubs/control-plane").RequireAuthorization();
    }

    private static ApiHostOptions LoadOptionsFromEnvironment()
    {
        var certificatePath = Environment.GetEnvironmentVariable("DOKPOD_API_CERTIFICATE_PATH")
            ?? throw new InvalidOperationException("DOKPOD_API_CERTIFICATE_PATH is required.");
        var certificatePassword = Environment.GetEnvironmentVariable("DOKPOD_API_CERTIFICATE_PASSWORD");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword);
        var clientCaPath = Environment.GetEnvironmentVariable("DOKPOD_API_CLIENT_CA_CERTIFICATE_PATH");
        var clientCaCertificate = string.IsNullOrWhiteSpace(clientCaPath)
            ? null
            : X509CertificateLoader.LoadCertificateFromFile(clientCaPath);
        var port = int.TryParse(Environment.GetEnvironmentVariable("DOKPOD_API_GRPC_PORT"), out var configuredPort)
            ? configuredPort
            : 7443;
        var healthPort = int.TryParse(Environment.GetEnvironmentVariable("DOKPOD_API_HEALTH_PORT"), out var configuredHealthPort)
            ? configuredHealthPort
            : 8080;

        Func<X509Certificate2, X509Chain?, SslPolicyErrors, bool>? clientValidation =
            clientCaCertificate is null
                ? null
                : (certificate, chain, errors) => ValidateClientCertificate(certificate, clientCaCertificate);

        return new ApiHostOptions(
            port,
            healthPort,
            certificate,
            clientValidation);
    }

    private static bool ValidateClientCertificate(
        X509Certificate2? clientCertificate,
        X509Certificate2 trustedRoot)
    {
        if (clientCertificate is null)
        {
            return false;
        }

        const string clientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
        var eku = clientCertificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SingleOrDefault();
        if (eku is null || !eku.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == clientAuthenticationOid))
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(clientCertificate);
    }
}