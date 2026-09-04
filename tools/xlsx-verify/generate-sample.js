/**
 * Generates a styled sample .xlsx to verify the hand-rolled workbook writer.
 *
 * The SpreadsheetML + stored-ZIP logic below MIRRORS the pure helpers in
 * web/src/app/shared/export.util.ts (sheetXml / XLSX_STYLES / zipStore / crc32 / styleIndex).
 * Keep the two in sync — if you change the exporter, update this file and re-run:
 *
 *     node tools/xlsx-verify/generate-sample.js        # writes sample.xlsx next to this file
 *     node tools/xlsx-verify/serve.js                  # then open http://localhost:8897 (SheetJS check)
 *     unzip -t tools/xlsx-verify/sample.xlsx           # CRC / archive integrity
 *
 * The sample exercises the colour flags (green pos / red neg), the shaded governorate row, Arabic text,
 * and the #,##0.### number format — the same shapes the Area Statistics export produces.
 */
const fs = require('fs');
const path = require('path');

const NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
const xmlEsc = (v) => String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] || c));
const colName = (i) => { let s = ''; i++; while (i > 0) { const m = (i - 1) % 26; s = String.fromCharCode(65 + m) + s; i = ((i - 1) / 26) | 0; } return s; };
const cellVal = (c) => (c != null && typeof c === 'object' ? c.v : c);
const cellMeta = (c) => (c != null && typeof c === 'object' ? c : {});
function styleIndex(c, header) {
  if (header) return 1;
  const v = cellVal(c), m = cellMeta(c), num = typeof v === 'number';
  if (m.fill === 'gov') return num ? 6 : 5;
  if (m.fill === 'pos') return num ? 3 : 7;
  if (m.fill === 'neg') return num ? 4 : 7;
  if (m.bold) return 7;
  return num ? 2 : 0;
}

const STYLES = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="' + NS + '"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.###"/></numFmts><fonts count="5"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FF15803D"/><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFB91C1C"/><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="6"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF004578"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFDCFCE7"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFFEE2E2"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEEF2F7"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD8DCE0"/></left><right style="thin"><color rgb="FFD8DCE0"/></right><top style="thin"><color rgb="FFD8DCE0"/></top><bottom style="thin"><color rgb="FFD8DCE0"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="8"><xf numFmtId="0" fontId="0" fillId="0" borderId="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf><xf numFmtId="0" fontId="1" fillId="2" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="1" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="164" fontId="2" fillId="3" borderId="1" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="164" fontId="3" fillId="4" borderId="1" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="0" fontId="4" fillId="5" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf><xf numFmtId="164" fontId="4" fillId="5" borderId="1" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf><xf numFmtId="0" fontId="4" fillId="0" borderId="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf></cellXfs></styleSheet>';

function sheetXml(header, rows) {
  const cols = header.length;
  const widths = header.map((h) => String(h ?? '').length);
  for (const r of rows) for (let i = 0; i < cols; i++) { const v = cellVal(r[i]); const l = v == null ? 0 : String(v).length; if (l > widths[i]) widths[i] = l; }
  const colsXml = '<cols>' + widths.map((w, i) => '<col min="' + (i + 1) + '" max="' + (i + 1) + '" width="' + Math.min(Math.max(w + 2, 9), 42) + '" customWidth="1"/>').join('') + '</cols>';
  const cellXml = (c, ref, hdr) => { const v = cellVal(c), s = styleIndex(c, hdr); if (v == null || v === '') return '<c r="' + ref + '" s="' + s + '"/>'; if (typeof v === 'number' && isFinite(v)) return '<c r="' + ref + '" s="' + s + '"><v>' + v + '</v></c>'; return '<c r="' + ref + '" s="' + s + '" t="inlineStr"><is><t xml:space="preserve">' + xmlEsc(v) + '</t></is></c>'; };
  const rowXml = (cells, rowNum, hdr) => '<row r="' + rowNum + '"' + (hdr ? ' ht="20" customHeight="1"' : '') + '>' + cells.map((c, i) => cellXml(c, colName(i) + rowNum, hdr)).join('') + '</row>';
  const body = [rowXml(header, 1, true), ...rows.map((r, i) => rowXml(r, i + 2, false))].join('');
  const dim = 'A1:' + colName(cols - 1) + (rows.length + 1);
  return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="' + NS + '"><dimension ref="' + dim + '"/><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight="15"/>' + colsXml + '<sheetData>' + body + '</sheetData><autoFilter ref="' + dim + '"/></worksheet>';
}

const CRC = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(b) { let c = 0xffffffff; for (let i = 0; i < b.length; i++) c = CRC[(c ^ b[i]) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }
function concat(a) { let n = 0; for (const x of a) n += x.length; const o = new Uint8Array(n); let p = 0; for (const x of a) { o.set(x, p); p += x.length; } return o; }
function zipStore(files) {
  const enc = new TextEncoder(); const chunks = [], central = []; let off = 0;
  const u16 = (n) => new Uint8Array([n & 0xff, (n >>> 8) & 0xff]);
  const u32 = (n) => new Uint8Array([n & 0xff, (n >>> 8) & 0xff, (n >>> 16) & 0xff, (n >>> 24) & 0xff]);
  const push = (a, ...p) => { for (const x of p) a.push(x); };
  for (const f of files) {
    const name = enc.encode(f.name); const crc = crc32(f.data), len = f.data.length; const local = [];
    push(local, u32(0x04034b50), u16(20), u16(0x0800), u16(0), u16(0), u16(0), u32(crc), u32(len), u32(len), u16(name.length), u16(0), name, f.data);
    const lb = concat(local); chunks.push(lb);
    push(central, u32(0x02014b50), u16(20), u16(20), u16(0x0800), u16(0), u16(0), u16(0), u32(crc), u32(len), u32(len), u16(name.length), u16(0), u16(0), u16(0), u16(0), u32(0), u32(off), name);
    off += lb.length;
  }
  const cd = concat(central); const eocd = [];
  push(eocd, u32(0x06054b50), u16(0), u16(0), u16(files.length), u16(files.length), u32(cd.length), u32(off), u16(0));
  return concat([...chunks, cd, concat(eocd)]);
}

function buildXlsx(header, rows) {
  const enc = new TextEncoder();
  const ct = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>';
  const rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>';
  const wb = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="' + NS + '" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Data" sheetId="1" r:id="rId1"/></sheets></workbook>';
  const wbr = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>';
  return zipStore([
    { name: '[Content_Types].xml', data: enc.encode(ct) },
    { name: '_rels/.rels', data: enc.encode(rels) },
    { name: 'xl/workbook.xml', data: enc.encode(wb) },
    { name: 'xl/_rels/workbook.xml.rels', data: enc.encode(wbr) },
    { name: 'xl/styles.xml', data: enc.encode(STYLES) },
    { name: 'xl/worksheets/sheet1.xml', data: enc.encode(sheetXml(header, rows)) },
  ]);
}

// Sample data covering every style path: governorate band, pos/neg flags, Arabic, formatted numbers.
const header = ['Governorate', 'Area', 'Real Name', 'Total', 'Jan 2026'];
const rows = [
  [{ v: 'A 4', fill: 'gov', bold: true }, { v: '', fill: 'gov', bold: true }, { v: 'سوهاج', fill: 'gov', bold: true }, { v: 49647, fill: 'gov', bold: true }, { v: 1654, fill: 'gov', bold: true }],
  ['', 'A 41', 'سوهاج ١', 306, { v: 306, fill: 'pos' }],
  ['', 'A 42', 'سوهاج ٢', 233, { v: 50, fill: 'neg' }],
];

const out = path.join(__dirname, 'sample.xlsx');
fs.writeFileSync(out, Buffer.from(buildXlsx(header, rows)));
console.log('Wrote ' + out + ' (' + fs.statSync(out).size + ' bytes)');
