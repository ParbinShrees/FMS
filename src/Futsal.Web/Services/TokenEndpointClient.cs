using System.Net.Http.Json;
using Futsal.Contracts;

namespace Futsal.Web.Services;

public sealed class TokenEndpointClient(IHttpClientFactory factory)
{
    public async Task<TokenDto?> LoginAsync(string identifier, string password, CancellationToken ct,string? twoFactorCode=null)
    {
        var response = await factory.CreateClient("FutsalApi").PostAsJsonAsync("api/v1/auth/staff/login", new {identifier,password,twoFactorCode}, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct) : null;
    }
    public async Task<bool> NeedsOnboardingAsync(TokenDto token,CancellationToken ct)
    {
        if(token.Role!="Owner")return false;
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"api/v1/pilot/onboarding");
            request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token.AccessToken);
            using var response=await factory.CreateClient("FutsalApi").SendAsync(request,ct);
            return !response.IsSuccessStatusCode||(await response.Content.ReadFromJsonAsync<OnboardingView>(cancellationToken:ct))?.Complete!=true;
        }
        catch(HttpRequestException){return true;}
    }

    public async Task LogoutAsync(TokenDto token, CancellationToken ct) =>
        await factory.CreateClient("FutsalApi").PostAsJsonAsync("api/v1/auth/logout", new LogoutDto(token.RefreshToken), ct);

    public async Task<bool> RequestOtpAsync(string phone, CancellationToken ct, Guid? venueId = null)
    {
        var response = await factory.CreateClient("FutsalApi").PostAsJsonAsync("api/v1/auth/otp/request", new { phone, venueId, language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName }, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<TokenDto?> VerifyOtpAsync(string phone, string code, CancellationToken ct)
    {
        var response = await factory.CreateClient("FutsalApi").PostAsJsonAsync("api/v1/auth/otp/verify", new { phone, code }, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct) : null;
    }
}
