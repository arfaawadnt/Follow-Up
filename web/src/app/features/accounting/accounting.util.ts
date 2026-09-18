import { localToday } from '../../shared/export.util';

/**
 * Shared helpers for the Accounting pages. "Day" is never stored — it is derived from the row's date here, in the
 * user's language, so every grid and export shows it consistently.
 */

/** Localized weekday name for a yyyy-MM-dd string (parsed as local calendar date to avoid timezone shifts). */
export function dayName(iso: string | null | undefined, lang: string): string {
  if (!iso) return '';
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  if (!y || !m || !d) return '';
  return new Intl.DateTimeFormat(lang === 'ar' ? 'ar-EG' : 'en-GB', { weekday: 'long' }).format(new Date(y, m - 1, d));
}

/** First day of the current month, yyyy-MM-dd — the default start of every Accounting date-range filter. */
export function firstOfMonth(): string { return localToday().slice(0, 7) + '-01'; }

/** Two-decimal money for exports and PDF (the grids use the DecimalPipe). */
export function money(v: number | null | undefined): number { return Math.round(((v ?? 0) + Number.EPSILON) * 100) / 100; }

/** Fixed value sets, mirroring the domain enumerations (persisted by name). */
export const IBAN_OPTIONS = ['12', '16', '18'];
export const PENALTY_USERS = ['Rep', 'DataEntry', 'Technician', 'LabRequest'];
/** The user types whose penalties are our side's fault (kept for the report's group order; all four types are reported). */
export const PENALTY_STAFF_USERS = ['Rep', 'DataEntry', 'Technician'];
/** LDM validation of a penalty's Acc No against the synced registration lines (server-computed). */
export function ldmStatusLabel(s: string): string {
  return s === 'Valid' ? 'Valid in LDM' : s === 'AccNotFound' ? 'Acc No not in LDM' : s === 'LabMismatch' ? 'Other lab in LDM' : s === 'TestMissing' ? 'Test not on registration' : s;
}
export function ldmStatusClass(s: string): string { return s === 'Valid' ? 'b-ok' : s === 'Unknown' ? 'b-neu' : 'b-bad'; }
export function penaltyUserLabel(u: string): string { return u === 'DataEntry' ? 'Data Entry' : u === 'LabRequest' ? 'Lab Request' : u; }
/** Penalties left the deductions business on 2026-09-18 (they post to the rep statement). */
export const DEDUCTION_REASONS = ['Transportation', 'PercentageDeal'];
export const COLLECTION_TYPES = ['Single', 'Group'];

/** Shared dialog + grid styles (same overlay/dialog idiom as the statistics pages). */
export const ACC_STYLES = `
  th.r,td.r{text-align:right}
  td.mono{font-variant-numeric:tabular-nums}
  td.pos{color:#15803d;font-weight:600}
  td.neg{color:#b91c1c;font-weight:600}
  tfoot td{border-top:2px solid var(--slate-150,#edebe9);background:var(--white,#fff);font-weight:800}
  .as-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
  .as-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,640px);max-height:92vh;display:flex;flex-direction:column}
  .as-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
  .as-dlg-head h2{font-size:15px;margin:0}
  .as-dlg-body{padding:16px;overflow:auto}
  .as-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
  .basis{font-size:12px;color:var(--slate-700,#605e5c);margin-top:4px}
  .tabs{display:flex;gap:6px;margin-bottom:14px}
  .tabs button{border:1px solid var(--slate-300,#c8c6c4);background:var(--white,#fff);border-radius:6px;padding:6px 14px;cursor:pointer}
  .tabs button.on{background:var(--primary-blue,#0078D4);color:#fff;border-color:var(--primary-blue,#0078D4)}
  .chip-list{display:flex;flex-wrap:wrap;gap:4px}
  .chip-list span{background:var(--slate-100,#f3f2f1);border-radius:10px;padding:1px 8px;font-size:12px}
`;
