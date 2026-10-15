using NepDate;
using System.Globalization;
namespace Futsal.Application.Notifications;
public static class NepalDate
{
 private static readonly DateOnly Anchor=new(2018,4,14);
 private static readonly int[][] Months=Load();
 private static int[][] Load(){using var stream=typeof(NepalDate).Assembly.GetManifestResourceStream("Futsal.Application.Notifications.bs-months.csv")!;using var reader=new StreamReader(stream);return reader.ReadToEnd().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(row=>row.Trim().Split(',').Skip(1).Select(x=>int.Parse(x,CultureInfo.InvariantCulture)).ToArray()).ToArray();}
 public static DateOnly Ad(string text)
 {
  var normalized=string.Concat(text.Replace(" BS","",StringComparison.OrdinalIgnoreCase).Select(c=>c is >= '?' and <= '?'?(char)('0'+c-'?'):c));
  var values=normalized.Split(['-','/']).Select(x=>int.Parse(x,CultureInfo.InvariantCulture)).ToArray();
  if(values.Length!=3)throw new ArgumentException("Invalid BS date");
  var y=values[0];var m=values[1];var d=values[2];
  if(y<2075||y>2090)return DateOnly.FromDateTime(NepaliDate.Parse(normalized).EnglishDate);
  if(m<1||m>12||d<1||d>Months[y-2075][m-1])throw new ArgumentException("Invalid BS date");
  return Anchor.AddDays(Months.Take(y-2075).Sum(row=>row.Sum())+Months[y-2075].Take(m-1).Sum()+d-1);
 }
 public static string Bs(DateOnly date)
 {
  int delta=date.DayNumber-Anchor.DayNumber;
  if(delta<0||delta>=Months.Sum(row=>row.Sum())){var bs=new NepaliDate(date.ToDateTime(TimeOnly.MinValue));return $"{bs.Year:D4}-{bs.Month:D2}-{bs.Day:D2} BS";}
  int y=0,m=0;while(delta>=Months[y].Sum())delta-=Months[y++].Sum();while(delta>=Months[y][m])delta-=Months[y][m++];return $"{2075+y:D4}-{m+1:D2}-{delta+1:D2} BS";
 }
}
