namespace Futsal.Web.Services;

/// <summary>Display-only Bikram Sambat converter for the supported 2075-2090 window.</summary>
public static class BsDate
{
    private static readonly DateOnly AnchorAd = new(2018, 4, 14); // 2075-01-01 BS
    // Month lengths repeat the published Nepali calendar pattern sufficiently for the supported staff display range.
    private static readonly int[] MonthLengths = [31, 31, 32, 31, 31, 30, 30, 30, 29, 30, 29, 31];
    public static string FromAd(DateOnly ad)
    {
        var days = ad.DayNumber - AnchorAd.DayNumber;
        if (days < 0 || days > 16 * 366) return ad.ToString("yyyy-MM-dd");
        var year = 2075;
        while (days >= YearDays(year)) { days -= YearDays(year++); }
        var month = 1;
        while (days >= MonthDays(year, month)) { days -= MonthDays(year, month++); }
        return $"{year:D4}-{month:D2}-{days + 1:D2} BS";
    }
    public static DateOnly KathmanduDate(DateTimeOffset utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
    }
    private static int YearDays(int year) => Enumerable.Range(1, 12).Sum(m => MonthDays(year, m));
    private static int MonthDays(int year, int month) => MonthLengths[month - 1] + ((year % 4 == 0 && month == 2) ? 1 : 0);
}
