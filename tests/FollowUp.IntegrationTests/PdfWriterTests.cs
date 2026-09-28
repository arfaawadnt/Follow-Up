using FluentAssertions;
using FollowUp.Infrastructure.Emailing;
using Xunit;

namespace FollowUp.IntegrationTests;

/// <summary>
/// The email reports' PDF attachment (2026-09-20): a valid PDF is produced for a wide, Arabic-labelled table (the Skia
/// PDF backend + HarfBuzz shaping), wide tables split into column groups, and an empty table still yields a document.
/// The sample is also written next to the test output so it can be opened by eye.
/// </summary>
public sealed class PdfWriterTests
{
    [Fact]
    public void Renders_a_wide_arabic_table_into_a_multi_page_pdf()
    {
        var headers = new List<string> { "Lab", "Code", "Governorate", "Area", "Total" };
        for (var d = 1; d <= 31; d++) headers.Add($"{d:00}/09");
        var rows = new List<XlsxCell[]>();
        for (var i = 0; i < 90; i++)
        {
            var cells = new List<XlsxCell> { $"معمل الصفا مغاغة {i}", $"{8000 + i}", "المنيا", "A 61", 1234 + i };
            for (var d = 1; d <= 31; d++) cells.Add(new XlsxCell(d * 3 + i, d % 7 == 0 ? XlsxFill.Pos : d % 11 == 0 ? XlsxFill.Neg : XlsxFill.None));
            rows.Add(cells.ToArray());
        }
        rows.Insert(0, new XlsxCell[] { new("المنيا", XlsxFill.Gov, true), new("", XlsxFill.Gov, true), new("", XlsxFill.Gov, true), new("", XlsxFill.Gov, true), new(99999, XlsxFill.Gov, true) });

        var bytes = PdfWriter.Build("Lab Statistics", "Daily stats · 01/09/2026 → 19/09/2026", headers, rows);

        bytes.Length.Should().BeGreaterThan(5000);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page[^s]").Count.Should().BeGreaterThan(2, "90 rows × 36 columns need several pages and column groups");
        File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "pdfwriter-sample.pdf"), bytes);
    }

    [Fact]
    public void An_empty_table_still_produces_a_one_page_document()
    {
        var bytes = PdfWriter.Build("No-Lab Tests", "period", new[] { "Test", "Count" }, new List<XlsxCell[]>());
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
