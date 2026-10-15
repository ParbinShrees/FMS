using NepDate;
namespace Futsal.Application.Notifications;

public static class NepalDate
{
    public static DateOnly Ad(string bs) => DateOnly.FromDateTime(NepaliDate.Parse(bs).EnglishDate);
    public static string Bs(DateOnly date)
    {
        var bs = new NepaliDate(date.ToDateTime(TimeOnly.MinValue));
        return $"{bs.Year:D4}-{bs.Month:D2}-{bs.Day:D2} BS";
    }
}
