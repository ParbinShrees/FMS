using Microsoft.VisualBasic.FileIO;
using System.Text;
using System.Text.RegularExpressions;
namespace Futsal.Application.Pilot;

public static partial class PilotCsv
{
    public const int MaximumBytes = 48 * 1024; public const int MaximumRows = 200;
    public static string Phone(string value) { value = value.Trim(); if (Regex.IsMatch(value, "^9[678][0-9]{8}$")) value = "+977" + value; if (!Regex.IsMatch(value, "^\\+9779[678][0-9]{8}$")) throw new ArgumentException("INVALID_PHONE"); return value; }
    public static bool Formula(string value) => value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r');
    public static string SafeCell(string value) => Formula(value) ? "'" + value : value;
    public static List<string[]> Parse(string csv, bool bookings)
    {
        if (Encoding.UTF8.GetByteCount(csv) > MaximumBytes) throw new ArgumentException("IMPORT_TOO_LARGE");
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF'))) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false }; parser.SetDelimiters(","); var rows = new List<string[]>();
        try { while (!parser.EndOfData) { var row = parser.ReadFields()!; if (row.Any(x => x.Length > 2048) || rows.Count > MaximumRows) throw new ArgumentException("IMPORT_TOO_LARGE"); rows.Add(row); } } catch (MalformedLineException) { throw new ArgumentException("INVALID_CSV"); }
        var en = bookings ? new[] { "court", "start", "end", "customer phone", "amount paid" } : new[] { "name", "phone" }; var ne = bookings ? new[] { "कोर्ट", "सुरु", "अन्त्य", "ग्राहक फोन", "तिरेको रकम" } : new[] { "नाम", "फोन" };
        if (rows.Count == 0 || !(rows[0].Select(x => x.Trim().ToLowerInvariant()).SequenceEqual(en) || rows[0].Select(x => x.Trim()).SequenceEqual(ne))) throw new ArgumentException("INVALID_HEADERS"); rows.RemoveAt(0); return rows;
    }
}
