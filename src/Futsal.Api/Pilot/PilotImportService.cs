using Futsal.Application.Pilot;
using Futsal.Application.Abstractions;
using Futsal.Contracts;
using Futsal.Api.Bookings;
using Futsal.Api.Contracts;
using Futsal.Api.Identity;
using Futsal.Domain.Bookings;
using Futsal.Domain.Identity;
using Futsal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
namespace Futsal.Api.Pilot;

public sealed class PilotImportService(FutsalDbContext db, BookingService bookings, ICurrentUser current, IClock clock)
{
    public async Task<ImportReport> RunAsync(ImportRequest input, bool booking, CancellationToken ct)
    {
        var org = current.OrganizationId ?? throw new UnauthorizedAccessException(); if (input.RunId == Guid.Empty) throw new ApiException(400, "VALIDATION_FAILED", "VALIDATION_FAILED");
        List<string[]> rows; try { rows = PilotCsv.Parse(input.Csv, booking); } catch (ArgumentException ex) { throw new ApiException(400, ex.Message, ex.Message); }
        if (!input.Preview) await Security.SubscriptionGuard.RequireWritableAsync(db, clock, org, ct);
        var report = new ImportReport([]); var seen = new HashSet<string>(); var ranges = new List<(Guid Court, DateTimeOffset Start, DateTimeOffset End)>();
        for (var i = 0; i < rows.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); db.ChangeTracker.Clear(); var cells = rows[i]; var code = ""; Guid? id = null;
            try
            {
                if (cells.Length != (booking ? 5 : 2)) throw new ArgumentException("INVALID_COLUMNS");
                var phone = PilotCsv.Phone(cells[booking ? 3 : 1]);
                if (!booking)
                {
                    if (string.IsNullOrWhiteSpace(cells[0]) || cells[0].Length > 160) throw new ArgumentException("INVALID_NAME");
                    if (PilotCsv.Formula(cells[0])) throw new ArgumentException("FORMULA_CELL");
                    if (!seen.Add(phone) || await db.Customers.AnyAsync(x => x.Phone == phone, ct)) throw new ArgumentException("DUPLICATE_PHONE");
                    if (!input.Preview) { await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"customer-import:" + org + phone},0))", ct); if (await db.Customers.AnyAsync(x => x.Phone == phone, ct)) throw new ArgumentException("DUPLICATE_PHONE"); db.Customers.Add(new() { OrganizationId = org, FullName = cells[0].Trim(), Phone = phone, CreatedAt = clock.UtcNow }); db.AuditLogs.Add(new(){OrganizationId=org,UserId=current.UserId,Action="CUSTOMER_IMPORTED",EntityType="Import",EntityId=input.RunId,After=System.Text.Json.JsonSerializer.Serialize(new{Row=i+2}),CreatedAt=clock.UtcNow}); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
                }
                else
                {
                    var matches = await db.Courts.Where(x => x.Name == cells[0] || x.Id.ToString() == cells[0]).Take(2).ToListAsync(ct); if (matches.Count != 1) throw new ArgumentException("COURT_NOT_FOUND"); var court = matches[0];
                    // An explicit offset is mandatory: local times never silently depend on the server timezone.
                    DateTimeOffset Parse(string value) { if (!(value.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(value, "[+-][0-9]{2}:[0-9]{2}$")) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new ArgumentException("INVALID_TIME"); return time.ToUniversalTime(); }
                    var start = Parse(cells[1]); var end = Parse(cells[2]); if (start <= clock.UtcNow || end <= start) throw new ArgumentException("FUTURE_BOOKING_REQUIRED");
                    if (!decimal.TryParse(cells[4], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var paid) || paid < 0 || paid > 9999999999.99m || decimal.Round(paid, 2) != paid) throw new ArgumentException("INVALID_AMOUNT");
                    if (input.Preview)
                    {
                        await bookings.PreviewStaffBookingAsync(new(court.Id, phone, "Customer", start, end, InitialPayment: paid, InitialPaymentMethod: PaymentMethod.CASH), ct);
                        if (ranges.Any(x => x.Court == court.Id && x.Start < end && x.End > start) || await db.Bookings.AnyAsync(x => x.CourtId == court.Id && x.Status != BookingStatus.CANCELLED && x.Status != BookingStatus.EXPIRED && !(x.Status == BookingStatus.HELD && x.HoldExpiresAt < clock.UtcNow) && x.StartAt < end && x.EndAt > start, ct) || await db.BlockedPeriods.AnyAsync(x => x.CourtId == court.Id && x.StartAt < end && x.EndAt > start, ct)) throw new ArgumentException("SLOT_UNAVAILABLE"); ranges.Add((court.Id, start, end));
                    }
                    else
                    {
                        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Phone == phone, ct);
                        var result = await bookings.CreateStaffBookingAsync(new(court.Id, phone, customer?.FullName ?? "Customer", start, end, InitialPayment: paid, InitialPaymentMethod: PaymentMethod.CASH), $"import:{input.RunId}:{i}", null, ct); id = result.Id;
                    }
                }
            }
            catch (DbUpdateException ex) when (!booking && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { code = "DUPLICATE_PHONE"; }
            catch (ArgumentException ex) { code = ex.Message; }
            catch (ApiException ex) { code = ex.Code; }
            catch (BookingDomainException ex) { code = ex.Code; }
            report.Rows.Add(new(i + 2, code != "" ? "SKIPPED" : input.Preview ? "VALID" : "IMPORTED", code, cells.Select(PilotCsv.SafeCell).ToArray(), id));
        }
        db.ChangeTracker.Clear(); if (!input.Preview) { db.AuditLogs.Add(new() { OrganizationId = org, UserId = current.UserId, Action = booking ? "BOOKING_IMPORT" : "CUSTOMER_IMPORT", EntityType = "Import", EntityId = input.RunId, After = System.Text.Json.JsonSerializer.Serialize(new { report.Imported, report.Skipped }), CreatedAt = clock.UtcNow }); await db.SaveChangesAsync(ct); }
        return report;
    }
}
