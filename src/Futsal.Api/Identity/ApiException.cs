namespace Futsal.Api.Identity;

public sealed class ApiException(int statusCode, string code, string title) : Exception(title)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
