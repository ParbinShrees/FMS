using System.Net.Http.Json;
using Futsal.Contracts;

namespace Futsal.Web.Services;

public sealed class TokenEndpointClient(IHttpClientFactory factory)
{
    public async Task<TokenDto?> LoginAsync(string identifier, string password, CancellationToken ct)
    {
        var response = await factory.CreateClient("FutsalApi").PostAsJsonAsync("api/v1/auth/staff/login", new StaffLoginDto(identifier, password), ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct) : null;
    }
    public async Task LogoutAsync(TokenDto token, CancellationToken ct) =>
        await factory.CreateClient("FutsalApi").PostAsJsonAsync("api/v1/auth/logout", new LogoutDto(token.RefreshToken), ct);
}
