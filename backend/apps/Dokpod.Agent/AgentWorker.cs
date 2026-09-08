using System.Text.Json;
using Dokpod.Agent.Application.Engines;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dokpod.Agent;

public sealed class AgentWorker(
    IContainerEngine engine,
    AgentOptions options,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private readonly string readinessPath = Path.Combine(options.DataDirectory, "health", "ready.json");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(readinessPath)!);

        try
        {
            var descriptor = await engine.InspectAsync(stoppingToken);
            logger.LogInformation(
                "Connected to container engine version {EngineVersion} using API {ApiVersion}",
                descriptor.EngineVersion,
                descriptor.ApiVersion);

            await RefreshInventoryAsync(descriptor, stoppingToken);
            if (options.RunOnce)
            {
                applicationLifetime.StopApplication();
                return;
            }

            using var timer = new PeriodicTimer(options.InventoryInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RefreshInventoryAsync(descriptor, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    File.Delete(readinessPath);
                    logger.LogWarning(exception, "Container inventory refresh failed");
                }
            }
        }
        finally
        {
            File.Delete(readinessPath);
        }
    }

    private async Task RefreshInventoryAsync(EngineDescriptor descriptor, CancellationToken cancellationToken)
    {
        var containers = await engine.ListContainersAsync(cancellationToken);
        var health = JsonSerializer.SerializeToUtf8Bytes(new
        {
            status = "ready",
            engineVersion = descriptor.EngineVersion,
            apiVersion = descriptor.ApiVersion,
            containerCount = containers.Count,
            observedAtUtc = DateTimeOffset.UtcNow,
        });
        var temporaryPath = $"{readinessPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, health, cancellationToken);
            File.Move(temporaryPath, readinessPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        logger.LogInformation("Container inventory refreshed with {ContainerCount} entries", containers.Count);
    }
}