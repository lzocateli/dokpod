using Dokpod.ControlPlane.Api.Agents;
using Dokpod.ControlPlane.Application.Agents;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.ConfigureHttpsDefaults(httpsOptions =>
{
    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
    httpsOptions.CheckCertificateRevocation = true;
}));

builder.Services.AddGrpc();
builder.Services.AddSingleton<IAgentIdentityRegistry, RejectingAgentIdentityRegistry>();
builder.Services.AddSingleton<IAgentSessionStore, RejectingAgentSessionStore>();
builder.Services.AddSingleton<AgentSessionNegotiator>();

var app = builder.Build();

app.MapGrpcService<AgentControlService>();
app.MapGet("/health/live", () => Results.Ok());

await app.RunAsync();

public partial class Program;