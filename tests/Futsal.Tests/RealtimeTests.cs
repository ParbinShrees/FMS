using System.Text.Json;
using Futsal.Api.Realtime;
using Futsal.Application.Abstractions;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Futsal.Tests;

public sealed class RealtimeTests
{
    [Fact]
    public async Task Two_clients_receive_public_event_and_other_group_receives_nothing()
    {
        await using var fixture = await RealtimeFixture.CreateAsync();
        var (_, otherVenueSlug) = await fixture.AddVenueAsync();
        using var host = await fixture.StartHostAsync();
        var server = host.GetTestServer();
        var first = NewConnection(server);
        var second = NewConnection(server);
        var isolated = NewConnection(server);
        var otherVenue = NewConnection(server);
        var receivedA = NewEventSource();
        var receivedB = NewEventSource();
        var receivedElsewhere = NewEventSource();
        var receivedOtherVenue = NewEventSource();
        var bookedA = NewEventSource();
        var releasedA = NewEventSource();
        var blockedA = NewEventSource();
        var cancelledA = NewEventSource();
        first.On<AvailabilityEventPayload>("SlotHeld", payload => receivedA.TrySetResult(payload));
        second.On<AvailabilityEventPayload>("SlotHeld", payload => receivedB.TrySetResult(payload));
        isolated.On<AvailabilityEventPayload>("SlotHeld", payload => receivedElsewhere.TrySetResult(payload));
        otherVenue.On<AvailabilityEventPayload>("SlotHeld", payload => receivedOtherVenue.TrySetResult(payload));
        first.On<AvailabilityEventPayload>("SlotBooked", payload => bookedA.TrySetResult(payload));
        first.On<AvailabilityEventPayload>("SlotReleased", payload => releasedA.TrySetResult(payload));
        first.On<AvailabilityEventPayload>("SlotBlocked", payload => blockedA.TrySetResult(payload));
        first.On<AvailabilityEventPayload>("SlotCancelled", payload => cancelledA.TrySetResult(payload));
        await Task.WhenAll(first.StartAsync(), second.StartAsync(), isolated.StartAsync(), otherVenue.StartAsync());
        await first.InvokeAsync("JoinVenueDate", fixture.VenueSlug, "2026-10-14");
        await second.InvokeAsync("JoinVenueDate", fixture.VenueSlug, "2026-10-14");
        await isolated.InvokeAsync("JoinVenueDate", fixture.VenueSlug, "2026-10-15");
        await otherVenue.InvokeAsync("JoinVenueDate", otherVenueSlug, "2026-10-14");

        var notifier = new SignalRBookingNotifier(server.Services.GetRequiredService<IHubContext<AvailabilityHub>>(), fixture.Db,
            NullLogger<SignalRBookingNotifier>.Instance);
        var start = new DateTimeOffset(2026, 10, 14, 12, 15, 0, TimeSpan.Zero);
        await notifier.PublishAsync(new BookingEvent("SlotHeld", fixture.VenueId, Guid.NewGuid(), start, start.AddHours(2), start.AddMinutes(10)), default);

        var a = await receivedA.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var b = await receivedB.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(a, b);
        Assert.Equal(fixture.VenueId, a.VenueId);
        Assert.Equal("HELD", a.Status);
        Assert.False(receivedElsewhere.Task.IsCompleted);
        Assert.False(receivedOtherVenue.Task.IsCompleted);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(a, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(new[] { "courtId", "endUtc", "holdExpiresAtUtc", "startUtc", "status", "venueId" },
            json.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());

        await notifier.PublishAsync(new BookingEvent("SlotBooked", fixture.VenueId, a.CourtId, start, start.AddHours(1)), default);
        await notifier.PublishAsync(new BookingEvent("SlotReleased", fixture.VenueId, a.CourtId, start, start.AddHours(1)), default);
        await notifier.PublishAsync(new BookingEvent("SlotBlocked", fixture.VenueId, a.CourtId, start, start.AddHours(1)), default);
        await notifier.PublishAsync(new BookingEvent("SlotCancelled", fixture.VenueId, a.CourtId, start, start.AddHours(1)), default);
        Assert.Equal("BOOKED", (await bookedA.Task.WaitAsync(TimeSpan.FromSeconds(1))).Status);
        Assert.Equal("FREE", (await releasedA.Task.WaitAsync(TimeSpan.FromSeconds(1))).Status);
        Assert.Equal("BLOCKED", (await blockedA.Task.WaitAsync(TimeSpan.FromSeconds(1))).Status);
        Assert.Equal("CANCELLED", (await cancelledA.Task.WaitAsync(TimeSpan.FromSeconds(1))).Status);

        await Task.WhenAll(first.DisposeAsync().AsTask(), second.DisposeAsync().AsTask(), isolated.DisposeAsync().AsTask(), otherVenue.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Range_crossing_local_midnight_is_published_to_each_affected_date()
    {
        await using var fixture = await RealtimeFixture.CreateAsync();
        using var host = await fixture.StartHostAsync();
        await using var client = NewConnection(host.GetTestServer());
        var received = 0;
        var bothDates = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.On<AvailabilityEventPayload>("SlotBooked", _ => { if (Interlocked.Increment(ref received) == 2) bothDates.TrySetResult(); });
        await client.StartAsync();
        await client.InvokeAsync("JoinVenueDate", fixture.VenueSlug, "2026-10-14");
        await client.InvokeAsync("JoinVenueDate", fixture.VenueSlug, "2026-10-15");
        var notifier = new SignalRBookingNotifier(host.Services.GetRequiredService<IHubContext<AvailabilityHub>>(), fixture.Db,
            NullLogger<SignalRBookingNotifier>.Instance);
        var start = new DateTimeOffset(2026, 10, 14, 12, 15, 0, TimeSpan.Zero);
        await notifier.PublishAsync(new BookingEvent("SlotBooked", fixture.VenueId, Guid.NewGuid(), start, start.AddHours(7)), default);
        await bothDates.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Anonymous_client_cannot_join_staff_group()
    {
        await using var fixture = await RealtimeFixture.CreateAsync();
        using var host = await fixture.StartHostAsync();
        var server = host.GetTestServer();
        await using var client = NewConnection(server);
        await client.StartAsync();
        await Assert.ThrowsAsync<HubException>(() => client.InvokeAsync("JoinStaffVenueDate", fixture.VenueSlug, "2026-10-14"));
    }

    [Fact]
    public async Task Connection_is_limited_to_ten_joined_groups()
    {
        await using var fixture = await RealtimeFixture.CreateAsync();
        using var host = await fixture.StartHostAsync();
        await using var client = NewConnection(host.GetTestServer());
        await client.StartAsync();
        var firstDate = new DateOnly(2026, 10, 1);
        for (var index = 0; index < 10; index++)
            await client.InvokeAsync("JoinVenueDate", fixture.VenueSlug, firstDate.AddDays(index).ToString("yyyy-MM-dd"));
        await Assert.ThrowsAsync<HubException>(() => client.InvokeAsync("JoinVenueDate", fixture.VenueSlug, firstDate.AddDays(10).ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task SignalR_publish_failure_is_swallowed_after_database_commit()
    {
        await using var fixture = await RealtimeFixture.CreateAsync();
        var notifier = new SignalRBookingNotifier(new ThrowingHubContext(), fixture.Db, NullLogger<SignalRBookingNotifier>.Instance);
        await notifier.PublishAsync(new BookingEvent("SlotBooked", fixture.VenueId, Guid.NewGuid(), fixture.Clock.UtcNow,
            fixture.Clock.UtcNow.AddHours(1)), default);
    }

    private static HubConnection NewConnection(TestServer server) => new HubConnectionBuilder()
        .WithUrl("http://localhost/hubs/availability", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => server.CreateHandler();
        }).Build();

    private static TaskCompletionSource<AvailabilityEventPayload> NewEventSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RealtimeFixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public readonly TestClock Clock = new();
        public readonly FutsalDbContext Db;
        public readonly Guid VenueId = Guid.NewGuid();
        public string VenueSlug { get; } = Guid.NewGuid().ToString("N");
        private RealtimeFixture()
        {
            var current = new TestCurrentUser();
            var options = new DbContextOptionsBuilder<FutsalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            Db = new FutsalDbContext(options, current, Clock);
        }
        public static async Task<RealtimeFixture> CreateAsync()
        {
            var f = new RealtimeFixture();
            await f.Db.Database.EnsureCreatedAsync();
            var org = new Organization { Name = "Realtime test" };
            var city = new Location { Type = LocationType.CITY, NameEn = "Kathmandu", NameNe = "काठमाडौँ", Slug = "ktm-" + f.VenueSlug, Path = "ktm-" + f.VenueSlug };
            f.Db.AddRange(org, city);
            await f.Db.SaveChangesAsync();
            f.Db.Add(new Venue { Id = f.VenueId, OrganizationId = org.Id, LocationId = city.Id, CityId = city.Id,
                Name = "Realtime venue", Slug = f.VenueSlug, IsPublic = true, IsActive = true });
            await f.Db.SaveChangesAsync();
            return f;
        }
        public async Task<IHost> StartHostAsync()
        {
            var host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => { services.AddSingleton(Db); services.AddSingleton<IClock>(Clock); services.AddSignalR(); })
                .Configure(app => { app.UseRouting(); app.UseEndpoints(endpoints => endpoints.MapHub<AvailabilityHub>("/hubs/availability")); }))
                .Build();
            await host.StartAsync();
            return host;
        }
        public async Task<(Guid Id,string Slug)> AddVenueAsync()
        {
            var city = await Db.Locations.AsNoTracking().SingleAsync();
            var organization = new Organization { Name = "Other realtime org" };
            var venue = new Venue { OrganizationId = Guid.NewGuid(), LocationId = city.Id, CityId = city.Id,
                Name = "Other venue", Slug = Guid.NewGuid().ToString("N"), IsPublic = true, IsActive = true };
            venue.OrganizationId = organization.Id;
            Db.AddRange(organization, venue);
            await Db.SaveChangesAsync();
            return (venue.Id, venue.Slug);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null; public Guid? OrganizationId => null; public string? Role => null;
        public bool IsSuperAdmin => true; public bool IsAuthenticated => false;
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow => RealtimeFixture.Now; }

    private sealed class ThrowingHubContext : IHubContext<AvailabilityHub>
    {
        public IHubClients Clients { get; } = new ThrowingHubClients();
        public IGroupManager Groups { get; } = new NoopGroupManager();
    }
    private sealed class ThrowingHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new ThrowingClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }
    private sealed class ThrowingClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated transport failure.");
    }
    private sealed class NoopGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
