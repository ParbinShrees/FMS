using System.Net.Http.Headers;
using System.Net.Http.Json;
using Futsal.Application.Abstractions;
using Futsal.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace Futsal.Infrastructure.Notifications;

public sealed class ConsoleSender(ILogger<ConsoleSender> logger, IConfiguration configuration) : INotificationSender
{
    public Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        if (configuration["ASPNETCORE_ENVIRONMENT"] != "Development") throw new InvalidOperationException("Console SMS is only available in Development.");
        logger.LogInformation("Development SMS to {Phone}: {Text}", SmsText.Mask(message.Phone), message.Text);
        return Task.CompletedTask;
    }
}
/// <summary>Only provider-specific class. Confirm the TODO contract with your provider before enabling.</summary>
public sealed class HttpSmsSender(HttpClient client, IConfiguration configuration) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken ct)
    {
        var url = configuration["Sms:ProviderUrl"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || (!bool.TryParse(configuration["Sms:ContractConfirmed"], out var confirmed) || !confirmed))
            throw new InvalidOperationException("Configure and confirm the SMS provider contract first.");
        // TODO(provider): confirm bearer authentication, JSON names, idempotency header, and success response semantics.
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["Sms:ApiKey"] ?? throw new InvalidOperationException("SMS API key missing."));
        request.Headers.Add("Idempotency-Key", message.Reference);
        request.Content = JsonContent.Create(new { to = message.Phone, message = message.Text, sender = configuration["Sms:SenderId"] });
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
