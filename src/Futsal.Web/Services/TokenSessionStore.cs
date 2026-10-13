using System.Collections.Concurrent;
using Futsal.Contracts;

namespace Futsal.Web.Services;

public sealed class TokenSessionStore
{
    private readonly ConcurrentDictionary<string, TokenDto> _sessions = new();
    public string Put(TokenDto token) { var id = Convert.ToHexString(Guid.NewGuid().ToByteArray()); _sessions[id] = token; return id; }
    public bool TryGet(string? id, out TokenDto token) => id is not null && _sessions.TryGetValue(id, out token!);
    public void Remove(string? id) { if (id is not null) _sessions.TryRemove(id, out _); }
}

public sealed class BffAuthState(IHttpContextAccessor httpContextAccessor, TokenSessionStore sessions)
{
    public const string CookieName = "__Host-futsalpro-session";
    private string? SessionId => httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
    public TokenDto? Token => sessions.TryGet(SessionId, out var token) ? token : null;
    public bool IsAuthenticated => Token is not null;
}
