namespace Futsal.Web.Services;

/// <summary>Display-only Bikram Sambat converter backed by the shared calendar dataset.</summary>
public static class BsDate
{
    public static string Display(DateOnly date,bool bsFirst=false,bool numerals=false){var ad=date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)+" AD";var bs=FromAd(date);var text=bsFirst?bs+" / "+ad:ad+" / "+bs;return numerals?string.Concat(text.Select(c=>c is >= '0' and <= '9'?(char)('०'+c-'0'):c)):text;}
    public static string Display(DateTimeOffset instant,bool bsFirst=false,bool numerals=false)=>Display(KathmanduDate(instant),bsFirst,numerals)+" "+TimeZoneInfo.ConvertTimeBySystemTimeZoneId(instant,"Asia/Kathmandu").ToString("h:mm tt",System.Globalization.CultureInfo.CurrentCulture);
    public static string FromAd(DateOnly ad) => Futsal.Application.Notifications.NepalDate.Bs(ad);
    public static DateOnly KathmanduDate(DateTimeOffset utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
    }
}
