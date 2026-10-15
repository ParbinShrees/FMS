using System.Text.Json;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Application.Abstractions;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Domain.Locations;
using Futsal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Futsal.Api.Controllers;

[ApiController, Route("api/v1/admin/venues/{venueId:guid}/payment-settings"), Authorize(Policy = "OwnerOnly")]
public sealed class VenuePaymentSettingsController(
    FutsalDbContext db,
    ICurrentUser current,
    IClock clock,
    IVenuePaymentProtector protector) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<VenuePaymentSettingsDto>> Get(Guid venueId, CancellationToken ct)
    {
        var orgId = current.OrganizationId ?? throw new UnauthorizedAccessException();
        var venue = await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId && v.OrganizationId == orgId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Venue was not found.");

        var settings = await db.VenuePaymentSettings
            .SingleOrDefaultAsync(s => s.VenueId == venue.Id && s.Method == PaymentMethod.ESEWA, ct);

        if (settings == null)
        {
            return Ok(new VenuePaymentSettingsDto(
                IsEnabled: false,
                MerchantCode: "",
                HasSecret: false,
                UpdatedAt: null));
        }

        // Secret is NEVER returned by any API!
        return Ok(new VenuePaymentSettingsDto(
            IsEnabled: settings.IsEnabled,
            MerchantCode: settings.MerchantCode,
            HasSecret: !string.IsNullOrWhiteSpace(settings.EncryptedSecret),
            UpdatedAt: settings.UpdatedAt));
    }

    [HttpPut]
    public async Task<ActionResult<VenuePaymentSettingsDto>> Update(
        Guid venueId,
        [FromBody] UpdateVenuePaymentSettingsRequest request,
        CancellationToken ct)
    {
        var orgId = current.OrganizationId ?? throw new UnauthorizedAccessException();
        var venue = await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId && v.OrganizationId == orgId, ct)
            ?? throw new ApiException(404, "NOT_FOUND", "Venue was not found.");

        if (string.IsNullOrWhiteSpace(request.MerchantCode))
            throw new ApiException(400, "VALIDATION_FAILED", "merchantCode is required.");

        var settings = await db.VenuePaymentSettings
            .SingleOrDefaultAsync(s => s.VenueId == venue.Id && s.Method == PaymentMethod.ESEWA, ct);

        var isNew = settings == null;
        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(request.SecretKey))
                throw new ApiException(400, "VALIDATION_FAILED", "secretKey is required when configuring payment settings.");

            settings = new VenuePaymentSettings
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                VenueId = venue.Id,
                Method = PaymentMethod.ESEWA,
                IsEnabled = request.IsEnabled,
                MerchantCode = request.MerchantCode.Trim(),
                EncryptedSecret = protector.Protect(request.SecretKey.Trim()),
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            };
            db.VenuePaymentSettings.Add(settings);
        }
        else
        {
            settings!.IsEnabled = request.IsEnabled;
            settings.MerchantCode = request.MerchantCode.Trim();
            if (!string.IsNullOrWhiteSpace(request.SecretKey))
            {
                settings.EncryptedSecret = protector.Protect(request.SecretKey.Trim());
            }
            settings.UpdatedAt = clock.UtcNow;
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = orgId,
            UserId = current.UserId,
            Action = "PAYMENT_SETTINGS_UPDATED",
            EntityType = "VenuePaymentSettings",
            EntityId = settings.Id,
            Before = null,
            After = JsonSerializer.Serialize(new { venueId, isEnabled = request.IsEnabled, merchantCode = request.MerchantCode }, JsonOptions),
            Ip = HttpContext.Connection.RemoteIpAddress,
            CreatedAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return Ok(new VenuePaymentSettingsDto(
            IsEnabled: settings.IsEnabled,
            MerchantCode: settings.MerchantCode,
            HasSecret: !string.IsNullOrWhiteSpace(settings.EncryptedSecret),
            UpdatedAt: settings.UpdatedAt));
    }
}
