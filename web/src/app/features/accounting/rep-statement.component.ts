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
interface AreaOpt { id: string; name: string; }

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
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_debit' | t : 'Total debit' }}</div><div class="val">{{ (st()?.totalDebit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'oracle_income' | t : 'Synced income' }} + {{ 'real_income' | t : 'Real income' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'total_credit' | t : 'Total credit' }}</div><div class="val">{{ (st()?.totalCredit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'collection' | t : 'Collection' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'balance' | t : 'Balance' }}</div><div class="val">{{ (st()?.balance ?? 0) | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ st()?.rows?.length ?? 0 }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:1fr 2fr 1fr 1fr 1fr;gap:12px;align-items:end">
        <div class="field"><label>{{ 'view_by' | t : 'View by' }}</label>
          <select class="select" [ngModel]="by" (ngModelChange)="setBy($event)">
            <option value="Responsible">{{ 'lab_responsible' | t : 'Lab Responsible' }}</option>
            <option value="Area">{{ 'area_2' | t : 'Area' }}</option>
            <option value="Lab">{{ 'lab' | t : 'Lab' }}</option>
          </select></div>
        <div class="field"><label>{{ subjectLabel() }} *</label><app-filter-select [(ngModel)]="subjectId" [options]="subjectOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><button class="btn btn-p" [disabled]="!subjectId" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      @if (by !== 'Responsible') { <div class="small muted" style="margin-top:8px">{{ 'statement_by_hint' | t : 'Collections and legacy manual lines appear on the Lab Responsible view only.' }}</div> }
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (!subjectId) { <div class="empty" style="padding:24px;text-align:center">{{ 'select_rep_first' | t : 'Select a subject to view the statement.' }}</div> }
      @else if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="padding:0 16px 8px;font-weight:700">{{ st()?.subjectName }}</div>
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'date' | t : 'Date' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'kind' | t : 'Kind' }}</th>
            <th class="r">{{ 'debit' | t : 'Debit' }}</th><th class="r">{{ 'credit_out' | t : 'Credit' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th class="r">{{ 'balance' | t : 'Balance' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (r of st()?.rows ?? []; track $index) {
              <tr>
                <td>{{ ddmy(r.date) }}</td><td>{{ day(r.date) }}</td><td>{{ kindLabel(r.kind) }}</td>
                <td class="r mono pos">{{ r.debit ? (r.debit | number:'1.2-2') : '' }}</td><td class="r mono neg">{{ r.credit ? (r.credit | number:'1.2-2') : '' }}</td>
                <td>{{ r.notes || '—' }}</td><td class="r mono" style="font-weight:700">{{ r.balance | number:'1.2-2' }}</td>
                @if (canManage()) { <td class="ar actions">@if (r.kind === 'ManualIncome' && r.sourceId) { <button class="icon-btn del" title="Delete" (click)="remove(r)">🗑</button> }</td> }
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
  by: ViewBy = 'Responsible'; subjectId = ''; from = firstOfMonth(); to = localToday();
  readonly bySig = signal<ViewBy>('Responsible');
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
  kindLabel(k: string): string { return k === 'OracleIncome' ? 'Synced income' : k === 'RealIncome' ? 'Real income' : k === 'ManualIncome' ? 'Real income (manual, legacy)' : 'Collection'; }
  subjectLabel(): string { return this.by === 'Area' ? 'Area' : this.by === 'Lab' ? 'Lab' : 'Lab Responsible'; }

  setBy(b: ViewBy): void { this.by = b; this.bySig.set(b); this.subjectId = ''; this.st.set(null); }
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

  private static readonly HEADER = ['Date', 'Day', 'Kind', 'Debit', 'Credit', 'Notes', 'Balance'];
  private exportRows() { return (this.st()?.rows ?? []).map((r) => [ddmy(r.date), this.day(r.date), this.kindLabel(r.kind), money(r.debit), money(r.credit), r.notes ?? '', money(r.balance)]); }
  private title(): string { return `Statement by ${this.subjectLabel()} — ${this.st()?.subjectName ?? ''} (${ddmy(this.from)} → ${ddmy(this.to)})`; }
  exportExcel(): void { exportXlsx(`statement-${this.by.toLowerCase()}-${localToday()}.xlsx`, RepStatementComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(this.title(), RepStatementComponent.HEADER, this.exportRows()); }
}
