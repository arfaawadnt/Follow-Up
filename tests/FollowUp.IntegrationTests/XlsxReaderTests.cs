using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FollowUp.Infrastructure.Gateways;

namespace FollowUp.IntegrationTests;

public sealed class XlsxReaderTests
{
    // Builds a minimal .xlsx (shared strings + one worksheet) the reader can parse. Header row + one data row.
    private static byte[] BuildWorkbook()
    {
        const string sharedStrings = """
        <sst><si><t>Date</t></si><si><t>LabCode</t></si><si><t>Registrations</t></si><si><t>2026-08-01</t></si><si><t>MGL-0001</t></si></sst>
        """;
        const string sheet = """
        <worksheet><sheetData>
        <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
        <row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2" t="s"><v>4</v></c><c r="C2"><v>42</v></c></row>
        </sheetData></worksheet>
        """;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "xl/sharedStrings.xml", sharedStrings);
            Write(zip, "xl/worksheets/sheet1.xml", sheet);
        }
        return ms.ToArray();

        static void Write(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path);
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
    }

    [Fact]
    public void Reads_header_mapped_rows_including_shared_strings_and_inline_numbers()
    {
        var reader = new XlsxSpreadsheetReader();

        var rows = reader.ReadRows(BuildWorkbook());

        rows.Should().ContainSingle();
        var row = rows[0];
        row["Date"].Should().Be("2026-08-01");
        row["LabCode"].Should().Be("MGL-0001");
        row["Registrations"].Should().Be("42");
    }
}
