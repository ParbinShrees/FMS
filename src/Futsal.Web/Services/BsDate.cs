namespace Futsal.Web.Services;

/// <summary>Display-only Bikram Sambat converter backed by the shared calendar dataset.</summary>
public static class BsDate
{
    public static string FromAd(DateOnly ad) => Futsal.Application.Notifications.NepalDate.Bs(ad);
    public static DateOnly KathmanduDate(DateTimeOffset utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, zone).DateTime);
    }
}
