using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

if (args.Length != 3)
{
    Console.WriteLine("Usage: dotnet run --project tools/Futsal.RealtimeProbe -- <api-base-url> <venue-slug> <yyyy-MM-dd>");
    return;
}

var baseUrl = args[0].TrimEnd('/');
var venueSlug = args[1];
var date = args[2];
var hubUrl = $"{baseUrl}/hubs/availability";
using var http = new HttpClient();
string? currentVersion = null;
async Task RefetchAsync()
{
    var availability = await http.GetStringAsync($"{baseUrl}/api/v1/public/venues/{Uri.EscapeDataString(venueSlug)}/availability?date={Uri.EscapeDataString(date)}");
    currentVersion = await http.GetStringAsync($"{baseUrl}/api/v1/public/venues/{Uri.EscapeDataString(venueSlug)}/availability/version?date={Uri.EscapeDataString(date)}");
    Console.WriteLine($"Availability refetched; version {currentVersion}; {availability.Length} response characters.");
}
await using var connection = new HubConnectionBuilder()
    .WithUrl(hubUrl)
    .WithAutomaticReconnect()
    .Build();

var eventNames = new[] { "SlotHeld", "SlotReleased", "SlotBooked", "SlotBlocked", "SlotCancelled" };
foreach (var eventName in eventNames)
    connection.On<JsonElement>(eventName, payload => Console.WriteLine($"{eventName} {payload.GetRawText()}"));

connection.Reconnecting += error => { Console.WriteLine($"Reconnecting: {error?.Message}"); return Task.CompletedTask; };
connection.Reconnected += async _ =>
{
    Console.WriteLine("Reconnected; rejoining the venue/date group.");
    await connection.InvokeAsync("JoinVenueDate", venueSlug, date);
    await RefetchAsync();
};
await connection.StartAsync();
await connection.InvokeAsync("JoinVenueDate", venueSlug, date);
await RefetchAsync();
Console.WriteLine($"Connected to {hubUrl}; watching {venueSlug} on {date}. Press Ctrl+C to stop.");
var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.TrySetResult(); };
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
while (!stop.Task.IsCompleted)
{
    var tick = timer.WaitForNextTickAsync().AsTask();
    var completed = await Task.WhenAny(stop.Task, tick);
    if (completed == stop.Task) break;
    if (!await tick) break;
    var previousVersion = currentVersion;
    await RefetchAsync();
    if (previousVersion == currentVersion) Console.WriteLine("Availability version unchanged.");
}
try { await connection.InvokeAsync("LeaveVenueDate", venueSlug, date); } catch { }
