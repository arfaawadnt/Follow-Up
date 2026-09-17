import { localToday } from '../../shared/export.util';

/**
 * Shared helpers for the Inventory pages: the fixed value sets (mirroring the domain enumerations, persisted by name),
 * badge colours per status, quantity formatting (up to three decimals, in the item's own unit) and the dialog/grid styles.
 */

export type Opt = { value: string; label: string };

export const ITEM_KINDS = ['Chemical', 'Consumable', 'Kit', 'Control', 'Other'];
export const PO_STATUSES = ['Draft', 'Ordered', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled'];
export const TRANSFER_STATUSES = ['InTransit', 'Received', 'Cancelled'];
export const MOVEMENT_TYPES = ['Receipt', 'Consumption', 'Disposal', 'TransferOut', 'TransferIn', 'Adjustment', 'ReturnToSupplier'];
export const ISSUE_REASONS = ['Consumption', 'Damaged', 'Expired', 'ReturnToSupplier', 'Other'];

/** First day of the current month, yyyy-MM-dd — the default start of the date-range filters. */
export function firstOfMonth(): string { return localToday().slice(0, 7) + '-01'; }

/** "PartiallyReceived" → "Partially Received" (the enumeration names are PascalCase). */
export function label(v: string | null | undefined): string { return (v ?? '').replace(/([a-z])([A-Z])/g, '$1 $2'); }

/** Quantity with up to three decimals and no trailing zeros (12, 12.5, 0.125). */
export function qty(v: number | null | undefined): string {
  if (v === null || v === undefined || isNaN(Number(v))) return '';
  return Number(v).toLocaleString('en-US', { maximumFractionDigits: 3 });
}

/** Two-decimal money for exports and PDF. */
export function money(v: number | null | undefined): number { return Math.round(((v ?? 0) + Number.EPSILON) * 100) / 100; }

/** Badge class per status / alert kind. */
export function badge(v: string | null | undefined): string {
  switch (v) {
    case 'Received': case 'Ok': return 'b-ok';
    case 'Ordered': case 'PartiallyReceived': case 'InTransit': case 'Expiring': case 'LowStock': return 'b-warn';
    case 'Cancelled': case 'Expired': case 'OutOfStock': return 'b-bad';
    default: return 'b-neu';
  }
}

export const INV_STYLES = `
  th.r,td.r{text-align:right}
  td.mono{font-variant-numeric:tabular-nums}
  td.pos{color:#15803d;font-weight:600}
  td.neg{color:#b91c1c;font-weight:600}
  tfoot td{border-top:2px solid var(--slate-150,#edebe9);background:var(--white,#fff);font-weight:800}
  .as-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
  .as-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,720px);max-height:92vh;display:flex;flex-direction:column}
  .as-dlg.wide{width:min(96vw,1040px)}
  .as-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
  .as-dlg-head h2{font-size:15px;margin:0}
  .as-dlg-body{padding:16px;overflow:auto}
  .as-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9);align-items:center}
  .hint{font-size:12px;color:var(--slate-700,#605e5c);margin-top:4px}
  .tabs{display:flex;gap:6px;margin-bottom:14px}
  .tabs button{border:1px solid var(--slate-300,#c8c6c4);background:var(--white,#fff);border-radius:6px;padding:6px 14px;cursor:pointer}
  .tabs button.on{background:var(--primary-blue,#0078D4);color:#fff;border-color:var(--primary-blue,#0078D4)}
  .chip-list{display:flex;flex-wrap:wrap;gap:4px}
  .chip-list span{background:var(--slate-100,#f3f2f1);border-radius:10px;padding:1px 8px;font-size:12px}
  .lines th,.lines td{padding:6px 8px;font-size:13px}
  .lines input.input{padding:4px 8px;height:30px}
  tr.sub td{background:var(--slate-50,#faf9f8);font-size:12px}
  .alert-list{display:flex;flex-direction:column;gap:6px;max-height:280px;overflow:auto}
  .alert-row{display:flex;gap:10px;align-items:flex-start;padding:6px 8px;border-radius:8px;background:var(--slate-50,#faf9f8)}
  .alert-row .badge{flex:none}
  .clickable{cursor:pointer}
  .sec-title{font-size:13px;font-weight:700;margin:14px 0 6px;color:var(--slate-700,#605e5c);text-transform:uppercase;letter-spacing:.03em}
  .kv{display:grid;grid-template-columns:repeat(4,1fr);gap:10px 16px;font-size:13px}
  .kv .k{color:var(--slate-700,#605e5c);font-size:11px;text-transform:uppercase}
  .kv .v{font-weight:600}
`;
