namespace Futsal.Infrastructure.Payments;

public sealed class EsewaOptions
{
    public const string SectionName = "Esewa";

    public string FormUrl { get; set; } = "https://rc-epay.esewa.com.np/api/epay/main/v2/form";
    public string StatusUrl { get; set; } = "https://rc.esewa.com.np/api/epay/transaction/status/";
    public string ProductCode { get; set; } = "EPAYTEST";
    public string SecretKey { get; set; } = "8gBm/:&EnhH.1/q";
    public string SuccessUrl { get; set; } = "http://localhost:5000/v/callback/esewa";
    public string FailureUrl { get; set; } = "http://localhost:5000/v/callback/esewa";
}
