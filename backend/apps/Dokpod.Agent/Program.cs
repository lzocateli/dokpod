using Dokpod.Agent;
using Dokpod.Agent.Application.Commands;
using Dokpod.Agent.Application.Engines;
using Dokpod.Agent.Application.Protocol;
using Dokpod.Agent.Infrastructure.Commands;
using Dokpod.Agent.Infrastructure.Engines.Docker;
using Dokpod.Domain.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var options = AgentOptions.FromEnvironment();
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(service => service.ServiceName = "Dokpod Agent");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ICommandJournal>(_ => new FileCommandJournal(options.DataDirectory));
builder.Services.AddSingleton(_ => DockerEngineClient.CreateUnixSocketClient(
    options.DockerSocketPath,
    options.EngineTimeout));
builder.Services.AddSingleton<IContainerEngine, DockerEngineClient>();
builder.Services.AddSingleton<AgentCommandGate>();
builder.Services.AddSingleton<AgentCommandProcessor>();
builder.Services.AddSingleton<AgentCommandProtocolHandler>();
builder.Services.AddHostedService<AgentWorker>();
if (options.ControlPlaneEndpoint is not null && !options.RunOnce)
{
    builder.Services.AddHostedService<AgentControlWorker>();
}

await builder.Build().RunAsync();