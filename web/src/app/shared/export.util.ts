/**
 * Client-side export helpers shared by the list/board pages (no external libraries).
 * - exportCsv: downloads a UTF-8-BOM CSV (opens cleanly in Excel, Arabic-safe).
 * - printTable: opens a print-ready table in a new window and triggers the print dialog (save as PDF).
 */

export type Cell = string | number | null | undefined;

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

export function escHtml(v: Cell): string {
  return String(v ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c] ?? c));
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
