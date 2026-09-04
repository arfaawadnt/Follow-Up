# xlsx export verification

Harness for the dependency-free `.xlsx` writer in
[`web/src/app/shared/export.util.ts`](../../web/src/app/shared/export.util.ts) (`exportXlsx`).

The app builds workbooks by hand (stored-mode ZIP + minimal SpreadsheetML) so exports stay
library-free under the app's `script-src 'self'` CSP. Because a single malformed byte makes Excel
reject the file, this harness proves the output is a well-formed, openable workbook.

`generate-sample.js` **mirrors** the pure helpers of `export.util.ts` (`sheetXml`, `XLSX_STYLES`,
`zipStore`, `crc32`, `styleIndex`). If you change the exporter, update this file too.

## Usage

```bash
# 1. Generate a sample workbook (governorate band, green/red flags, Arabic, formatted numbers)
node tools/xlsx-verify/generate-sample.js        # -> tools/xlsx-verify/sample.xlsx

# 2a. Archive/CRC integrity
unzip -t tools/xlsx-verify/sample.xlsx           # expect "No errors detected"

# 2b. Open it with a real spreadsheet parser (SheetJS)
node tools/xlsx-verify/serve.js                  # then open http://localhost:8897
#     viewer.html reports "✓ OPENED OK" and renders the sheet, or the parse error.

# 2c. Or just open sample.xlsx in Excel / LibreOffice / Google Sheets.
```

`sample.xlsx` is a generated artifact and is not committed — run `generate-sample.js` to (re)create it.
