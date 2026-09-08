using Dokpod.Agent;
using Dokpod.Agent.Application.Engines;
using Dokpod.Agent.Infrastructure.Commands;
using Dokpod.Agent.Infrastructure.Engines.Docker;
using Dokpod.Domain.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var options = AgentOptions.FromEnvironment();
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(service => service.ServiceName = "Dokpod Agent");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ICommandJournal>(_ => new FileCommandJournal(options.DataDirectory));
builder.Services.AddSingleton(_ => OperatingSystem.IsWindows()
    ? DockerEngineClient.CreateNamedPipeClient(options.DockerSocketPath, options.EngineTimeout)
    : DockerEngineClient.CreateUnixSocketClient(options.DockerSocketPath, options.EngineTimeout));
builder.Services.AddSingleton<IContainerEngine, DockerEngineClient>();
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();