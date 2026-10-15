using Futsal.Web.Components;
using Futsal.Web.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddLocalization(options => options.ResourcesPath = "Localization");
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("FutsalApi", client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5139/"));
builder.Services.AddSingleton<TokenSessionStore>();
builder.Services.AddScoped<BffAuthState>();
builder.Services.AddScoped<ApiClient>();
builder.Services.AddSingleton<Futsal.Application.Abstractions.IClock, ReportClock>();
builder.Services.AddSingleton<TokenEndpointClient>();
var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Error", true); app.UseHsts(); }
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en", "ne").AddSupportedUICultures("en", "ne"));
app.UseAntiforgery();
app.MapStaticAssets();
app.MapPost("/bff/login", async (HttpContext context, TokenEndpointClient api, TokenSessionStore sessions, CancellationToken ct) =>
{
    var form = await context.Request.ReadFormAsync(ct);
    var token = await api.LoginAsync(form["identifier"].ToString(), form["password"].ToString(), ct);
    if (token is null) return Results.Redirect("/login?error=INVALID_CREDENTIALS");
    var sessionId = sessions.Put(token);
    context.Response.Cookies.Append(BffAuthState.CookieName, sessionId, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = token.RefreshExpiresAt - DateTimeOffset.UtcNow });
    return Results.Redirect("/admin");
}).DisableAntiforgery();
app.MapPost("/bff/logout", async (HttpContext context, TokenEndpointClient api, TokenSessionStore sessions, CancellationToken ct) =>
{
    var id = context.Request.Cookies[BffAuthState.CookieName];
    if (sessions.TryGet(id, out var token)) await api.LogoutAsync(token, ct);
    sessions.Remove(id); context.Response.Cookies.Delete(BffAuthState.CookieName, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict });
    return Results.Redirect("/login");
}).DisableAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
