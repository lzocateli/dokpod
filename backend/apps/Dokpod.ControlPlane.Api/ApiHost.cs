using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Net;
using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Dokpod.ControlPlane.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;

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
                    httpsOptions.CheckCertificateRevocation = true;
                    httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    if (options.ClientCertificateValidation is not null)
                    {
                        httpsOptions.ClientCertificateValidation = options.ClientCertificateValidation;
                    }
                });
            });
        });

        builder.Services.AddGrpc(options =>
        {
            options.MaxReceiveMessageSize = 1_048_576;
            options.MaxSendMessageSize = 1_048_576;
        });
        builder.Services.AddSingleton<IAgentIdentityRegistry, EnvironmentVariableAgentIdentityRegistry>();
        builder.Services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
        builder.Services.AddSingleton<AgentSessionNegotiator>();
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
        app.MapGrpcService<AgentControlService>();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
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