using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Json;
namespace Futsal.Api.Security;
public sealed partial class SafeLogFormatter:ITextFormatter
{
 private readonly JsonFormatter inner=new(renderMessage:true);
 [GeneratedRegex(@"(?i)(?:\+?977)?9[678][0-9]{8}|[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9.-]+\.[a-z]{2,}|eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|(?<![A-Za-z0-9])[0-9]{6}(?![A-Za-z0-9])")]private static partial Regex Personal();
 [GeneratedRegex("""(?i)((?:access_token|refresh_token|password|secret|api_key|authorization|otp|code)[\\"\s:=]+)[^\s,}"&]+""")]private static partial Regex Secret();
 public static string Scrub(string value)=>Personal().Replace(Secret().Replace(value,"$1[REDACTED]"),"[REDACTED]");
 public void Format(LogEvent logEvent,TextWriter output){using var text=new StringWriter();inner.Format(logEvent,text);output.Write(Scrub(text.ToString()));}
}
