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

public sealed record PublicVenueCardDto(
    Guid Id,
    string Name,
    string LocationPath,
    string Slug,
    double? DistanceKm,
    decimal? LowestPrice,
    string? PhotoPlaceholder,
    int CourtCount,
    string[] Surfaces,
    string[] AmenityCodes);

public sealed record LocationItemDto(
    Guid Id,
    Guid? ParentId,
    string Type,
    string NameEn,
    string NameNe,
    string Slug,
    string Path,
    decimal? Latitude,
    decimal? Longitude);

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

    public Task<BookingQuoteDto?> QuoteAsync(StaffBookingDto booking, CancellationToken ct = default) =>
        PostAsync<StaffBookingDto, BookingQuoteDto>("api/v1/staff/bookings/quote", booking, null, ct);

    public Task<BookingDto?> CreateBookingAsync(StaffBookingDto booking, CancellationToken ct = default) =>
        PostAsync<StaffBookingDto, BookingDto>("api/v1/staff/bookings", booking, Guid.NewGuid().ToString("N"), ct);

    // Public / Player endpoints
    public async Task<IReadOnlyList<LocationItemDto>> GetLocationsAsync(Guid? parentId = null, CancellationToken ct = default)
    {
        var uri = parentId is null ? "api/v1/public/locations" : $"api/v1/public/locations?parentId={parentId.Value}";
        return await GetAsync<List<LocationItemDto>>(uri, ct) ?? [];
    }

    public async Task<IReadOnlyList<LocationItemDto>> SearchLocationsAsync(string query, CancellationToken ct = default) =>
        await GetAsync<List<LocationItemDto>>($"api/v1/public/locations/search?q={Uri.EscapeDataString(query)}", ct) ?? [];

    public async Task<IReadOnlyList<PublicVenueCardDto>> SearchVenuesAsync(
        string? city = null, string? area = null, string? near = null, double? radiusKm = null,
        string? surface = null, decimal? minPrice = null, decimal? maxPrice = null, string? amenities = null, string? sort = null,
        CancellationToken ct = default)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(city)) queryParams.Add($"city={Uri.EscapeDataString(city)}");
        if (!string.IsNullOrWhiteSpace(area)) queryParams.Add($"area={Uri.EscapeDataString(area)}");
        if (!string.IsNullOrWhiteSpace(near)) queryParams.Add($"near={Uri.EscapeDataString(near)}");
        if (radiusKm.HasValue) queryParams.Add($"radiusKm={radiusKm.Value}");
        if (!string.IsNullOrWhiteSpace(surface)) queryParams.Add($"surface={Uri.EscapeDataString(surface)}");
        if (minPrice.HasValue) queryParams.Add($"minPrice={minPrice.Value}");
        if (maxPrice.HasValue) queryParams.Add($"maxPrice={maxPrice.Value}");
        if (!string.IsNullOrWhiteSpace(amenities)) queryParams.Add($"amenities={Uri.EscapeDataString(amenities)}");
        if (!string.IsNullOrWhiteSpace(sort)) queryParams.Add($"sort={Uri.EscapeDataString(sort)}");

        var qs = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
        return await GetAsync<List<PublicVenueCardDto>>($"api/v1/public/venues{qs}", ct) ?? [];
    }

    public async Task<PublicVenueDetailDto?> GetVenueDetailAsync(string slug, CancellationToken ct = default) =>
        await GetAsync<PublicVenueDetailDto>($"api/v1/public/venues/{Uri.EscapeDataString(slug)}", ct);

    public async Task<IReadOnlyList<AvailabilitySlotDto>> GetPublicAvailabilityAsync(string slug, DateOnly date, CancellationToken ct = default) =>
        await GetAsync<List<AvailabilitySlotDto>>($"api/v1/public/venues/{Uri.EscapeDataString(slug)}/availability?date={date:yyyy-MM-dd}", ct) ?? [];

    public Task<BookingDto?> CreateHoldAsync(Guid courtId, DateTimeOffset startAt, DateTimeOffset endAt, CancellationToken ct = default) =>
        PostAsync<object, BookingDto>("api/v1/bookings/holds", new { courtId, startAt, endAt }, Guid.NewGuid().ToString("N"), ct);

    public Task<InitiatePaymentResponseDto?> InitiateEsewaPaymentAsync(Guid bookingId, CancellationToken ct = default) =>
        PostAsync<InitiatePaymentRequestDto, InitiatePaymentResponseDto>("api/v1/payments/esewa/initiate", new(bookingId), null, ct);

    public Task<VerifyPaymentResponseDto?> VerifyEsewaPaymentAsync(string data, CancellationToken ct = default) =>
        PostAsync<VerifyPaymentRequestDto, VerifyPaymentResponseDto>("api/v1/payments/esewa/verify", new(data), null, ct);

    public async Task<IReadOnlyList<BookingDto>> GetMyBookingsAsync(CancellationToken ct = default) =>
        await GetAsync<List<BookingDto>>("api/v1/bookings/mine", ct) ?? [];

    public async Task CancelBookingAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        using var response = await Client().PostAsJsonAsync($"api/v1/bookings/{bookingId}/cancel", new { reason }, Json, ct);
        await EnsureAsync(response, ct);
    }

    // Admin / Staff payment management
    public async Task<VenuePaymentSettingsDto?> GetVenuePaymentSettingsAsync(Guid venueId, CancellationToken ct = default) =>
        await GetAsync<VenuePaymentSettingsDto>($"api/v1/admin/venues/{venueId}/payment-settings", ct);

    public async Task<VenuePaymentSettingsDto?> UpdateVenuePaymentSettingsAsync(Guid venueId, UpdateVenuePaymentSettingsRequestDto request, CancellationToken ct = default)
    {
        using var response = await Client().PutAsJsonAsync($"api/v1/admin/venues/{venueId}/payment-settings", request, Json, ct);
        await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<VenuePaymentSettingsDto>(Json, ct);
    }

    public async Task<IReadOnlyList<NeedsRefundPaymentDto>> GetNeedsRefundPaymentsAsync(CancellationToken ct = default) =>
        await GetAsync<List<NeedsRefundPaymentDto>>("api/v1/staff/payments/needs-refund", ct) ?? [];

    public async Task RecordPaymentRefundAsync(Guid paymentId, decimal amount, string method, string? reference, CancellationToken ct = default) =>
        await PostAsync<object, object>($"api/v1/staff/payments/{paymentId}/refund", new { amount, method, reference }, Guid.NewGuid().ToString("N"), ct);

    private async Task<T?> GetAsync<T>(string uri, CancellationToken ct)
    {
        using var response = await Client().GetAsync(uri, ct);
        await EnsureAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private async Task<T?> PostAsync<TRequest, T>(string uri, TRequest body, string? idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, options: Json) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await Client().SendAsync(request, ct);
        await EnsureAsync(response, ct);
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
