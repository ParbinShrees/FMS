namespace Futsal.Application.Abstractions;

public interface IOtpSender
{
    Task SendAsync(string phone, string code, CancellationToken cancellationToken);
}
