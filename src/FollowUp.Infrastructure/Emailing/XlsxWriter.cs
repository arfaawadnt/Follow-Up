using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FollowUp.Infrastructure.Emailing;

/// <summary>Colour flag for a styled worksheet cell — mirrors the on-screen grids and the browser export util.</summary>
internal enum XlsxFill { None, Pos, Neg, Gov }

/// <summary>
/// A worksheet cell: a value plus an optional colour flag / bold. Strings and the common numeric types convert
/// implicitly, so plain rows read like <c>new XlsxCell[] { "Cairo", 1234, 56.7m }</c> and flagged cells like
/// <c>new XlsxCell(v, XlsxFill.Pos)</c>.
/// </summary>
internal readonly record struct XlsxCell(object? Value, XlsxFill Fill = XlsxFill.None, bool Bold = false)
{
    public static implicit operator XlsxCell(string? v) => new(v);
    public static implicit operator XlsxCell(int v) => new(v);
    public static implicit operator XlsxCell(long v) => new(v);
    public static implicit operator XlsxCell(decimal v) => new(v);
    public static implicit operator XlsxCell(double v) => new(v);
}

/// <summary>
/// Dependency-free <c>.xlsx</c> writer (SpreadsheetML packed into a ZIP via <see cref="ZipArchive"/>), a 1:1 port of
/// the browser export util so emailed attachments match the on-screen exports exactly: banded frozen header, thin
/// borders, auto-filter, thousands-separated numbers, and the green/red/governorate colour flags of the statistics
/// grids. Text cells are inline strings; <c>int</c>/<c>long</c>/<c>decimal</c>/<c>double</c> cells are real numbers.
/// </summary>
internal static class XlsxWriter
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>The MIME content type to use when attaching the produced bytes to an email.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Plain-value convenience overload (no colour flags) — used by the Lab/Test reports.</summary>
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows) =>
        Build(sheetName, headers, rows.Select(r => r.Select(v => new XlsxCell(v)).ToArray()).ToList());

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<XlsxCell[]> rows)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "[Content_Types].xml", ContentTypesXml);
            AddEntry(zip, "_rels/.rels", RootRelsXml);
            AddEntry(zip, "xl/workbook.xml", WorkbookXml(SanitizeSheetName(sheetName)));
            AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            AddEntry(zip, "xl/styles.xml", StylesXml);
            AddEntry(zip, "xl/worksheets/sheet1.xml", SheetXml(headers, rows));
        }
        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    // cellXfs indices baked into StylesXml: 0 text · 1 header · 2 number · 3 pos-number · 4 neg-number · 5 gov-text · 6 gov-number · 7 bold-text
    private static int StyleIndex(XlsxCell c, bool header)
    {
        if (header) return 1;
        var num = IsNumber(c.Value);
        return c.Fill switch
        {
            XlsxFill.Gov => num ? 6 : 5,
            XlsxFill.Pos => num ? 3 : 7,
            XlsxFill.Neg => num ? 4 : 7,
            _ => c.Bold ? 7 : (num ? 2 : 0),
        };
    }

    private static string SheetXml(IReadOnlyList<string> headers, IReadOnlyList<XlsxCell[]> rows)
    {
        var cols = headers.Count;
        var widths = new int[cols];
        for (var i = 0; i < cols; i++) widths[i] = (headers[i] ?? "").Length;
        foreach (var r in rows)
            for (var i = 0; i < cols && i < r.Length; i++)
            {
                var len = Display(r[i].Value)?.Length ?? 0;
                if (len > widths[i]) widths[i] = len;
            }

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<worksheet xmlns=\"{Ns}\">");
        var dim = $"A1:{ColName(cols - 1)}{rows.Count + 1}";
        sb.Append($"<dimension ref=\"{dim}\"/>");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
        sb.Append("<cols>");
        for (var i = 0; i < cols; i++)
        {
            var w = Math.Min(Math.Max(widths[i] + 2, 9), 42);
            sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{w}\" customWidth=\"1\"/>");
        }
        sb.Append("</cols><sheetData>");

        sb.Append("<row r=\"1\" ht=\"20\" customHeight=\"1\">");
        for (var i = 0; i < cols; i++) sb.Append(CellXml(new XlsxCell(headers[i]), ColName(i) + "1", header: true));
        sb.Append("</row>");

        var rowNum = 2;
        foreach (var r in rows)
        {
            sb.Append($"<row r=\"{rowNum}\">");
            for (var i = 0; i < r.Length; i++) sb.Append(CellXml(r[i], ColName(i) + rowNum, header: false));
            sb.Append("</row>");
            rowNum++;
        }

        sb.Append("</sheetData>");
        sb.Append($"<autoFilter ref=\"{dim}\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static string CellXml(XlsxCell c, string reference, bool header)
    {
        var s = StyleIndex(c, header);
        var v = c.Value;
        if (v is null || (v is string es && es.Length == 0)) return $"<c r=\"{reference}\" s=\"{s}\"/>";
        if (IsNumber(v)) return $"<c r=\"{reference}\" s=\"{s}\"><v>{Invariant(v)}</v></c>";
        return $"<c r=\"{reference}\" s=\"{s}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlEscape(v.ToString() ?? "")}</t></is></c>";
    }

    private static bool IsNumber(object? v) => v is int or long or decimal or double or float;

    private static string Invariant(object v) => v switch
    {
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string? Display(object? v) => v is null ? null : IsNumber(v) ? Invariant(v) : v.ToString();

    private static string ColName(int colZeroBased)
    {
        var letters = "";
        var n = colZeroBased;
        do { letters = (char)('A' + (n % 26)) + letters; n = (n / 26) - 1; } while (n >= 0);
        return letters;
    }

    private static string SanitizeSheetName(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(ch is '\\' or '/' or '?' or '*' or '[' or ']' or ':' ? ' ' : ch);
        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0) cleaned = "Sheet1";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&apos;");

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private const string RootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string WorkbookRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private static string WorkbookXml(string sheetName) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"" + Ns + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets><sheet name=\"" + XmlEscape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    // Ported verbatim from the browser export util (web/src/app/shared/export.util.ts) so colours/fonts match.
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"" + Ns + "\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.###\"/></numFmts>" +
        "<fonts count=\"5\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><color rgb=\"FF15803D\"/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><color rgb=\"FFB91C1C\"/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "</fonts>" +
        "<fills count=\"6\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF004578\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCFCE7\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFEE2E2\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEEF2F7\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"2\">" +
        "<border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"><color rgb=\"FFD8DCE0\"/></left><right style=\"thin\"><color rgb=\"FFD8DCE0\"/></right><top style=\"thin\"><color rgb=\"FFD8DCE0\"/></top><bottom style=\"thin\"><color rgb=\"FFD8DCE0\"/></bottom><diagonal/></border>" +
        "</borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"8\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"2\" fillId=\"3\" borderId=\"1\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"3\" fillId=\"4\" borderId=\"1\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"5\" borderId=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"4\" fillId=\"5\" borderId=\"1\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"1\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
