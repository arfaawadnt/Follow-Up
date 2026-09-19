import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { UiService } from '../../core/ui.service';
import { TranslatePipe } from '../../core/i18n';
import { LabLookup, PagedResult, RepListItem, RepStatementRow, Statement } from '../../core/models';
import { ACC_STYLES, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };
type ViewBy = 'Responsible' | 'Area' | 'Lab';
type ViewAs = 'Daily' | 'Weekly' | 'Monthly' | 'Yearly';
interface AreaOpt { id: string; name: string; }
/** One grid line: a statement row as-is (Daily) or one period's totals (Weekly / Monthly / Yearly). */
interface ViewRow { label: string; day: string; kind: string; debit: number; credit: number; notes: string | null; balance: number; source: RepStatementRow | null; }

/**
 * Rep Statement — a ledger over a date range drawn for one subject, chosen with "View by": a Lab Responsible (the classic
 * rep statement), an Area (all its labs) or a single Lab. Debit = the synced income of the subject's labs (one line per
 * day) plus the real income recorded on the Rep Income page (Σ paid + delayed payment per day; legacy manual lines stay
 * visible and deletable on the Responsible view); Credit = the rep's share of collections (Responsible view only);
 * Balance runs Debit − Credit.
 */
@Component({
  selector: 'app-acc-rep-statement',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent, RouterLink],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_rep_statement' | t : 'Rep Statement' }}</div><h1>{{ 'acc_rep_statement' | t : 'Rep Statement' }}</h1></div>
      <div class="pagehead-actions">
        <a class="btn btn-s" routerLink="/accounting/rep-income">{{ 'acc_rep_income' | t : 'Rep Income' }}</a>
        <button class="btn btn-s" [disabled]="!st()" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" [disabled]="!st()" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_debit' | t : 'Total debit' }}</div><div class="val">{{ (st()?.totalDebit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'statement_debit_hint' | t : 'Total required (Rep Income) + penalties: right tests' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'total_credit' | t : 'Total credit' }}</div><div class="val">{{ (st()?.totalCredit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'statement_credit_hint' | t : 'Collections + deductions + penalties: wrong tests' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'balance' | t : 'Balance' }}</div><div class="val">{{ (st()?.balance ?? 0) | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ st()?.rows?.length ?? 0 }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:1fr 2fr 1fr 1fr 1fr 1fr;gap:12px;align-items:end">
        <div class="field"><label>{{ 'view_by' | t : 'View by' }}</label>
          <select class="select" [ngModel]="by" (ngModelChange)="setBy($event)">
            <option value="Responsible">{{ 'lab_responsible' | t : 'Lab Responsible' }}</option>
            <option value="Area">{{ 'area_2' | t : 'Area' }}</option>
            <option value="Lab">{{ 'lab' | t : 'Lab' }}</option>
          </select></div>
        <div class="field"><label>{{ subjectLabel() }} *</label><app-filter-select [(ngModel)]="subjectId" [options]="subjectOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'view_as' | t : 'View as' }}</label>
          <select class="select" [ngModel]="viewAs" (ngModelChange)="setViewAs($event)">
            <option value="Daily">{{ 'view_daily' | t : 'Daily' }}</option>
            <option value="Weekly">{{ 'view_weekly' | t : 'Weekly' }}</option>
            <option value="Monthly">{{ 'view_monthly' | t : 'Monthly' }}</option>
            <option value="Yearly">{{ 'view_yearly' | t : 'Yearly' }}</option>
          </select></div>
        <div class="field"><button class="btn btn-p" [disabled]="!subjectId" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      <div class="small muted" style="margin-top:8px">{{ 'statement_by_hint' | t : 'Debit = total required entered on Rep Income + the right test of each penalty. Credit = the actual collections (Lab Responsible view), the area deductions (Area and Lab Responsible views) and the wrong test of each penalty, each noted with its record.' }}</div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (!subjectId) { <div class="empty" style="padding:24px;text-align:center">{{ 'select_rep_first' | t : 'Select a subject to view the statement.' }}</div> }
      @else if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="padding:0 16px 8px;font-weight:700">{{ st()?.subjectName }} <span class="small muted">· {{ viewAsSig() === 'Daily' ? ('view_daily' | t : 'Daily') : viewAsSig() === 'Weekly' ? ('view_weekly' | t : 'Weekly') : viewAsSig() === 'Monthly' ? ('view_monthly' | t : 'Monthly') : ('view_yearly' | t : 'Yearly') }} · {{ view().length }} {{ 'entries' | t : 'Entries' }}</span></div>
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ viewAsSig() === 'Daily' ? ('date' | t : 'Date') : ('period' | t : 'Period') }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'kind' | t : 'Kind' }}</th>
            <th class="r">{{ 'debit' | t : 'Debit' }}</th><th class="r">{{ 'credit_out' | t : 'Credit' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th class="r">{{ 'balance' | t : 'Balance' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (r of view(); track $index) {
              <tr>
                <td>{{ r.label }}</td><td>{{ r.day }}</td><td>{{ r.kind }}</td>
                <td class="r mono pos">{{ r.debit ? (r.debit | number:'1.2-2') : '' }}</td><td class="r mono neg">{{ r.credit ? (r.credit | number:'1.2-2') : '' }}</td>
                <td>{{ r.notes || '—' }}</td><td class="r mono" style="font-weight:700">{{ r.balance | number:'1.2-2' }}</td>
                @if (canManage()) { <td class="ar actions">@if (r.source && r.source.kind === 'ManualIncome' && r.source.sourceId) { <button class="icon-btn del" title="Delete" (click)="remove(r.source)">🗑</button> }</td> }
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (st()?.rows?.length) {
            <tfoot><tr><td colspan="3">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ st()!.totalDebit | number:'1.2-2' }}</td><td class="r mono">{{ st()!.totalCredit | number:'1.2-2' }}</td><td></td><td class="r mono">{{ st()!.balance | number:'1.2-2' }}</td>@if (canManage()) { <td></td> }</tr></tfoot>
          }
        </table></div>
      }
    </div>
  `,
  styles: [ACC_STYLES],
})
export class RepStatementComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;

  readonly loading = signal(false);
  readonly st = signal<Statement | null>(null);
  readonly reps = signal<RepListItem[]>([]);
  readonly areas = signal<AreaOpt[]>([]);
  readonly labs = signal<LabLookup[]>([]);
  by: ViewBy = 'Responsible'; subjectId = ''; from = firstOfMonth(); to = localToday(); viewAs: ViewAs = 'Daily';
  readonly bySig = signal<ViewBy>('Responsible');
  readonly viewAsSig = signal<ViewAs>('Daily');
  /** The grid lines: every statement row (Daily) or one line per week / month / year with the period's debit, credit and the
   *  running balance after it; the kind column then lists how many rows of each kind the period holds. */
  readonly view = computed<ViewRow[]>(() => {
    const rows = this.st()?.rows ?? []; const as = this.viewAsSig();
    if (as === 'Daily') return rows.map((r) => ({ label: ddmy(r.date), day: this.day(r.date), kind: this.kindLabel(r.kind), debit: r.debit, credit: r.credit, notes: r.notes, balance: r.balance, source: r }));
    const groups = new Map<string, { label: string; rows: RepStatementRow[] }>();
    for (const r of rows) {
      const p = RepStatementComponent.period(r.date, as);
      const g = groups.get(p.key) ?? { label: p.label, rows: [] }; g.rows.push(r); groups.set(p.key, g);
    }
    let balance = 0;
    return [...groups.entries()].sort((a, b) => a[0].localeCompare(b[0])).map(([, g]) => {
      const debit = money(g.rows.reduce((s, r) => s + r.debit, 0)); const credit = money(g.rows.reduce((s, r) => s + r.credit, 0));
      balance = money(balance + debit - credit);
      const kinds = new Map<string, number>(); for (const r of g.rows) kinds.set(r.kind, (kinds.get(r.kind) ?? 0) + 1);
      return { label: g.label, day: '', kind: [...kinds.entries()].map(([k, n]) => `${this.kindLabel(k)} ×${n}`).join(' · '), debit, credit,
        notes: `${g.rows.length} entries · ${ddmy(g.rows[0].date)} → ${ddmy(g.rows[g.rows.length - 1].date)}`, balance, source: null };
    });
  });
  /** Period key (sortable) and label for a yyyy-MM-dd date. Weeks start on Saturday (the Egyptian work week). */
  private static period(iso: string, as: ViewAs): { key: string; label: string } {
    const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
    if (as === 'Yearly') return { key: String(y), label: String(y) };
    if (as === 'Monthly') return { key: `${y}-${String(m).padStart(2, '0')}`, label: new Date(y, m - 1, 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }) };
    const date = new Date(y, m - 1, d); const back = (date.getDay() + 1) % 7; // days since Saturday
    const start = new Date(y, m - 1, d - back); const end = new Date(y, m - 1, d - back + 6);
    const iso2 = (x: Date) => `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`;
    return { key: iso2(start), label: `${ddmy(iso2(start))} – ${ddmy(iso2(end))}` };
  }
  readonly repOptions = computed<Opt[]>(() => this.reps().map((r) => ({ value: r.id, label: `${r.fullName} · ${r.type}` })));
  readonly areaOptions = computed<Opt[]>(() => this.areas().map((a) => ({ value: a.id, label: a.name })));
  readonly labOptions = computed<Opt[]>(() => this.labs().map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` })));
  readonly subjectOptions = computed<Opt[]>(() => this.bySig() === 'Area' ? this.areaOptions() : this.bySig() === 'Lab' ? this.labOptions() : this.repOptions());

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items), error: () => {} });
    this.api.get<AreaOpt[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r), error: () => {} });
    this.api.get<LabLookup[]>('/labs/lookup').subscribe({ next: (r) => this.labs.set(r), error: () => {} });
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  kindLabel(k: string): string {
    switch (k) {
      case 'TotalRequired': return 'Total required (Rep Income)';
      case 'PenaltyRight': return 'Penalty · right test';
      case 'PenaltyWrong': return 'Penalty · wrong test';
      case 'Deduction': return 'Deduction';
      case 'ManualIncome': return 'Real income (manual, legacy)';
      case 'Collection': return 'Collection';
      default: return k;
    }
  }
  subjectLabel(): string { return this.by === 'Area' ? 'Area' : this.by === 'Lab' ? 'Lab' : 'Lab Responsible'; }

  setBy(b: ViewBy): void { this.by = b; this.bySig.set(b); this.subjectId = ''; this.st.set(null); }
  setViewAs(v: ViewAs): void { this.viewAs = v; this.viewAsSig.set(v); }
  load(): void {
    if (!this.subjectId) return;
    this.loading.set(true);
    this.api.get<Statement>('/accounting/statement', { by: this.by, id: this.subjectId, from: this.from, to: this.to })
      .subscribe({ next: (r) => { this.st.set(r); this.loading.set(false); }, error: () => { this.st.set(null); this.loading.set(false); } });
  }
  remove(r: RepStatementRow): void {
    if (!r.sourceId || !confirm(`Delete the legacy real-income line of ${ddmy(r.date)}?`)) return;
    this.api.delete(`/accounting/rep-income/${r.sourceId}`).subscribe({ next: () => { this.toast.success('Income line deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Date / Period', 'Day', 'Kind', 'Debit', 'Credit', 'Notes', 'Balance'];
  private exportRows() { return this.view().map((r) => [r.label, r.day, r.kind, money(r.debit), money(r.credit), r.notes ?? '', money(r.balance)]); }
  private title(): string { return `Statement by ${this.subjectLabel()} — ${this.st()?.subjectName ?? ''} (${ddmy(this.from)} → ${ddmy(this.to)}, ${this.viewAs.toLowerCase()})`; }
  exportExcel(): void { exportXlsx(`statement-${this.by.toLowerCase()}-${this.viewAs.toLowerCase()}-${localToday()}.xlsx`, RepStatementComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(this.title(), RepStatementComponent.HEADER, this.exportRows()); }
}
