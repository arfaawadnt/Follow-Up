/**
 * Client-side export helpers shared by the list/board pages (no external libraries).
 * - exportXlsx: downloads a styled Office Open XML (.xlsx) workbook — banded header, thin borders,
 *   frozen header row, auto-filter, thousands-separated numbers, and optional per-cell colour flags
 *   (pos/neg/gov) matching the on-screen grids. Hand-rolled (stored-mode ZIP + minimal SpreadsheetML)
 *   so it stays dependency-free and Arabic-safe.
 * - exportCsv: legacy UTF-8-BOM CSV (kept for callers that still want plain CSV).
 * - printTable / printDoc: print-ready HTML in a new window (save as PDF).
 */

export type Cell = string | number | null | undefined;
/** A worksheet cell: a bare value, or a value with a colour flag / bold for styled exports. */
export type SheetCell = Cell | { v: Cell; fill?: 'pos' | 'neg' | 'gov'; bold?: boolean };

export function exportCsv(filename: string, header: string[], rows: Cell[][]): void {
  const esc = (v: Cell) => {
    const s = v == null ? '' : String(v);
    return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
  };
  const lines = [header.join(','), ...rows.map((r) => r.map(esc).join(','))];
  const blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = filename; a.click();
  URL.revokeObjectURL(url);
}

// ---- .xlsx export (styled, colour-aware) ---------------------------------------------------------

const XLSX_NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
function xmlEsc(v: Cell): string {
  return String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] ?? c));
}
function colName(i: number): string { let s = ''; i++; while (i > 0) { const m = (i - 1) % 26; s = String.fromCharCode(65 + m) + s; i = ((i - 1) / 26) | 0; } return s; }
function cellVal(c: SheetCell): Cell { return c != null && typeof c === 'object' ? c.v : c; }
function cellMeta(c: SheetCell): { fill?: string; bold?: boolean } { return c != null && typeof c === 'object' ? c : {}; }

/**
 * cellXfs style indices baked into the styles part below:
 * 0 text · 1 header · 2 number · 3 pos-number · 4 neg-number · 5 gov-text · 6 gov-number · 7 bold-text
 */
function styleIndex(c: SheetCell, header: boolean): number {
  if (header) return 1;
  const v = cellVal(c); const m = cellMeta(c); const num = typeof v === 'number';
  if (m.fill === 'gov') return num ? 6 : 5;
  if (m.fill === 'pos') return num ? 3 : 7;
  if (m.fill === 'neg') return num ? 4 : 7;
  if (m.bold) return 7;
  return num ? 2 : 0;
}

const XLSX_STYLES =
  `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
  `<styleSheet xmlns="${XLSX_NS}">` +
  `<numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.###"/></numFmts>` +
  `<fonts count="5">` +
  `<font><sz val="11"/><name val="Calibri"/></font>` +
  `<font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font>` +
  `<font><b/><color rgb="FF15803D"/><sz val="11"/><name val="Calibri"/></font>` +
  `<font><b/><color rgb="FFB91C1C"/><sz val="11"/><name val="Calibri"/></font>` +
  `<font><b/><sz val="11"/><name val="Calibri"/></font>` +
  `</fonts>` +
  `<fills count="6">` +
  `<fill><patternFill patternType="none"/></fill>` +
  `<fill><patternFill patternType="gray125"/></fill>` +
  `<fill><patternFill patternType="solid"><fgColor rgb="FF004578"/></patternFill></fill>` +
  `<fill><patternFill patternType="solid"><fgColor rgb="FFDCFCE7"/></patternFill></fill>` +
  `<fill><patternFill patternType="solid"><fgColor rgb="FFFEE2E2"/></patternFill></fill>` +
  `<fill><patternFill patternType="solid"><fgColor rgb="FFEEF2F7"/></patternFill></fill>` +
  `</fills>` +
  `<borders count="2">` +
  `<border><left/><right/><top/><bottom/><diagonal/></border>` +
  `<border><left style="thin"><color rgb="FFD8DCE0"/></left><right style="thin"><color rgb="FFD8DCE0"/></right><top style="thin"><color rgb="FFD8DCE0"/></top><bottom style="thin"><color rgb="FFD8DCE0"/></bottom><diagonal/></border>` +
  `</borders>` +
  `<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>` +
  `<cellXfs count="8">` +
  `<xf numFmtId="0" fontId="0" fillId="0" borderId="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>` +
  `<xf numFmtId="0" fontId="1" fillId="2" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>` +
  `<xf numFmtId="164" fontId="0" fillId="0" borderId="1" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>` +
  `<xf numFmtId="164" fontId="2" fillId="3" borderId="1" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>` +
  `<xf numFmtId="164" fontId="3" fillId="4" borderId="1" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>` +
  `<xf numFmtId="0" fontId="4" fillId="5" borderId="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>` +
  `<xf numFmtId="164" fontId="4" fillId="5" borderId="1" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>` +
  `<xf numFmtId="0" fontId="4" fillId="0" borderId="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>` +
  `</cellXfs>` +
  `</styleSheet>`;

function sheetXml(header: string[], rows: SheetCell[][]): string {
  const cols = header.length;
  const widths: number[] = header.map((h) => String(h ?? '').length);
  for (const r of rows) for (let i = 0; i < cols; i++) { const v = cellVal(r[i]); const l = v == null ? 0 : String(v).length; if (l > widths[i]) widths[i] = l; }
  const colsXml = '<cols>' + widths.map((w, i) => `<col min="${i + 1}" max="${i + 1}" width="${Math.min(Math.max(w + 2, 9), 42)}" customWidth="1"/>`).join('') + '</cols>';

  const cellXml = (c: SheetCell, ref: string, header: boolean) => {
    const v = cellVal(c); const s = styleIndex(c, header);
    if (v == null || v === '') return `<c r="${ref}" s="${s}"/>`;
    if (typeof v === 'number' && isFinite(v)) return `<c r="${ref}" s="${s}"><v>${v}</v></c>`;
    return `<c r="${ref}" s="${s}" t="inlineStr"><is><t xml:space="preserve">${xmlEsc(v)}</t></is></c>`;
  };
  const rowXml = (cells: SheetCell[], rowNum: number, header: boolean) =>
    `<row r="${rowNum}"${header ? ' ht="20" customHeight="1"' : ''}>` +
    cells.map((c, i) => cellXml(c, colName(i) + rowNum, header)).join('') + '</row>';

  const body = [rowXml(header, 1, true), ...rows.map((r, i) => rowXml(r, i + 2, false))].join('');
  const dim = `A1:${colName(cols - 1)}${rows.length + 1}`;
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>` +
    `<worksheet xmlns="${XLSX_NS}"><dimension ref="${dim}"/>` +
    `<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>` +
    `<sheetFormatPr defaultRowHeight="15"/>` + colsXml +
    `<sheetData>${body}</sheetData>` +
    `<autoFilter ref="${dim}"/></worksheet>`;
}

const CRC_TABLE = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(buf: Uint8Array): number { let c = 0xffffffff; for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; }

/** Builds a stored-mode (uncompressed) ZIP — enough for a valid .xlsx and keeps the writer tiny. */
function zipStore(files: { name: string; data: Uint8Array }[]): Uint8Array {
  const enc = new TextEncoder();
  const chunks: Uint8Array[] = [];
  const central: Uint8Array[] = [];
  let offset = 0;
  const u16 = (n: number) => new Uint8Array([n & 0xff, (n >>> 8) & 0xff]);
  const u32 = (n: number) => new Uint8Array([n & 0xff, (n >>> 8) & 0xff, (n >>> 16) & 0xff, (n >>> 24) & 0xff]);
  const push = (arr: Uint8Array[], ...parts: Uint8Array[]) => { for (const p of parts) arr.push(p); };

  for (const f of files) {
    const name = enc.encode(f.name);
    const crc = crc32(f.data); const len = f.data.length;
    const local: Uint8Array[] = [];
    push(local, u32(0x04034b50), u16(20), u16(0x0800), u16(0), u16(0), u16(0), u32(crc), u32(len), u32(len), u16(name.length), u16(0), name, f.data);
    const localBytes = concat(local);
    chunks.push(localBytes);
    push(central, u32(0x02014b50), u16(20), u16(20), u16(0x0800), u16(0), u16(0), u16(0), u32(crc), u32(len), u32(len), u16(name.length), u16(0), u16(0), u16(0), u16(0), u32(0), u32(offset), name);
    offset += localBytes.length;
  }
  const cd = concat(central);
  const eocd: Uint8Array[] = [];
  push(eocd, u32(0x06054b50), u16(0), u16(0), u16(files.length), u16(files.length), u32(cd.length), u32(offset), u16(0));
  return concat([...chunks, cd, concat(eocd)]);
}
function concat(arrs: Uint8Array[]): Uint8Array {
  let n = 0; for (const a of arrs) n += a.length;
  const out = new Uint8Array(n); let o = 0; for (const a of arrs) { out.set(a, o); o += a.length; }
  return out;
}

/**
 * Downloads a styled .xlsx workbook. `header` becomes the banded, frozen, auto-filtered top row;
 * `rows` are bare values (numbers auto-formatted, text left-aligned) or `{v, fill, bold}` for the
 * green/red/gov colour flags used by the statistics grids.
 */
export function exportXlsx(filename: string, header: string[], rows: SheetCell[][], sheetName = 'Data'): void {
  const enc = new TextEncoder();
  const safeSheet = xmlEsc(sheetName).slice(0, 31) || 'Data';
  const files = [
    { name: '[Content_Types].xml', data: enc.encode(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>`) },
    { name: '_rels/.rels', data: enc.encode(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>`) },
    { name: 'xl/workbook.xml', data: enc.encode(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="${XLSX_NS}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="${safeSheet}" sheetId="1" r:id="rId1"/></sheets></workbook>`) },
    { name: 'xl/_rels/workbook.xml.rels', data: enc.encode(`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>`) },
    { name: 'xl/styles.xml', data: enc.encode(XLSX_STYLES) },
    { name: 'xl/worksheets/sheet1.xml', data: enc.encode(sheetXml(header, rows)) },
  ];
  const blob = new Blob([zipStore(files)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = filename.endsWith('.xlsx') ? filename : filename.replace(/\.csv$/i, '') + '.xlsx'; a.click();
  URL.revokeObjectURL(url);
}

export function escHtml(v: Cell): string {
  return String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] ?? c));
}

/**
 * Prints a caller-built HTML table so cell styling (e.g. the Area Statistics green/red flags) is preserved
 * in the PDF. `print-color-adjust: exact` forces the background fills to render when saved/printed.
 */
export function printDoc(title: string, tableHtml: string): void {
  const html = `<!doctype html><html><head><title>${escHtml(title)}</title><style>
    body{font:12px system-ui,sans-serif;padding:16px}h1{font-size:15px}
    table{border-collapse:collapse;width:100%}
    th,td{border:1px solid #ccc;padding:5px 7px;text-align:left;white-space:nowrap}
    th{background:#004578;color:#fff;-webkit-print-color-adjust:exact;print-color-adjust:exact}
    td.r{text-align:right}
    tr.gov td{background:#eef2f7;font-weight:700;-webkit-print-color-adjust:exact;print-color-adjust:exact}
    td.pos{background:#dcfce7;color:#15803d;font-weight:700;-webkit-print-color-adjust:exact;print-color-adjust:exact}
    td.neg{background:#fee2e2;color:#b91c1c;font-weight:700;-webkit-print-color-adjust:exact;print-color-adjust:exact}
    .rn{color:#0078D4;margin-inline-start:6px}
  </style></head><body><h1>${escHtml(title)}</h1>${tableHtml}</body></html>`;
  const w = window.open('', '_blank');
  if (!w) return;
  w.document.write(html); w.document.close(); w.focus(); w.print();
}

export function printTable(title: string, header: string[], rows: Cell[][]): void {
  const head = header.map((h) => `<th>${escHtml(h)}</th>`).join('');
  const body = rows.map((r) => `<tr>${r.map((c) => `<td>${escHtml(c)}</td>`).join('')}</tr>`).join('');
  const html = `<!doctype html><html><head><title>${escHtml(title)}</title><style>
    body{font:12px system-ui,sans-serif;padding:16px}h1{font-size:16px}
    table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left}
    th{background:#f1f5f9}</style></head><body>
    <h1>${escHtml(title)} (${rows.length})</h1>
    <table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></body></html>`;
  const w = window.open('', '_blank');
  if (!w) return;
  w.document.write(html); w.document.close(); w.focus(); w.print();
}

/** Today's date (yyyy-MM-dd) in the machine's local timezone — never the UTC date, which is
 *  yesterday between midnight and 03:00 in Cairo and made "today" pages render empty. */
export function localToday(): string {
  const d = new Date();
  d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
  return d.toISOString().slice(0, 10);
}

/** Formats an ISO timestamp as local HH:mm (reference shows stage times in local time). */
export function localTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return isNaN(d.getTime()) ? '—' : d.toTimeString().slice(0, 5);
}

/**
 * Formats a date value as dd/MM/yyyy (or dd/MM/yyyy HH:mm with `withTime`).
 * A plain yyyy-MM-dd string (the API's DateOnly wire format) is rearranged
 * textually — never parsed as UTC — so the day never shifts across timezones.
 * ISO timestamps are rendered in the browser's local time.
 */
export function ddmy(value: string | Date | null | undefined, withTime = false): string {
  if (value == null || value === '') return '—';
  if (typeof value === 'string' && !withTime) {
    const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value.trim());
    if (m) return `${m[3]}/${m[2]}/${m[1]}`;
  }
  const d = value instanceof Date ? value : new Date(value);
  if (isNaN(d.getTime())) return typeof value === 'string' ? value : '—';
  const date = `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
  if (!withTime) return date;
  return `${date} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

/** Formats an ISO timestamp as local dd/MM/yyyy HH:mm. */
export function localDateTime(iso: string | null | undefined): string {
  return ddmy(iso, true);
}
