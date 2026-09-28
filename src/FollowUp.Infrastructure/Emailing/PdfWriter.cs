using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FollowUp.Infrastructure.Emailing;

/// <summary>
/// Server-side PDF rendering of a report table (2026-09-20: the email reports attach a PDF beside every Excel file).
/// Skia draws the pages into a PDF document; HarfBuzz shapes the text so Arabic lab / area names come out joined and
/// right-to-left (the standard PDF fonts cannot render Arabic at all). The typeface is Arial from the Windows font folder
/// (covers Latin + Arabic); Segoe UI is the fallback, then Skia's default.
/// Layout: A4 landscape, a title band with the period, a repeated coloured header row, thin borders, numbers right-aligned
/// with thousands separators, and the Pos / Neg / Gov colour flags of the statistics grids. A table wider than the page is
/// split into column groups, each group repeating the first <see cref="KeyColumns"/> columns so every page reads on its own.
/// </summary>
public static class PdfWriter
{
    public const string ContentType = "application/pdf";

    // A4 landscape in PDF points.
    private const float PageW = 841.89f, PageH = 595.28f, Margin = 28f;
    private const float FontSize = 7.5f, HeaderFontSize = 7.5f, TitleFontSize = 14f, SubFontSize = 9f;
    private const float RowH = 14f, CellPadX = 3f, MaxColW = 150f, MinColW = 22f;
    /// <summary>The leading columns repeated on every column group (the row's identity: e.g. governorate + area, lab + code).</summary>
    private const int KeyColumns = 2;

    private static readonly Lazy<SKTypeface> Regular = new(() => Load("arial.ttf", "segoeui.ttf"));
    private static readonly Lazy<SKTypeface> Bold = new(() => Load("arialbd.ttf", "segoeuib.ttf"));

    private static SKTypeface Load(params string[] files)
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var f in files)
        {
            var path = Path.Combine(fonts, f);
            if (File.Exists(path)) { var tf = SKTypeface.FromFile(path); if (tf is not null) return tf; }
        }
        return SKTypeface.Default;
    }

    public static byte[] Build(string title, string subtitle, IReadOnlyList<string> headers, IReadOnlyList<XlsxCell[]> rows)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata { Title = title, Creator = "Follow-Up", Producer = "Follow-Up" }))
        {
            using var text = new SKPaint { Typeface = Regular.Value, TextSize = FontSize, IsAntialias = true, Color = SKColors.Black, SubpixelText = true };
            using var bold = new SKPaint { Typeface = Bold.Value, TextSize = HeaderFontSize, IsAntialias = true, Color = SKColors.White, SubpixelText = true };
            using var shaper = new SKShaper(Regular.Value);
            using var boldShaper = new SKShaper(Bold.Value);

            var cellText = rows.Select(r => r.Select(Display).ToArray()).ToList();
            var widths = ColumnWidths(headers, cellText, text, bold, shaper, boldShaper);
            var groups = ColumnGroups(widths);
            var usableH = PageH - 2 * Margin;

            foreach (var group in groups)
            {
                var groupW = group.Sum(c => widths[c]);
                var x0 = Margin; // left-aligned; a narrow last group simply leaves room on the right
                int rowIndex = 0; var page = 1;
                while (rowIndex < rows.Count || (rows.Count == 0 && page == 1))
                {
                    using var canvas = doc.BeginPage(PageW, PageH);
                    var y = Margin;
                    y = DrawTitle(canvas, title, subtitle, groups.Count > 1 ? $"columns {headers[group[0]]} → {headers[group[^1]]}" : null, y, text, bold, shaper, boldShaper, page);
                    y = DrawHeader(canvas, headers, group, widths, x0, y, bold, boldShaper);
                    while (rowIndex < rows.Count && y + RowH <= Margin + usableH - 12f)
                    {
                        DrawRow(canvas, rows[rowIndex], cellText[rowIndex], group, widths, x0, y, text, bold, shaper, boldShaper);
                        y += RowH; rowIndex++;
                    }
                    if (rows.Count == 0) DrawShaped(canvas, "No data for this period.", x0 + CellPadX, y + 10f, text, shaper);
                    DrawFooter(canvas, page, text, shaper);
                    doc.EndPage();
                    page++;
                    if (rows.Count == 0) break;
                }
            }
            doc.Close();
        }
        return ms.ToArray();
    }

    // ---- layout ----

    private static float[] ColumnWidths(IReadOnlyList<string> headers, List<string[]> rows, SKPaint text, SKPaint bold, SKShaper shaper, SKShaper boldShaper)
    {
        var widths = new float[headers.Count];
        for (var c = 0; c < headers.Count; c++)
        {
            var w = Measure(headers[c], bold, boldShaper);
            foreach (var r in rows) if (c < r.Length) w = Math.Max(w, Measure(r[c], text, shaper));
            widths[c] = Math.Clamp(w + 2 * CellPadX, MinColW, MaxColW);
        }
        return widths;
    }

    /// <summary>Splits the columns into groups that fit the page width; every group after the first repeats the key columns.</summary>
    private static List<int[]> ColumnGroups(float[] widths)
    {
        var usable = PageW - 2 * Margin;
        var groups = new List<int[]>();
        var keyW = widths.Take(Math.Min(KeyColumns, widths.Length)).Sum();
        var current = new List<int>(); float w = 0;
        for (var c = 0; c < widths.Length; c++)
        {
            var isKey = c < KeyColumns;
            if (!isKey && current.Count > 0 && w + widths[c] > usable)
            {
                groups.Add(current.ToArray());
                current = Enumerable.Range(0, Math.Min(KeyColumns, widths.Length)).ToList(); w = keyW;
            }
            current.Add(c); w += widths[c];
        }
        if (current.Count > 0) groups.Add(current.ToArray());
        // A group that only repeats the key columns (can happen when the last data column did not fit) is dropped.
        return groups.Where(g => g.Length > KeyColumns || groups.Count == 1).ToList();
    }

    private static float DrawTitle(SKCanvas canvas, string title, string subtitle, string? columnsNote, float y, SKPaint text, SKPaint bold, SKShaper shaper, SKShaper boldShaper, int page)
    {
        using var titlePaint = new SKPaint { Typeface = Bold.Value, TextSize = TitleFontSize, IsAntialias = true, Color = new SKColor(0x00, 0x45, 0x78) };
        using var subPaint = new SKPaint { Typeface = Regular.Value, TextSize = SubFontSize, IsAntialias = true, Color = new SKColor(0x55, 0x55, 0x55) };
        using var titleShaper = new SKShaper(Bold.Value);
        DrawShaped(canvas, title, Margin, y + TitleFontSize, titlePaint, titleShaper);
        var sub = subtitle + (columnsNote is null ? "" : "  ·  " + columnsNote) + (page > 1 ? $"  ·  continued (page {page})" : "");
        DrawShaped(canvas, sub, Margin, y + TitleFontSize + SubFontSize + 5f, subPaint, shaper);
        using var line = new SKPaint { Color = new SKColor(0x00, 0x45, 0x78), StrokeWidth = 1.2f, IsStroke = true };
        var lineY = y + TitleFontSize + SubFontSize + 11f;
        canvas.DrawLine(Margin, lineY, PageW - Margin, lineY, line);
        return lineY + 6f;
    }

    private static float DrawHeader(SKCanvas canvas, IReadOnlyList<string> headers, int[] cols, float[] widths, float x0, float y, SKPaint bold, SKShaper boldShaper)
    {
        using var fill = new SKPaint { Color = new SKColor(0x00, 0x45, 0x78), Style = SKPaintStyle.Fill };
        using var border = new SKPaint { Color = new SKColor(0xB0, 0xB0, 0xB0), StrokeWidth = 0.5f, IsStroke = true };
        var x = x0;
        foreach (var c in cols)
        {
            var rect = new SKRect(x, y, x + widths[c], y + RowH + 2f);
            canvas.DrawRect(rect, fill); canvas.DrawRect(rect, border);
            DrawClipped(canvas, headers[c], rect, bold, boldShaper, alignRight: false);
            x += widths[c];
        }
        return y + RowH + 2f;
    }

    private static void DrawRow(SKCanvas canvas, XlsxCell[] cells, string[] texts, int[] cols, float[] widths, float x0, float y, SKPaint text, SKPaint bold, SKShaper shaper, SKShaper boldShaper)
    {
        using var border = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD0), StrokeWidth = 0.5f, IsStroke = true };
        var x = x0;
        foreach (var c in cols)
        {
            var rect = new SKRect(x, y, x + widths[c], y + RowH);
            var cell = c < cells.Length ? cells[c] : default;
            var fillColor = cell.Fill switch
            {
                XlsxFill.Pos => new SKColor(0xDC, 0xFC, 0xE7),
                XlsxFill.Neg => new SKColor(0xFE, 0xE2, 0xE2),
                XlsxFill.Gov => new SKColor(0xEE, 0xF2, 0xF7),
                _ => SKColors.Transparent,
            };
            if (fillColor != SKColors.Transparent) { using var f = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill }; canvas.DrawRect(rect, f); }
            canvas.DrawRect(rect, border);
            var isBold = cell.Bold || cell.Fill == XlsxFill.Gov;
            var color = cell.Fill switch { XlsxFill.Pos => new SKColor(0x15, 0x80, 0x3D), XlsxFill.Neg => new SKColor(0xB9, 0x1C, 0x1C), _ => SKColors.Black };
            using var paint = new SKPaint { Typeface = isBold ? Bold.Value : Regular.Value, TextSize = FontSize, IsAntialias = true, Color = color, SubpixelText = true };
            DrawClipped(canvas, c < texts.Length ? texts[c] : "", rect, paint, isBold ? boldShaper : shaper, alignRight: IsNumber(cell.Value));
            x += widths[c];
        }
    }

    private static void DrawFooter(SKCanvas canvas, int page, SKPaint text, SKShaper shaper)
    {
        using var p = new SKPaint { Typeface = Regular.Value, TextSize = 7f, IsAntialias = true, Color = new SKColor(0x88, 0x88, 0x88) };
        DrawShaped(canvas, $"Sent automatically by Follow-Up · page {page}", Margin, PageH - Margin + 10f, p, shaper);
    }

    // ---- text ----

    private static string Display(XlsxCell c) => c.Value switch
    {
        null => "",
        int i => i.ToString("N0", CultureInfo.InvariantCulture),
        long l => l.ToString("N0", CultureInfo.InvariantCulture),
        decimal d => d == Math.Truncate(d) ? d.ToString("N0", CultureInfo.InvariantCulture) : d.ToString("N1", CultureInfo.InvariantCulture),
        double x => x == Math.Truncate(x) ? x.ToString("N0", CultureInfo.InvariantCulture) : x.ToString("N1", CultureInfo.InvariantCulture),
        _ => c.Value.ToString() ?? "",
    };

    private static bool IsNumber(object? v) => v is int or long or decimal or double or float;

    private static float Measure(string s, SKPaint paint, SKShaper shaper)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        return shaper.Shape(s, paint).Width;
    }

    /// <summary>Draws the text inside the cell, right-aligned for numbers, shortened with an ellipsis when it would overflow.</summary>
    private static void DrawClipped(SKCanvas canvas, string s, SKRect rect, SKPaint paint, SKShaper shaper, bool alignRight)
    {
        if (string.IsNullOrEmpty(s)) return;
        var maxW = rect.Width - 2 * CellPadX;
        var w = Measure(s, paint, shaper);
        if (w > maxW)
        {
            // Trim characters until it fits (few cells overflow; MaxColW keeps long names in check).
            var t = s;
            while (t.Length > 1 && Measure(t + "…", paint, shaper) > maxW) t = t[..^1];
            s = t + "…"; w = Measure(s, paint, shaper);
        }
        var x = alignRight ? rect.Right - CellPadX - w : rect.Left + CellPadX;
        var baseline = rect.Top + (rect.Height + paint.TextSize * 0.72f) / 2f;
        DrawShaped(canvas, s, x, baseline, paint, shaper);
    }

    private static void DrawShaped(SKCanvas canvas, string s, float x, float y, SKPaint paint, SKShaper shaper)
    {
        if (string.IsNullOrEmpty(s)) return;
        canvas.DrawShapedText(shaper, s, x, y, paint);
    }
}
