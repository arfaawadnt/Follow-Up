using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FollowUp.Infrastructure.Emailing;

/// <summary>
/// Dependency-free <c>.xlsx</c> writer (SpreadsheetML packed into a ZIP via <see cref="ZipArchive"/>), mirroring the
/// browser export util so emailed attachments match the on-screen exports. Text cells are written as inline strings
/// (no shared-strings table); <c>int</c>/<c>long</c>/<c>decimal</c>/<c>double</c> cells are written as real numbers so
/// Excel can sum and sort them. The header row is styled bold white on the app's blue (#004578).
/// </summary>
internal static class XlsxWriter
{
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>The MIME content type to use when attaching the produced bytes to an email.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
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

    private static string SheetXml(IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append($"<worksheet xmlns=\"{SheetNs}\"><sheetData>");

        var rowIdx = 1;
        sb.Append($"<row r=\"{rowIdx}\">");
        for (var c = 0; c < headers.Count; c++)
            sb.Append(TextCell(ColRef(c, rowIdx), headers[c], styleId: 1));
        sb.Append("</row>");
        rowIdx++;

        foreach (var row in rows)
        {
            sb.Append($"<row r=\"{rowIdx}\">");
            for (var c = 0; c < row.Length; c++)
            {
                var reference = ColRef(c, rowIdx);
                var cell = row[c];
                if (cell is null) sb.Append($"<c r=\"{reference}\"/>");
                else if (TryNumber(cell, out var num)) sb.Append($"<c r=\"{reference}\" s=\"2\"><v>{num}</v></c>");
                else sb.Append(TextCell(reference, cell.ToString() ?? "", styleId: 0));
            }
            sb.Append("</row>");
            rowIdx++;
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static string TextCell(string reference, string text, int styleId) =>
        $"<c r=\"{reference}\" s=\"{styleId}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XmlEscape(text)}</t></is></c>";

    private static bool TryNumber(object value, out string invariant)
    {
        switch (value)
        {
            case int i: invariant = i.ToString(CultureInfo.InvariantCulture); return true;
            case long l: invariant = l.ToString(CultureInfo.InvariantCulture); return true;
            case decimal m: invariant = m.ToString(CultureInfo.InvariantCulture); return true;
            case double d: invariant = d.ToString(CultureInfo.InvariantCulture); return true;
            case float ff: invariant = ff.ToString(CultureInfo.InvariantCulture); return true;
            default: invariant = ""; return false;
        }
    }

    private static string ColRef(int colZeroBased, int row)
    {
        var letters = "";
        var n = colZeroBased;
        do { letters = (char)('A' + (n % 26)) + letters; n = (n / 26) - 1; } while (n >= 0);
        return letters + row.ToString(CultureInfo.InvariantCulture);
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
        "<workbook xmlns=\"" + SheetNs + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
        "<sheets><sheet name=\"" + XmlEscape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"" + SheetNs + "\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.###\"/></numFmts>" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF004578\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"3\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
