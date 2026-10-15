using System.Globalization;
using System.Text.RegularExpressions;
namespace Futsal.Application.Notifications;

public static partial class SmsText
{
    private const string Basic = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
    private const string Extended = "^{}\\[~]|€\f";
    [GeneratedRegex(@"^\+9779[678][0-9]{8}$")] private static partial Regex PhonePattern();
    public static bool ValidPhone(string phone) => PhonePattern().IsMatch(phone);
    public static string Mask(string phone) => phone.Length < 4 ? "****" : "********" + phone[^4..];
    public static (int Characters, int Units, int Segments, bool Unicode) Count(string text)
    {
        bool unicode = text.Any(c => !Basic.Contains(c) && !Extended.Contains(c));
        int units = unicode ? text.Length : text.Sum(c => Extended.Contains(c) ? 2 : 1);
        int single = unicode ? 70 : 160, multi = unicode ? 67 : 153;
        return (text.EnumerateRunes().Count(), units, units == 0 ? 0 : units <= single ? 1 : (units + multi - 1) / multi, unicode);
    }
    public static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        return Regex.Replace(text, @"\{([a-z]+)\}", m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }
    public static string NepalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(instant, "Asia/Kathmandu").ToString("h:mm tt", CultureInfo.InvariantCulture);
}
