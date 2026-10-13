namespace Futsal.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
