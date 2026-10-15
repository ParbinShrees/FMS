using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Mail;
using Futsal.Application.Abstractions;
using Futsal.Contracts;
using Futsal.Api.Identity;
using Futsal.Domain.Pilot;
using Futsal.Domain.Locations;
using Futsal.Domain.Identity;
using Futsal.Domain.Bookings;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
namespace Futsal.Api.Pilot;

public sealed class OnboardingService(FutsalDbContext db, ICurrentUser current, IClock clock, PasswordHasher<AppUser> hasher)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<OnboardingView> GetAsync(CancellationToken ct) { var s = await db.OnboardingStates.SingleOrDefaultAsync(ct); return s == null ? new(0, null, null, await db.Venues.AnyAsync(ct)) : new(s.Step, JsonSerializer.Deserialize<OnboardingDraft>(s.Draft, Json), s.VenueId, s.CompletedAt != null); }
    public async Task SaveAsync(OnboardingSave input, CancellationToken ct)
    {
        if (input.Step is < 0 or > 6 || JsonSerializer.Serialize(input.Draft, Json).Length > 40000) throw Invalid();
        var org = current.OrganizationId ?? throw new UnauthorizedAccessException(); await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"onboarding:" + org},0))", ct); var s = await db.OnboardingStates.SingleOrDefaultAsync(ct); if (s?.CompletedAt != null) throw new ApiException(409, "ONBOARDING_COMPLETE", "ONBOARDING_COMPLETE");
        if (s == null) { s = new() { OrganizationId = org }; db.OnboardingStates.Add(s); }
        s.Step = input.Step; s.Draft = JsonSerializer.Serialize(input.Draft, Json); s.UpdatedAt = clock.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task<OnboardingResult> FinishAsync(CancellationToken ct)
    {
        var org = current.OrganizationId ?? throw new UnauthorizedAccessException(); await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"onboarding:" + org},0))", ct);
        var state = await db.OnboardingStates.SingleOrDefaultAsync(ct) ?? throw Invalid(); if (state.CompletedAt != null) return new(state.VenueId!.Value, []);
        await Security.SubscriptionGuard.RequireWritableAsync(db, clock, org, ct); var d = JsonSerializer.Deserialize<OnboardingDraft>(state.Draft, Json) ?? throw Invalid();
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 160 || d.Address.Length > 300 || !System.Text.RegularExpressions.Regex.IsMatch(d.Slug, "^[a-z0-9][a-z0-9-]{2,79}$") || d.Courts.Count is < 1 or > 5 || d.Courts.Any(c => string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 80 || c.Price <= 0 || c.Price > 9999999999.99m || decimal.Round(c.Price, 2) != c.Price || c.SlotMinutes is not (30 or 60 or 90)) || d.Courts.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.Courts.Count || d.Hours.Count is < 1 or > 7 || d.Hours.Select(x => x.Weekday).Distinct().Count() != d.Hours.Count || d.Hours.Any(h => h.Weekday is < 0 or > 6 || h.Close <= h.Open) || d.Prices.Count > 30 || d.FreeCancelHours is < 0 or > 168 || d.AdvanceType is not ("FIXED" or "PERCENT") || d.AdvanceValue < 0 || d.AdvanceValue > 9999999999.99m || (d.AdvanceType == "PERCENT" && d.AdvanceValue > 100)) throw Invalid();
        var location = await db.Locations.SingleOrDefaultAsync(x => x.Id == d.LocationId && x.IsActive, ct); if (location == null || location.Type is not (LocationType.CITY or LocationType.AREA)) throw Invalid();
        if (await db.Venues.IgnoreQueryFilters().AnyAsync(x => x.Slug == d.Slug, ct)) throw new ApiException(409, "SLUG_TAKEN", "SLUG_TAKEN");
        var venue = new Venue { OrganizationId = org, Name = d.Name.Trim(), Slug = d.Slug, Address = d.Address, LocationId = location.Id, CityId = location.Type == LocationType.CITY ? location.Id : location.ParentId!.Value, FreeCancelHours = d.FreeCancelHours, AdvanceType = d.AdvanceType, AdvanceValue = d.AdvanceValue }; db.Venues.Add(venue);
        var courts = d.Courts.Select(x => new Court { OrganizationId = org, VenueId = venue.Id, Name = x.Name.Trim(), PricePerSlot = x.Price, SlotMinutes = x.SlotMinutes }).ToArray(); db.Courts.AddRange(courts);
        foreach (var c in courts) foreach (var h in d.Hours) db.CourtHours.Add(new() { CourtId = c.Id, Weekday = h.Weekday, OpenTime = h.Open, CloseTime = h.Close });
        foreach (var p in d.Prices) { if (p.CourtIndex < 0 || p.CourtIndex >= courts.Length || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 80 || p.Weekdays.Length is < 1 or > 7 || p.Weekdays.Any(x => x is < 0 or > 6) || p.To <= p.From || p.Price <= 0 || p.Price > 9999999999.99m || decimal.Round(p.Price, 2) != p.Price) throw Invalid(); db.PricingRules.Add(new() { OrganizationId = org, CourtId = courts[p.CourtIndex].Id, Name = p.Name, Weekdays = p.Weekdays, FromTime = p.From, ToTime = p.To, PricePerSlot = p.Price, Priority = p.Priority }); }
        var links = new List<InviteLink>(); if ((d.Staff?.Count ?? 0) > 10) throw Invalid(); foreach (var invite in d.Staff ?? [])
        { if (invite.Role is not ("Manager" or "Cashier") || !MailAddress.TryCreate(invite.Email, out var email) || email.Address != invite.Email || invite.Email.Length > 254) throw Invalid(); var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); db.StaffInvites.Add(new() { OrganizationId = org, VenueId = venue.Id, Email = invite.Email.ToLowerInvariant(), Role = invite.Role, TokenHash = Hash(token), ExpiresAt = clock.UtcNow.AddDays(7) }); links.Add(new(invite.Email, token)); }
        state.VenueId = venue.Id; state.CompletedAt = clock.UtcNow; state.UpdatedAt = clock.UtcNow; state.Step = 6; db.AuditLogs.Add(new() { OrganizationId = org, UserId = current.UserId, Action = "ONBOARDING_COMPLETED", EntityType = "Venue", EntityId = venue.Id, CreatedAt = clock.UtcNow }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(venue.Id, links);
    }
    public async Task AcceptAsync(InviteAccept input, CancellationToken ct)
    {
        if (input.Token.Length != 64 || !StaffPasswordPolicy.Accepts(input.Password) || string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Length > 160) throw Invalid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); var hash = Hash(input.Token); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"invite:" + hash},0))", ct);
        var invite = await db.StaffInvites.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TokenHash == hash, ct); if (invite == null || invite.AcceptedAt != null || invite.ExpiresAt <= clock.UtcNow) throw new ApiException(400, "INVITE_INVALID", "INVITE_INVALID");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"invite-email:" + invite.Email},0))", ct);
        if (await db.Users.AnyAsync(x => x.Email == invite.Email, ct)) throw new ApiException(409, "INVITE_EXISTING_ACCOUNT", "INVITE_EXISTING_ACCOUNT");
        var user = new AppUser { Email = invite.Email, FullName = input.FullName.Trim(), CreatedAt = clock.UtcNow }; user.PasswordHash = hasher.HashPassword(user, input.Password); db.Users.Add(user); db.Memberships.Add(new() { OrganizationId = invite.OrganizationId, VenueId = invite.VenueId, UserId = user.Id, Role = invite.Role == "Manager" ? OrganizationRole.MANAGER : OrganizationRole.CASHIER }); invite.AcceptedAt = clock.UtcNow; db.AuditLogs.Add(new() { OrganizationId = invite.OrganizationId, UserId = user.Id, Action = "STAFF_INVITE_ACCEPTED", EntityType = "StaffInvite", EntityId = invite.Id, CreatedAt = clock.UtcNow }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static ApiException Invalid() => new(400, "VALIDATION_FAILED", "VALIDATION_FAILED");
}
