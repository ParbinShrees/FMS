using Futsal.Infrastructure.Persistence;

namespace Futsal.Api.Bookings;

public sealed class ExpireHoldsBackgroundService(IServiceScopeFactory scopeFactory,ILogger<ExpireHoldsBackgroundService> logger, Futsal.Api.Operations.JobHeartbeat? heartbeat = null):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(45));
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BookingService>().ExpireDueHoldsAsync(stoppingToken);
                heartbeat?.Beat("expiry");
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Expiring booking holds failed; it will retry on the next interval.");}
            try{if(!await timer.WaitForNextTickAsync(stoppingToken))break;}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
