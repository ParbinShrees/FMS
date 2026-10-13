using Futsal.Application.Abstractions;

namespace Futsal.Api.Identity;

public sealed partial class FakeOtpSender(ILogger<FakeOtpSender> logger) : IOtpSender
{
    public Task SendAsync(string phone, string code, CancellationToken cancellationToken)
    {
        logger.LogInformation("Development OTP sent to {MaskedPhone}: {OtpCode}", Mask(phone), code);
        return Task.CompletedTask;
    }

    private static string Mask(string phone) => phone.Length < 5 ? "***" : $"{phone[..3]}***{phone[^2..]}";
}
