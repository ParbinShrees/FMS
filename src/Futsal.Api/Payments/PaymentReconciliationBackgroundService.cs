namespace Futsal.Api.Payments;

public sealed class PaymentReconciliationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentReconciliationBackgroundService> logger, Futsal.Api.Operations.JobHeartbeat? heartbeat = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PaymentProcessingService>();
                await service.ProcessPendingReconciliationsAsync(stoppingToken);
                heartbeat?.Beat("payments");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payment reconciliation background task encountered an error.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
