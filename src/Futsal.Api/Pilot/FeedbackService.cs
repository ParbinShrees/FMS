using Futsal.Application.Abstractions;
using Futsal.Application.Pilot;
using Futsal.Contracts;
using Futsal.Domain.Pilot;
using Futsal.Infrastructure.Persistence;
using Futsal.Api.Identity;
using Microsoft.EntityFrameworkCore;
namespace Futsal.Api.Pilot;

public sealed class FeedbackService(FutsalDbContext db, ICurrentUser current, IClock clock)
{
    public async Task<Guid> SubmitAsync(FeedbackWrite input, CancellationToken ct)
    {
        if (input.Category is not ("bug" or "confusing" or "idea") || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 4000 || input.PageUrl.Length > 500 || !input.PageUrl.StartsWith("/admin", StringComparison.Ordinal) || input.PageUrl.Any(char.IsControl)) throw new ApiException(400, "VALIDATION_FAILED", "VALIDATION_FAILED");
        byte[]? screenshot = null; try { if (input.ScreenshotBase64 != null) { if (input.ScreenshotBase64.Length > PilotScreenshot.MaxBytes * 4 / 3 + 4) throw new ArgumentException(); screenshot = PilotScreenshot.Scan(Convert.FromBase64String(input.ScreenshotBase64)); } } catch (Exception ex) when (ex is ArgumentException or FormatException) { throw new ApiException(400, "INVALID_SCREENSHOT", "INVALID_SCREENSHOT"); }
        var item = new PilotFeedback { OrganizationId = current.OrganizationId ?? throw new UnauthorizedAccessException(), SubmittedBy = current.UserId ?? throw new UnauthorizedAccessException(), Category = input.Category, Text = input.Text.Trim(), PageUrl = input.PageUrl.Split('?')[0].Split('#')[0], Screenshot = screenshot, CreatedAt = clock.UtcNow }; db.PilotFeedback.Add(item); await db.SaveChangesAsync(ct); return item.Id;
    }
    public Task<List<FeedbackView>> ListAsync(CancellationToken ct) => db.PilotFeedback.OrderByDescending(x => x.CreatedAt).Take(200).Select(x => new FeedbackView(x.Id, x.OrganizationId, db.Organizations.Where(o => o.Id == x.OrganizationId).Select(o => o.Name).First(), x.Category, x.Text, x.PageUrl, x.Status, x.Screenshot != null, x.CreatedAt)).ToListAsync(ct);
    public async Task<byte[]> ScreenshotAsync(Guid id, CancellationToken ct) => (await db.PilotFeedback.SingleOrDefaultAsync(x => x.Id == id, ct))?.Screenshot ?? throw new ApiException(404, "NOT_FOUND", "NOT_FOUND");
}
