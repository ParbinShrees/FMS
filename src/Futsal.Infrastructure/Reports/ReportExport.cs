using System.Globalization;
using System.Text;
using Futsal.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
namespace Futsal.Infrastructure.Reports;

public static class ReportExport
{
    static ReportExport()
    {
        using var font = typeof(ReportExport).Assembly.GetManifestResourceStream("Futsal.Infrastructure.Reports.Fonts.NotoSansDevanagari.ttf")!;
        QuestPDF.Drawing.FontManager.RegisterFont(font);
    }

    private static readonly System.Resources.ResourceManager Labels = new("Futsal.Application.Reports.Labels", typeof(ReportRules).Assembly);
    private static string L(string key) => Labels.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static byte[] Csv(IEnumerable<ReportData> rows)
    {
        var lines = new List<string> { string.Join(",", new[] { "Category", "Label", "Amount", "Count", "Value", "Detail" }.Select(x => ReportRules.CsvCell(L(x)))) };
        lines.AddRange(rows.Select(r => string.Join(",", new[] { L(r.Category), L(r.Label), r.Amount.ToString(CultureInfo.InvariantCulture), r.Count.ToString(CultureInfo.InvariantCulture), r.Value.ToString(CultureInfo.InvariantCulture), r.Detail }.Select(ReportRules.CsvCell))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines))).ToArray();
    }
    public static byte[] Pdf(string title, IReadOnlyList<ReportData> rows)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape()); page.Margin(30); page.DefaultTextStyle(x => x.FontFamily("Noto Sans Devanagari").FontSize(9));
            page.Header().Text(title).FontSize(16);
            page.Content().PaddingVertical(12).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(2); });
                t.Header(h => { foreach (var label in new[] { "Category", "Label", "Amount", "Count", "Value", "Detail" }) h.Cell().Text(L(label)).Bold(); });
                foreach (var r in rows) foreach (var value in new[] { L(r.Category), L(r.Label), r.Amount.ToString("0.00", CultureInfo.InvariantCulture), r.Count.ToString(CultureInfo.InvariantCulture), r.Value.ToString("0.00", CultureInfo.InvariantCulture), r.Detail ?? "" }) t.Cell().Padding(3).Text(value);
            });
            page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
        })).GeneratePdf();
    }
}
