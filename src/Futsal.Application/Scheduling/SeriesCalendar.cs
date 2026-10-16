namespace Futsal.Application.Scheduling;
public static class SeriesCalendar
{
 public static DateOnly Today(DateTimeOffset now,string timezone)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,timezone).DateTime);
 public static DateTimeOffset Utc(DateOnly date,TimeOnly time,string timezone){var local=DateTime.SpecifyKind(date.ToDateTime(time),DateTimeKind.Unspecified);return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,TimeZoneInfo.FindSystemTimeZoneById(timezone)),TimeSpan.Zero);}
 public static IEnumerable<DateOnly> Dates(DateOnly first,DateOnly? last,short weekday,DateOnly horizonExclusive){for(var day=first.AddDays(((int)weekday-(int)first.DayOfWeek+7)%7);day<horizonExclusive&&(last==null||day<=last);day=day.AddDays(7))yield return day;}
}
