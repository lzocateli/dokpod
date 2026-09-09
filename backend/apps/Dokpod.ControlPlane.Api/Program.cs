using Dokpod.Agent.Contracts.V1;
using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
var certificatePath = Environment.GetEnvironmentVariable("DOKPOD_API_CERTIFICATE_PATH")
    ?? throw new InvalidOperationException("DOKPOD_API_CERTIFICATE_PATH is required.");
var certificatePassword = Environment.GetEnvironmentVariable("DOKPOD_API_CERTIFICATE_PASSWORD");
var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, certificatePassword);
var port = int.TryParse(Environment.GetEnvironmentVariable("DOKPOD_API_GRPC_PORT"), out var configuredPort)
    ? configuredPort
    : 7443;

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
        listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificate = certificate;
            httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            httpsOptions.CheckCertificateRevocation = true;
            httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        });
    });
});

builder.Services.AddGrpc();
builder.Services.AddSingleton<IAgentIdentityRegistry, EnvironmentVariableAgentIdentityRegistry>();
builder.Services.AddSingleton<IAgentSessionStore, InMemoryAgentSessionStore>();
builder.Services.AddSingleton<AgentSessionNegotiator>();

var app = builder.Build();
app.MapGrpcService<AgentControlService>();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.Run();

public partial class Program;