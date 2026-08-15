using System.IO.Compression;
using System.Xml.Linq;
using FollowUp.Application.Common.Abstractions;

namespace FollowUp.Infrastructure.Gateways;

/// <summary>
/// A minimal, dependency-free reader for <c>.xlsx</c> workbooks (SRS: hand-written spreadsheet reader, no
/// external library). Reads the first worksheet, treats row 1 as headers, and returns each subsequent row as
/// a header→cell-text map. Shared strings and sparse cells (by column letter) are resolved.
/// </summary>
public sealed class XlsxSpreadsheetReader : ISpreadsheetReader
{
    public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var sharedStrings = ReadSharedStrings(zip);
        var sheet = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                                                    && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The workbook contains no worksheet.");

        using var sheetStream = sheet.Open();
        var doc = XDocument.Load(sheetStream);

        var rows = doc.Root!.Descendants().Where(e => e.Name.LocalName == "row").ToList();
        if (rows.Count == 0) return Array.Empty<IReadOnlyDictionary<string, string>>();

        var headers = ParseRow(rows[0], sharedStrings); // column index -> header text
        var result = new List<IReadOnlyDictionary<string, string>>();

        foreach (var row in rows.Skip(1))
        {
            var cells = ParseRow(row, sharedStrings);
            if (cells.Count == 0) continue;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, header) in headers)
                map[header] = cells.TryGetValue(col, out var value) ? value : string.Empty;
            result.Add(map);
        }
        return result;
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        return doc.Root!.Elements().Where(e => e.Name.LocalName == "si")
            .Select(si => string.Concat(si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)))
            .ToList();
    }

    private static Dictionary<int, string> ParseRow(XElement row, List<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        foreach (var c in row.Elements().Where(e => e.Name.LocalName == "c"))
        {
            var reference = c.Attribute("r")?.Value ?? string.Empty;
            var col = ColumnIndex(reference);
            var type = c.Attribute("t")?.Value;
            var valueElement = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v");
            string text;
            if (type == "s" && valueElement is not null && int.TryParse(valueElement.Value, out var idx) && idx < sharedStrings.Count)
                text = sharedStrings[idx];
            else if (type == "inlineStr")
                text = string.Concat(c.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value));
            else
                text = valueElement?.Value ?? string.Empty;

            if (col >= 0) cells[col] = text;
        }
        return cells;
    }

    /// <summary>Converts a cell reference like "B3" to a zero-based column index (B → 1).</summary>
    private static int ColumnIndex(string cellRef)
    {
        var letters = new string(cellRef.TakeWhile(char.IsLetter).ToArray());
        if (letters.Length == 0) return -1;
        var index = 0;
        foreach (var ch in letters.ToUpperInvariant())
            index = index * 26 + (ch - 'A' + 1);
        return index - 1;
    }
}
