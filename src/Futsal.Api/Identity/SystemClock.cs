using Futsal.Application.Abstractions;

namespace Futsal.Api.Identity;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
