using Futsal.Application.Abstractions;
using Futsal.Application.Scheduling;
using Futsal.Api.Bookings;
using Futsal.Infrastructure.Notifications;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Scheduling;
public sealed class SchedulingRuntime(DbContextOptions<FutsalDbContext> options,IServiceScopeFactory scopes,IClock clock,IConfiguration config,ILogger<SchedulingRuntime> logger)
{
 internal sealed class SystemUser:ICurrentUser{public Guid? UserId=>null;public Guid? OrganizationId=>null;public string? Role=>null;public bool IsAuthenticated=>false;public bool IsSuperAdmin=>true;}
 public async Task ReleasedAsync(BookingEvent message,CancellationToken ct)
 {
  try{await using var db=new FutsalDbContext(options,new SystemUser(),clock);var venue=await db.Venues.SingleOrDefaultAsync(x=>x.Id==message.VenueId,ct);if(venue==null)return;var date=SeriesCalendar.Today(message.StartAt,venue.Timezone);await Dispatch(db,message.VenueId,date,ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch(Exception ex){logger.LogWarning("Waitlist event deferred to recovery job ({Type})",ex.GetType().Name);}
 }
 async Task Dispatch(FutsalDbContext db,Guid venue,DateOnly date,CancellationToken ct)
 {
  await using var scope=scopes.CreateAsyncScope();var services=scope.ServiceProvider;var notifier=services.GetRequiredService<IBookingNotifier>();var writer=new SchedulingWriter(db,clock,new BookingPricing(db));var queue=new NotificationQueue(db,clock,services.GetRequiredService<IDataProtectionProvider>(),services.GetRequiredService<INotificationSender>());var root=string.IsNullOrWhiteSpace(config["PublicBooking:BaseUrl"])?config["Cors:WebOrigins:0"]:config["PublicBooking:BaseUrl"];await new WaitlistService(db,new SystemUser(),clock,writer,notifier,queue,root).DispatchAsync(venue,date,ct);
 }
 public async Task TickAsync(CancellationToken ct)
 {
  await using(var db=new FutsalDbContext(options,new SystemUser(),clock))
  {
   var pairs=await db.WaitlistEntries.Where(x=>x.Status=="WAITING"||x.Status=="OFFERED").Select(x=>new{x.VenueId,x.Date}).Distinct().ToListAsync(ct);
   foreach(var pair in pairs){try{await using var work=new FutsalDbContext(options,new SystemUser(),clock);await Dispatch(work,pair.VenueId,pair.Date,ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch(Exception ex){logger.LogWarning("Waitlist recovery failed ({Type})",ex.GetType().Name);}}
   var cleanup=new WaitlistService(db,new SystemUser(),clock,new(db,clock,new(db)),new NoopNotifier());await cleanup.CleanupAsync(ct);
  }
 }
 public async Task GenerateAsync(CancellationToken ct)
 {
  await using var db=new FutsalDbContext(options,new SystemUser(),clock);var ids=await db.RecurringSeries.Where(x=>x.Status=="ACTIVE"||x.Status=="ENDED").Select(x=>x.Id).ToListAsync(ct);
  foreach(var id in ids){try{await using var work=new FutsalDbContext(options,new SystemUser(),clock);await using var scope=scopes.CreateAsyncScope();var service=new RecurringService(work,new SystemUser(),clock,new(work,clock,new(work)),scope.ServiceProvider.GetRequiredService<IBookingNotifier>());await service.GenerateAsync(id,ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch(Exception ex){logger.LogWarning("Recurring generation failed for series {SeriesId} ({Type})",id,ex.GetType().Name);}}
 }
 sealed class NoopNotifier:IBookingNotifier{public Task PublishAsync(BookingEvent message,CancellationToken ct)=>Task.CompletedTask;}
}
public sealed class SchedulingJobs(IServiceScopeFactory scopes,IClock clock,ILogger<SchedulingJobs> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));var generateAt=DateTimeOffset.MinValue;
  // IClock controls the daily horizon. A crash/restart is harmless because generation is idempotent.
  do{try{await using var scope=scopes.CreateAsyncScope();var runtime=scope.ServiceProvider.GetRequiredService<SchedulingRuntime>();await runtime.TickAsync(ct);if(clock.UtcNow>=generateAt){await runtime.GenerateAsync(ct);generateAt=clock.UtcNow.AddDays(1);}}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}catch(Exception ex){logger.LogError(ex,"Scheduling recovery job failed");}try{if(!await timer.WaitForNextTickAsync(ct))break;}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}}while(!ct.IsCancellationRequested);
 }
}
