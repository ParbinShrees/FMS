using System.Xml.Linq;
using Futsal.Application.Notifications;
namespace Futsal.Tests;
public sealed class LocalizationCoverageTests
{
 private static string Root(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"AGENTS.md")))d=d.Parent;return d!.FullName;}
 [Fact]public void Every_English_resource_key_has_a_nonempty_Nepali_translation()
 {
  foreach(var path in Directory.EnumerateFiles(Path.Combine(Root(),"src"),"*.resx",SearchOption.AllDirectories).Where(x=>!x.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)&&!x.EndsWith(".ne.resx")))
  {
   var ne=path.EndsWith(".en.resx")?path[..^8]+".ne.resx":path[..^5]+".ne.resx";Assert.True(File.Exists(ne),ne);
   var translated=XDocument.Load(ne).Root!.Elements("data").ToDictionary(x=>x.Attribute("name")!.Value,x=>x.Element("value")?.Value);
   foreach(var key in XDocument.Load(path).Root!.Elements("data").Select(x=>x.Attribute("name")!.Value))Assert.True(translated.TryGetValue(key,out var value)&&!string.IsNullOrWhiteSpace(value),Path.GetFileName(path)+":"+key);
  }
 }
 [Fact]public void Bs_calendar_matches_independent_known_dates_across_2075_to_2090()
 {
  var rows=File.ReadAllLines(Path.Combine(Root(),"tests/Futsal.Tests/Fixtures/bs-reference.csv")).Skip(1);
  foreach(var row in rows){var parts=row.Split(',');var ad=DateOnly.ParseExact(parts[1],"yyyy-MM-dd");Assert.Equal(parts[0]+" BS",NepalDate.Bs(ad));Assert.Equal(ad,NepalDate.Ad(parts[0]));}
 }
}
