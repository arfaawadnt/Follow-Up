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
import { PagedResult, RealIncomeLab, RealIncomeRep, RealIncomeRow, RealIncomeSheet, RepListItem, RepStatement, RepStatementRow } from '../../core/models';
import { ACC_STYLES, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };
interface AreaOpt { id: string; name: string; }
/** Editable copy of a sheet row: the view-only context stays as served, the rep's figures are bound to inputs. */
type SheetRow = RealIncomeRow & { samplesIn: number | null; requiredIn: number | null; paidIn: number | null; delayedIn: number | null; notesIn: string; addedByHand: boolean };

/**
 * Rep Statement — two tabs.
 * "Statement": a per-rep ledger over a date range. Debit = the synced income of the labs the rep collects for (one line
 * per day) plus the rep's real income (Σ paid + delayed payment of the real-income sheet per day; legacy manual lines
 * stay visible and deletable); Credit = the collections the rep took part in; Balance runs Debit − Credit.
 * "Real income": the Lab Responsible's daily sheet (operator decision, 2026-09-16) — pick an Area, a Date and one of the
 * Lab Responsibles linked to that area; the grid lists the rep's labs with a recorded visit that day, each with view-only
 * context (LDM income from the Oracle sync, the visit's total required + samples, penalties, remaining carried from
 * earlier days) and the rep's own figures (samples, total required, paid, remaining = required − paid, delayed payment
 * against the earlier remaining, notes). A lab of the area without a recorded visit can be added by hand.
 */
@Component({
  selector: 'app-acc-rep-statement',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_rep_statement' | t : 'Rep Statement' }}</div><h1>{{ 'acc_rep_statement' | t : 'Rep Statement' }}</h1></div>
      <div class="pagehead-actions">
        @if (tab() === 'statement') {
          <button class="btn btn-s" [disabled]="!st()" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
          <button class="btn btn-s" [disabled]="!st()" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
        } @else {
          @if (canManage() && sheet()) { <button class="btn btn-p" [disabled]="busy() || !sheetValid()" (click)="saveSheet()">{{ 'save_sheet' | t : 'Save sheet' }}</button> }
          <button class="btn btn-s" [disabled]="!sheet()" (click)="exportSheetExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
          <button class="btn btn-s" [disabled]="!sheet()" (click)="exportSheetPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
        }
      </div>
    </div>

    <div class="tabs">
      <button [class.on]="tab() === 'statement'" (click)="tab.set('statement')">{{ 'statement' | t : 'Statement' }}</button>
      <button [class.on]="tab() === 'sheet'" (click)="tab.set('sheet')">{{ 'real_income' | t : 'Real income' }}</button>
    </div>

    @if (tab() === 'statement') {
      <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
        <div class="kpi kpi-green"><div class="lbl">{{ 'total_debit' | t : 'Total debit' }}</div><div class="val">{{ (st()?.totalDebit ?? 0) | number:'1.2-2' }}</div><div class="sub">{{ 'oracle_income' | t : 'Synced income' }} + {{ 'real_income' | t : 'Real income' }}</div></div>
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
    } @else {
      <div class="kpis" style="grid-template-columns:repeat(5,1fr);margin-bottom:16px">
        <div class="kpi kpi-blue"><div class="lbl">{{ 'ldm_income' | t : 'LDM income' }}</div><div class="val">{{ sk().ldm | number:'1.2-2' }}</div><div class="sub">{{ 'view_only' | t : 'View only' }}</div></div>
        <div class="kpi kpi-teal"><div class="lbl">{{ 'total_required' | t : 'Total required' }}</div><div class="val">{{ sk().required | number:'1.2-2' }}</div><div class="sub">{{ 'rep_entry' | t : 'Rep data' }}</div></div>
        <div class="kpi kpi-green"><div class="lbl">{{ 'paid' | t : 'Paid' }}</div><div class="val">{{ sk().paid | number:'1.2-2' }}</div><div class="sub">+ {{ 'delayed_payment' | t : 'Delayed payment' }} {{ sk().delayed | number:'1.2-2' }}</div></div>
        <div class="kpi kpi-amber"><div class="lbl">{{ 'remaining' | t : 'Remaining' }}</div><div class="val">{{ sk().remaining | number:'1.2-2' }}</div><div class="sub">{{ 'prev_remaining' | t : 'Remaining (previous)' }} {{ sk().previous | number:'1.2-2' }}</div></div>
        <div class="kpi kpi-purple"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ rows().length }}</div></div>
      </div>

      <div class="card" style="padding:16px;margin-bottom:16px">
        <div class="frm-grid" style="grid-template-columns:2fr 1fr 2fr 1fr;gap:12px;align-items:end">
          <div class="field"><label>{{ 'area_2' | t : 'Area' }} *</label><app-filter-select [ngModel]="areaId" (ngModelChange)="setArea($event)" [options]="areaOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
          <div class="field"><label>{{ 'date' | t : 'Date' }} *</label><app-date-input [(ngModel)]="date"></app-date-input></div>
          <div class="field"><label>{{ 'lab_responsible' | t : 'Lab Responsible' }} *</label><app-filter-select [(ngModel)]="sheetRepId" [options]="sheetRepOptions()" [clearable]="true" placeholder="—" [disabled]="!areaId"></app-filter-select></div>
          <div class="field"><button class="btn btn-p" [disabled]="!areaId || !date || !sheetRepId" (click)="loadSheet()" style="height:36px">{{ 'load' | t : 'Load' }}</button></div>
        </div>
      </div>

      <div class="card" style="padding:10px 0;overflow-x:auto">
        @if (!sheet() && !sheetLoading()) { <div class="empty" style="padding:24px;text-align:center">{{ 'select_area_rep_first' | t : 'Select an area, a date and a Lab Responsible, then Load.' }}</div> }
        @else if (sheetLoading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <div style="display:flex;justify-content:space-between;align-items:center;gap:12px;padding:0 16px 8px;flex-wrap:wrap">
            <div style="font-weight:700">{{ sheet()!.repName }} · {{ sheet()!.areaName }} · {{ ddmy(sheet()!.date) }} ({{ day(sheet()!.date) }})</div>
            @if (canManage()) {
              <div style="display:flex;gap:8px;align-items:center;min-width:320px">
                <app-filter-select [(ngModel)]="addLabId" [options]="addLabOptions()" [clearable]="true" [placeholder]="'add_lab' | t : 'Add lab'" style="flex:1"></app-filter-select>
                <button class="btn btn-s" [disabled]="!addLabId" (click)="addLab()">{{ 'add_lab' | t : 'Add lab' }}</button>
              </div>
            }
          </div>
          <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
            <thead>
              <tr>
                <th rowspan="2">{{ 'serial' | t : 'Serial' }}</th><th rowspan="2">{{ 'lab' | t : 'Lab' }}</th>
                <th colspan="5" style="text-align:center;background:var(--slate-100,#f3f2f1)">{{ 'view_only' | t : 'View only' }}</th>
                <th colspan="6" style="text-align:center">{{ 'rep_entry' | t : 'Rep data' }}</th>
                @if (canManage()) { <th rowspan="2" class="ar"></th> }
              </tr>
              <tr>
                <th class="r">{{ 'ldm_income' | t : 'LDM income' }}</th><th class="r">{{ 'visit_required' | t : 'Visit: total required' }}</th><th class="r">{{ 'visit_samples' | t : 'Visit: samples' }}</th><th class="r">{{ 'penalty' | t : 'Penalty' }}</th><th class="r">{{ 'prev_remaining' | t : 'Remaining (previous)' }}</th>
                <th class="r">{{ 'samples' | t : 'Samples' }}</th><th class="r">{{ 'total_required' | t : 'Total required' }}</th><th class="r">{{ 'paid' | t : 'Paid' }}</th><th class="r">{{ 'remaining' | t : 'Remaining' }}</th><th class="r">{{ 'delayed_payment' | t : 'Delayed payment' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
              </tr>
            </thead>
            <tbody>
              @for (r of rows(); track r.laboratoryId; let i = $index) {
                <tr>
                  <td class="mono">{{ i + 1 }}</td>
                  <td><b>{{ r.labName }}</b> <span class="small muted">{{ r.labDisplayCode }}</span>@if (r.addedByHand || !r.hasVisit) { <div class="small muted">{{ 'added_by_hand' | t : 'Added by hand (no recorded visit)' }}</div> }</td>
                  <td class="r mono">{{ r.ldmIncome | number:'1.2-2' }}</td>
                  <td class="r mono">{{ r.visitTotalRequired ?? '—' }}</td>
                  <td class="r mono">{{ r.visitSamples ?? '—' }}</td>
                  <td class="r mono" [class.neg]="r.penalty > 0">{{ r.penalty | number:'1.2-2' }}</td>
                  <td class="r mono" [class.neg]="r.previousRemaining > 0">{{ r.previousRemaining | number:'1.2-2' }}</td>
                  @if (canManage()) {
                    <td class="r"><input class="input mono" type="number" min="0" step="1" [(ngModel)]="r.samplesIn" style="width:80px;text-align:end"></td>
                    <td class="r"><input class="input mono" type="number" min="0" step="0.01" [(ngModel)]="r.requiredIn" style="width:110px;text-align:end"></td>
                    <td class="r"><input class="input mono" type="number" min="0" step="0.01" [(ngModel)]="r.paidIn" style="width:110px;text-align:end" [class.invalid]="(r.paidIn ?? 0) > (r.requiredIn ?? 0)"></td>
                    <td class="r mono" style="font-weight:700">{{ rowRemaining(r) | number:'1.2-2' }}</td>
                    <td class="r"><input class="input mono" type="number" min="0" step="0.01" [(ngModel)]="r.delayedIn" style="width:110px;text-align:end"></td>
                    <td><input class="input" [(ngModel)]="r.notesIn" maxlength="500" style="min-width:160px"></td>
                    <td class="ar actions">@if (r.addedByHand && !r.entryId) { <button class="icon-btn del" title="Remove" (click)="dropRow(r)">✕</button> }</td>
                  } @else {
                    <td class="r mono">{{ r.samples }}</td><td class="r mono">{{ r.totalRequired | number:'1.2-2' }}</td><td class="r mono">{{ r.paid | number:'1.2-2' }}</td>
                    <td class="r mono" style="font-weight:700">{{ r.remaining | number:'1.2-2' }}</td><td class="r mono">{{ r.delayedPayment | number:'1.2-2' }}</td><td>{{ r.notes || '—' }}</td>
                  }
                </tr>
              } @empty { <tr><td colspan="14" class="empty" style="text-align:center;padding:24px">{{ 'no_visits_hint' | t : 'No lab of this responsible has a recorded visit on this date. Add a lab to record it anyway.' }}</td></tr> }
            </tbody>
            @if (rows().length) {
              <tfoot><tr><td colspan="2">{{ 'total' | t : 'Total' }}</td>
                <td class="r mono">{{ sk().ldm | number:'1.2-2' }}</td><td class="r mono">{{ sk().visitRequired }}</td><td class="r mono">{{ sk().visitSamples }}</td><td class="r mono">{{ sk().penalty | number:'1.2-2' }}</td><td class="r mono">{{ sk().previous | number:'1.2-2' }}</td>
                <td class="r mono">{{ sk().samples }}</td><td class="r mono">{{ sk().required | number:'1.2-2' }}</td><td class="r mono">{{ sk().paid | number:'1.2-2' }}</td><td class="r mono">{{ sk().remaining | number:'1.2-2' }}</td><td class="r mono">{{ sk().delayed | number:'1.2-2' }}</td><td></td>
                @if (canManage()) { <td></td> }
              </tr></tfoot>
            }
          </table></div>
        }
      </div>
    }
  `,
  styles: [ACC_STYLES, `.input.invalid{border-color:var(--red-600,#dc2626)}`],
})
export class RepStatementComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;

  readonly tab = signal<'statement' | 'sheet'>('statement');

  // ---- Statement tab
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly st = signal<RepStatement | null>(null);
  readonly reps = signal<RepListItem[]>([]);
  repId = ''; from = firstOfMonth(); to = localToday();
  readonly repOptions = computed<Opt[]>(() => this.reps().map((r) => ({ value: r.id, label: `${r.fullName} · ${r.type}` })));

  // ---- Real income sheet tab
  readonly areas = signal<AreaOpt[]>([]);
  readonly sheetReps = signal<RealIncomeRep[]>([]);
  readonly sheetLabs = signal<RealIncomeLab[]>([]);
  readonly sheet = signal<RealIncomeSheet | null>(null);
  readonly rows = signal<SheetRow[]>([]);
  readonly sheetLoading = signal(false);
  areaId = ''; date = localToday(); sheetRepId = ''; addLabId = '';
  readonly areaOptions = computed<Opt[]>(() => this.areas().map((a) => ({ value: a.id, label: a.name })));
  readonly sheetRepOptions = computed<Opt[]>(() => this.sheetReps().map((r) => ({ value: r.id, label: `${r.fullName} · ${r.labCount}` })));
  readonly addLabOptions = computed<Opt[]>(() => {
    const present = new Set(this.rows().map((r) => r.laboratoryId));
    return this.sheetLabs().filter((l) => !present.has(l.id)).map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` }));
  });
  /** Sheet totals over the rows as currently typed (the rep's figures) and as served (the view-only context). */
  readonly sk = computed(() => {
    const rs = this.rows();
    const sum = (f: (r: SheetRow) => number) => money(rs.reduce((a, r) => a + f(r), 0));
    return {
      ldm: sum((r) => r.ldmIncome), penalty: sum((r) => r.penalty), previous: sum((r) => r.previousRemaining),
      visitRequired: rs.reduce((a, r) => a + (r.visitTotalRequired ?? 0), 0), visitSamples: rs.reduce((a, r) => a + (r.visitSamples ?? 0), 0),
      samples: rs.reduce((a, r) => a + (r.samplesIn ?? 0), 0), required: sum((r) => r.requiredIn ?? 0), paid: sum((r) => r.paidIn ?? 0),
      remaining: sum((r) => this.rowRemaining(r)), delayed: sum((r) => r.delayedIn ?? 0),
    };
  });

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items), error: () => {} });
    this.api.get<AreaOpt[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r), error: () => {} });
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  kindLabel(k: string): string { return k === 'OracleIncome' ? 'Synced income' : k === 'RealIncome' ? 'Real income' : k === 'ManualIncome' ? 'Real income (manual, legacy)' : 'Collection'; }

  // ---- Statement
  load(): void {
    if (!this.repId) return;
    this.loading.set(true);
    this.api.get<RepStatement>(`/accounting/rep-statement/${this.repId}`, { from: this.from, to: this.to })
      .subscribe({ next: (r) => { this.st.set(r); this.loading.set(false); }, error: () => { this.st.set(null); this.loading.set(false); } });
  }
  remove(r: RepStatementRow): void {
    if (!r.sourceId || !confirm(`Delete the legacy real-income line of ${ddmy(r.date)}?`)) return;
    this.api.delete(`/accounting/rep-income/${r.sourceId}`).subscribe({ next: () => { this.toast.success('Income line deleted.'); this.load(); } });
  }

  // ---- Sheet
  setArea(id: string): void {
    this.areaId = id; this.sheetRepId = ''; this.sheetReps.set([]); this.sheetLabs.set([]); this.sheet.set(null); this.rows.set([]);
    if (!id) return;
    this.api.get<RealIncomeRep[]>('/accounting/real-income/reps', { areaId: id }).subscribe({ next: (r) => this.sheetReps.set(r), error: () => {} });
    this.api.get<RealIncomeLab[]>('/accounting/real-income/labs', { areaId: id }).subscribe({ next: (r) => this.sheetLabs.set(r), error: () => {} });
  }
  loadSheet(): void {
    if (!this.areaId || !this.date || !this.sheetRepId) return;
    this.sheetLoading.set(true); this.addLabId = '';
    this.api.get<RealIncomeSheet>('/accounting/real-income/sheet', { areaId: this.areaId, date: this.date, repId: this.sheetRepId }).subscribe({
      next: (s) => { this.sheet.set(s); this.rows.set(s.rows.map((r) => this.toRow(r, false))); this.sheetLoading.set(false); },
      error: () => { this.sheet.set(null); this.rows.set([]); this.sheetLoading.set(false); },
    });
  }
  private toRow(r: RealIncomeRow, addedByHand: boolean): SheetRow {
    const z = (v: number) => (v === 0 ? null : v);
    return { ...r, addedByHand, samplesIn: z(r.samples), requiredIn: z(r.totalRequired), paidIn: z(r.paid), delayedIn: z(r.delayedPayment), notesIn: r.notes ?? '' };
  }
  rowRemaining(r: SheetRow): number { return money((r.requiredIn ?? 0) - (r.paidIn ?? 0)); }
  /** A lab of the area the visits did not produce: added with an empty entry; its view-only context arrives after the first save (reload). */
  addLab(): void {
    const lab = this.sheetLabs().find((l) => l.id === this.addLabId);
    if (!lab || this.rows().some((r) => r.laboratoryId === lab.id)) return;
    this.rows.update((rs) => [...rs, this.toRow({
      laboratoryId: lab.id, labDisplayCode: lab.displayCode, labName: lab.name, hasVisit: false, visitTotalRequired: null, visitSamples: null,
      ldmIncome: 0, penalty: 0, previousRemaining: 0, entryId: null, samples: 0, totalRequired: 0, paid: 0, remaining: 0, delayedPayment: 0, notes: null,
    }, true)]);
    this.addLabId = '';
  }
  dropRow(r: SheetRow): void { this.rows.update((rs) => rs.filter((x) => x !== r)); }
  sheetValid(): boolean {
    return this.rows().every((r) => (r.samplesIn ?? 0) >= 0 && (r.requiredIn ?? 0) >= 0 && (r.paidIn ?? 0) >= 0 && (r.delayedIn ?? 0) >= 0 && (r.paidIn ?? 0) <= (r.requiredIn ?? 0));
  }
  saveSheet(): void {
    const s = this.sheet(); if (!s || !this.sheetValid()) return;
    this.busy.set(true);
    const body = {
      date: s.date, representativeId: s.representativeId,
      rows: this.rows().map((r) => ({ laboratoryId: r.laboratoryId, samples: Math.round(r.samplesIn ?? 0), totalRequired: money(r.requiredIn), paid: money(r.paidIn), delayedPayment: money(r.delayedIn), notes: r.notesIn.trim() || null })),
    };
    this.api.put('/accounting/real-income/sheet', body).subscribe({ next: () => { this.busy.set(false); this.toast.success('Sheet saved.'); this.loadSheet(); }, error: () => this.busy.set(false) });
  }

  // ---- Exports
  private static readonly HEADER = ['Date', 'Day', 'Kind', 'Debit', 'Credit', 'Notes', 'Balance'];
  private exportRows() { return (this.st()?.rows ?? []).map((r) => [ddmy(r.date), this.day(r.date), this.kindLabel(r.kind), money(r.debit), money(r.credit), r.notes ?? '', money(r.balance)]); }
  exportExcel(): void { exportXlsx(`rep-statement-${localToday()}.xlsx`, RepStatementComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Rep Statement — ${this.st()?.repName ?? ''} (${ddmy(this.from)} → ${ddmy(this.to)})`, RepStatementComponent.HEADER, this.exportRows()); }

  private static readonly SHEET_HEADER = ['Serial', 'Lab', 'Code', 'LDM income', 'Visit: total required', 'Visit: samples', 'Penalty', 'Remaining (previous)', 'Samples', 'Total required', 'Paid', 'Remaining', 'Delayed payment', 'Notes'];
  private exportSheetRows() {
    return this.rows().map((r, i) => [i + 1, r.labName, r.labDisplayCode, money(r.ldmIncome), r.visitTotalRequired ?? '', r.visitSamples ?? '', money(r.penalty), money(r.previousRemaining),
      r.samplesIn ?? 0, money(r.requiredIn), money(r.paidIn), this.rowRemaining(r), money(r.delayedIn), r.notesIn]);
  }
  exportSheetExcel(): void { const s = this.sheet(); exportXlsx(`real-income-${s?.date ?? localToday()}.xlsx`, RepStatementComponent.SHEET_HEADER, this.exportSheetRows()); }
  exportSheetPdf(): void { const s = this.sheet(); printTable(`Real income — ${s?.repName ?? ''} · ${s?.areaName ?? ''} · ${ddmy(s?.date ?? null)}`, RepStatementComponent.SHEET_HEADER, this.exportSheetRows()); }
}
