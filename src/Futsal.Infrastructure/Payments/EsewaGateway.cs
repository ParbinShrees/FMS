using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Futsal.Infrastructure.Payments;

public sealed class EsewaGateway(
    HttpClient httpClient,
    IOptions<EsewaOptions> options,
    ILogger<EsewaGateway> logger) : IPaymentGateway
{
    private readonly EsewaOptions _options = options.Value;

    public PaymentMethod Method => PaymentMethod.ESEWA;

    public Task<PaymentInitiation> InitiateAsync(PaymentRequest request, CancellationToken ct)
    {
        var productCode = string.IsNullOrWhiteSpace(request.ProductCode) ? _options.ProductCode : request.ProductCode;
        var secretKey = string.IsNullOrWhiteSpace(request.SecretKey) ? _options.SecretKey : request.SecretKey;
        var successUrl = string.IsNullOrWhiteSpace(request.SuccessUrl) ? _options.SuccessUrl : request.SuccessUrl;
        var failureUrl = string.IsNullOrWhiteSpace(request.FailureUrl) ? _options.FailureUrl : request.FailureUrl;

        var amountStr = FormatAmount(request.Amount);
        var signedFieldNames = "total_amount,transaction_uuid,product_code";
        var message = $"total_amount={amountStr},transaction_uuid={request.TransactionUuid},product_code={productCode}";
        var signature = GenerateSignature(secretKey, message);

        var formFields = new Dictionary<string, string>
        {
            ["amount"] = amountStr,
            ["tax_amount"] = "0",
            ["total_amount"] = amountStr,
            ["transaction_uuid"] = request.TransactionUuid,
            ["product_code"] = productCode,
            ["product_service_charge"] = "0",
            ["product_delivery_charge"] = "0",
            ["success_url"] = successUrl,
            ["failure_url"] = failureUrl,
            ["signed_field_names"] = signedFieldNames,
            ["signature"] = signature
        };

        var initiation = new PaymentInitiation(
            GatewayUrl: _options.FormUrl,
            Method: "POST",
            FormFields: formFields,
            TransactionUuid: request.TransactionUuid,
            Amount: request.Amount);

        return Task.FromResult(initiation);
    }

    public async Task<PaymentVerification> VerifyAsync(PaymentCallback callback, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callback.EncodedData))
        {
            return new PaymentVerification(PaymentStatus.FAILED, callback.TransactionUuid ?? "", null, 0, null, false, "Missing encoded data.");
        }

        string jsonText;
        try
        {
            var cleanBase64 = callback.EncodedData.Trim().Replace(" ", "+");
            var mod4 = cleanBase64.Length % 4;
            if (mod4 > 0) cleanBase64 += new string('=', 4 - mod4);
            var bytes = Convert.FromBase64String(cleanBase64);
            jsonText = Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decode eSewa response Base64 payload.");
            return new PaymentVerification(PaymentStatus.FAILED, callback.TransactionUuid ?? "", null, 0, null, false, "Invalid Base64 payload.");
        }

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var signature = root.TryGetProperty("signature", out var sig) ? sig.GetString() ?? "" : "";
        var signedFieldNames = root.TryGetProperty("signed_field_names", out var sfn) ? sfn.GetString() ?? "" : "";
        var txnUuid = root.TryGetProperty("transaction_uuid", out var tu) ? tu.GetString() ?? "" : callback.TransactionUuid ?? "";
        var txnCode = root.TryGetProperty("transaction_code", out var tc) ? tc.GetString() : null;
        var productCode = root.TryGetProperty("product_code", out var pc) ? pc.GetString() ?? "" : callback.ProductCode ?? _options.ProductCode;
        var secretKey = string.IsNullOrWhiteSpace(callback.SecretKey) ? _options.SecretKey : callback.SecretKey;

        var amount = ExtractAmount(root);
        if (callback.Amount is decimal expectedAmount && amount != expectedAmount)
        {
            logger.LogWarning("Amount mismatch in eSewa callback. Expected {Expected}, received {Received}", expectedAmount, amount);
            return new PaymentVerification(PaymentStatus.FAILED, txnUuid, txnCode, amount, jsonText, false, "Amount mismatch.");
        }

        // Recompute signature over signed_field_names in order
        if (string.IsNullOrWhiteSpace(signedFieldNames) || string.IsNullOrWhiteSpace(signature))
        {
            return new PaymentVerification(PaymentStatus.FAILED, txnUuid, txnCode, amount, jsonText, false, "Missing signature fields.");
        }

        var fields = signedFieldNames.Split(',', StringSplitOptions.TrimEntries);
        var parts = new List<string>(fields.Length);
        foreach (var field in fields)
        {
            var val = root.TryGetProperty(field, out var elem) ? elem.ToString() : "";
            parts.Add($"{field}={val}");
        }
        var message = string.Join(",", parts);

        if (!VerifySignature(secretKey, message, signature))
        {
            logger.LogWarning("eSewa callback signature verification failed for txn {TxnUuid}.", txnUuid);
            return new PaymentVerification(PaymentStatus.FAILED, txnUuid, txnCode, amount, jsonText, false, "Invalid signature.");
        }

        // Call status check API server-to-server
        var statusVerification = await CheckStatusInternalAsync(txnUuid, amount, productCode, ct);
        if (!statusVerification.IsSuccess)
        {
            return statusVerification;
        }

        var finalRef = statusVerification.GatewayRef ?? txnCode;
        return new PaymentVerification(
            Status: PaymentStatus.COMPLETED,
            TransactionUuid: txnUuid,
            GatewayRef: finalRef,
            Amount: amount,
            RawResponse: jsonText,
            IsSuccess: true);
    }

    public Task<PaymentVerification> CheckStatusAsync(string transactionUuid, decimal amount, CancellationToken ct) =>
        CheckStatusInternalAsync(transactionUuid, amount, _options.ProductCode, ct);

    public async Task<PaymentVerification> CheckStatusInternalAsync(string transactionUuid, decimal amount, string productCode, CancellationToken ct)
    {
        var amountStr = FormatAmount(amount);
        var url = $"{_options.StatusUrl.TrimEnd('/')}/?product_code={Uri.EscapeDataString(productCode)}&total_amount={Uri.EscapeDataString(amountStr)}&transaction_uuid={Uri.EscapeDataString(transactionUuid)}";

        try
        {
            using var response = await httpClient.GetAsync(url, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("eSewa status check returned HTTP {StatusCode}: {Content}", response.StatusCode, content);
                return new PaymentVerification(PaymentStatus.FAILED, transactionUuid, null, amount, content, false, $"Status check API failed with {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            var returnedTxnUuid = root.TryGetProperty("transaction_uuid", out var tu) ? tu.GetString() ?? "" : "";
            var txnCode = root.TryGetProperty("transaction_code", out var tc) ? tc.GetString() : null;
            var returnedAmount = ExtractAmount(root);

            if (!string.Equals(status, "COMPLETE", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("eSewa status check for txn {TxnUuid} is {Status}", transactionUuid, status);
                var paymentStatus = status switch
                {
                    "CANCELED" or "FAILED" => PaymentStatus.FAILED,
                    _ => PaymentStatus.PENDING
                };
                return new PaymentVerification(paymentStatus, transactionUuid, txnCode, returnedAmount, content, false, $"Status is {status}");
            }

            if (!string.Equals(returnedTxnUuid, transactionUuid, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("eSewa status check txn UUID mismatch. Expected {Expected}, got {Returned}", transactionUuid, returnedTxnUuid);
                return new PaymentVerification(PaymentStatus.FAILED, transactionUuid, txnCode, returnedAmount, content, false, "Transaction UUID mismatch.");
            }

            if (Math.Abs(returnedAmount - amount) >= 0.01m)
            {
                logger.LogWarning("eSewa status check amount mismatch. Expected {Expected}, got {Returned}", amount, returnedAmount);
                return new PaymentVerification(PaymentStatus.FAILED, transactionUuid, txnCode, returnedAmount, content, false, "Amount mismatch.");
            }

            return new PaymentVerification(PaymentStatus.COMPLETED, transactionUuid, txnCode, returnedAmount, content, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call eSewa status check API for txn {TxnUuid}", transactionUuid);
            return new PaymentVerification(PaymentStatus.PENDING, transactionUuid, null, amount, null, false, ex.Message);
        }
    }

    public static string GenerateSignature(string secretKey, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var hash = HMACSHA256.HashData(keyBytes, messageBytes);
        return Convert.ToBase64String(hash);
    }

    public static bool VerifySignature(string secretKey, string message, string expectedSignature)
    {
        var computed = GenerateSignature(secretKey, message);
        var computedBytes = Encoding.UTF8.GetBytes(computed);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        if (computedBytes.Length != expectedBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(computedBytes, expectedBytes);
    }

    public static string FormatAmount(decimal amount) =>
        amount % 1 == 0
            ? ((long)amount).ToString(CultureInfo.InvariantCulture)
            : amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal ExtractAmount(JsonElement element)
    {
        if (element.TryGetProperty("total_amount", out var ta))
        {
            if (ta.ValueKind == JsonValueKind.Number && ta.TryGetDecimal(out var d)) return d;
            if (ta.ValueKind == JsonValueKind.String && decimal.TryParse(ta.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return 0m;
    }
}
