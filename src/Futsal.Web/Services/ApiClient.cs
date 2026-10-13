using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Futsal.Contracts;

namespace Futsal.Web.Services;

public sealed class ApiProblemException(HttpStatusCode status, string code, string detail) : Exception(detail)
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class ApiClient(IHttpClientFactory factory, BffAuthState auth)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client()
    {
        var client = factory.CreateClient("FutsalApi");
        if (auth.Token is { } token) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
    public async Task<IReadOnlyList<VenueDto>> GetVenuesAsync(CancellationToken ct = default) =>
        await GetAsync<List<VenueDto>>("api/v1/staff/venues", ct) ?? [];
    public async Task<IReadOnlyList<AvailabilitySlotDto>> GetAvailabilityAsync(string slug, DateOnly date, CancellationToken ct = default) =>
        await GetAsync<List<AvailabilitySlotDto>>($"api/v1/staff/venues/{Uri.EscapeDataString(slug)}/availability?date={date:yyyy-MM-dd}", ct) ?? [];
    public Task<BookingQuoteDto?> QuoteAsync(StaffBookingDto booking, CancellationToken ct = default) => PostAsync<StaffBookingDto, BookingQuoteDto>("api/v1/staff/bookings/quote", booking, null, ct);
    public Task<BookingDto?> CreateBookingAsync(StaffBookingDto booking, CancellationToken ct = default) =>
        PostAsync<StaffBookingDto, BookingDto>("api/v1/staff/bookings", booking, Guid.NewGuid().ToString("N"), ct);
    private async Task<T?> GetAsync<T>(string uri, CancellationToken ct)
    {
        using var response = await Client().GetAsync(uri, ct); await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }
    private async Task<T?> PostAsync<TRequest, T>(string uri, TRequest body, string? idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, options: Json) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await Client().SendAsync(request, ct); await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }
    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var code = json.TryGetProperty("code", out var c) ? c.GetString() ?? "REQUEST_FAILED" : "REQUEST_FAILED";
        var detail = json.TryGetProperty("detail", out var d) ? d.GetString() ?? "The request could not be completed." : "The request could not be completed.";
        throw new ApiProblemException(response.StatusCode, code, detail);
    }
}
