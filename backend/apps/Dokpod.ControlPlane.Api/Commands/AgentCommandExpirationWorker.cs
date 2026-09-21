using Dokpod.ControlPlane.Application.Commands;

namespace Dokpod.ControlPlane.Api.Commands;

public sealed class AgentCommandExpirationWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AgentCommandExpirationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExpireAsync(stoppingToken);
        using var timer = new PeriodicTimer(SweepInterval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExpireAsync(stoppingToken);
        }
    }

    private async Task ExpireAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IAgentCommandStore>();
            var expired = await store.ExpireNonTerminalAsync(
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (expired > 0)
            {
                logger.LogInformation("Terminally expired {ExpiredCommandCount} agent commands", expired);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to terminally expire agent commands");
        }
    }
}