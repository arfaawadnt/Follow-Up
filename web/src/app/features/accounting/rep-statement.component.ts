import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { UiService } from '../../core/ui.service';
import { TranslatePipe } from '../../core/i18n';
import { PagedResult, RepListItem, RepStatement, RepStatementRow } from '../../core/models';
import { ACC_STYLES, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };

/**
 * Rep Statement — a per-rep ledger over a date range. Debit = the synced income of the labs the rep collects for
 * (Oracle-derived, one line per day) plus manually recorded real income; Credit = the collections the rep took part
 * in; Balance runs Debit − Credit.
 */
@Component({
  selector: 'app-acc-rep-statement',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_rep_statement' | t : 'Rep Statement' }}</div><h1>{{ 'acc_rep_statement' | t : 'Rep Statement' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage() && repId) { <button class="btn btn-p" (click)="openNew()">{{ 'add_income' | t : 'Add real income' }}</button> }
        <button class="btn btn-s" [disabled]="!st()" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" [disabled]="!st()" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_debit' | t : 'Total debit' }}</div><div class="val">{{ (st()?.totalDebit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'oracle_income' | t : 'Synced income' }} + {{ 'manual_income' | t : 'Real income' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'total_credit' | t : 'Total credit' }}</div><div class="val">{{ (st()?.totalCredit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'collection' | t : 'Collection' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'balance' | t : 'Balance' }}</div><div class="val">{{ (st()?.balance ?? 0) | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ st()?.rows?.length ?? 0 }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:2fr 1fr 1fr 1fr;gap:12px;align-items:end">
        <div class="field"><label>{{ 'rep' | t : 'Rep' }} *</label><app-filter-select [(ngModel)]="repId" [options]="repOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><button class="btn btn-p" [disabled]="!repId" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (!repId) { <div class="empty" style="padding:24px;text-align:center">{{ 'select_rep_first' | t : 'Select a representative to view the statement.' }}</div> }
      @else if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="padding:0 16px 8px;font-weight:700">{{ st()?.repName }}</div>
        <table class="grid-table" style="margin:0;border:none">
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
        </table>
      }
    </div>

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(false)">
        <div class="as-dlg" (click)="$event.stopPropagation()" style="width:min(94vw,460px)">
          <div class="as-dlg-head"><h2>{{ 'add_income' | t : 'Add real income' }}</h2><button class="btn btn-mini btn-s" (click)="dlg.set(false)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'date' | t : 'Date' }} *</label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field"><label>{{ 'day' | t : 'Day' }}</label><input class="input" [value]="day(f.date)" disabled></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'amount' | t : 'Amount' }} *</label><input class="input" type="number" min="0.01" step="0.01" [(ngModel)]="f.amount"></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="500"></div>
            </div>
          </div>
          <div class="as-dlg-foot">
            <button class="btn btn-s" (click)="dlg.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy() || !f.date || !f.amount || f.amount <= 0" (click)="save()">{{ 'save' | t : 'Save' }}</button>
          </div>
        </div>
      </div>
    }
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
  readonly busy = signal(false);
  readonly dlg = signal(false);
  readonly st = signal<RepStatement | null>(null);
  readonly reps = signal<RepListItem[]>([]);
  repId = ''; from = firstOfMonth(); to = localToday();
  f = { date: localToday(), amount: null as number | null, notes: '' };

  readonly repOptions = computed<Opt[]>(() => this.reps().map((r) => ({ value: r.id, label: `${r.fullName} · ${r.type}` })));

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items), error: () => {} });
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  kindLabel(k: string): string { return k === 'OracleIncome' ? 'Synced income' : k === 'ManualIncome' ? 'Real income' : 'Collection'; }

  load(): void {
    if (!this.repId) return;
    this.loading.set(true);
    this.api.get<RepStatement>(`/accounting/rep-statement/${this.repId}`, { from: this.from, to: this.to })
      .subscribe({ next: (r) => { this.st.set(r); this.loading.set(false); }, error: () => { this.st.set(null); this.loading.set(false); } });
  }

  openNew(): void { this.f = { date: localToday(), amount: null, notes: '' }; this.dlg.set(true); }
  save(): void {
    if (!this.f.date || !this.f.amount || this.f.amount <= 0) return;
    this.busy.set(true);
    this.api.post('/accounting/rep-income', { date: this.f.date, representativeId: this.repId, amount: this.f.amount, notes: this.f.notes.trim() || null })
      .subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Income recorded.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(r: RepStatementRow): void {
    if (!r.sourceId || !confirm(`Delete the real-income line of ${ddmy(r.date)}?`)) return;
    this.api.delete(`/accounting/rep-income/${r.sourceId}`).subscribe({ next: () => { this.toast.success('Income line deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Date', 'Day', 'Kind', 'Debit', 'Credit', 'Notes', 'Balance'];
  private exportRows() { return (this.st()?.rows ?? []).map((r) => [ddmy(r.date), this.day(r.date), this.kindLabel(r.kind), money(r.debit), money(r.credit), r.notes ?? '', money(r.balance)]); }
  exportExcel(): void { exportXlsx(`rep-statement-${localToday()}.xlsx`, RepStatementComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Rep Statement — ${this.st()?.repName ?? ''} (${ddmy(this.from)} → ${ddmy(this.to)})`, RepStatementComponent.HEADER, this.exportRows()); }
}
