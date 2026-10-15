namespace Futsal.Application.Common;
public static class ErrorMessages
{
 private static readonly System.Resources.ResourceManager Resources=new("Futsal.Application.Common.Errors",typeof(ErrorMessages).Assembly);
 public static string Get(string code,string fallback)=>Resources.GetString(code,System.Globalization.CultureInfo.CurrentUICulture)??(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ne"?Resources.GetString("INTERNAL_ERROR",System.Globalization.CultureInfo.CurrentUICulture)!:fallback);
}
