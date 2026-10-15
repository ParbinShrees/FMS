namespace Futsal.Application.Reports;

public static class ReportRules
{
    public static (DateTimeOffset Start, DateTimeOffset End) UtcRange(DateOnly from, DateOnly to, string timezone = "Asia/Kathmandu")
    {
        if (to < from || to.DayNumber - from.DayNumber > 366) throw new ArgumentException("Invalid report date range");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone)), new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone)));
    }
    public static string CsvCell(string? value)
    {
        value ??= "";
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') || value.StartsWith('\t') || value.StartsWith('\r')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
