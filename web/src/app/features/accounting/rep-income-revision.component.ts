import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ddmy, exportXlsx, localToday, printTable, SheetCell } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { AuditLogButtonComponent } from '../../shared/audit-log-button.component';
import { LdmDetailsDialogComponent } from '../../shared/ldm-details-dialog.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { UiService } from '../../core/ui.service';
import { I18nService, TranslatePipe } from '../../core/i18n';
import { PagedResult, RepIncomeRevisionRow, RepListItem, StatementLdmDetail } from '../../core/models';
import { ACC_STYLES, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };
const DASH = '—';
/** A grid line: the served row plus the reviewer's inputs and a dirty flag. */
type Row = RepIncomeRevisionRow & { incomeIn: number | null; paidIn: number | null; delayedIn: number | null; notesIn: string; dirty: boolean; saving: boolean };

/**
 * Rep Income Revision (2026-09-30) — the reviewer's page: every entered Rep Income line (lab × day) beside its LDM side —
 * income, accessions, tests, tests not verified, tests added within / after 3 hours, and the registrations behind it —
 * with the reviewer's ACTUAL figures entered beside each line (actual income, actual paid, actual remaining = income −
 * paid, actual delayed payment, notes). Filters narrow by period, geography, Lab Responsible, lab, review state and the
 * entered-vs-LDM variance; the cards and the sum row follow the filtered lines. Choosing a Lab Responsible also lists that
 * rep's labs with LDM income but no sheet entry (a missed sheet). The revision is a review record only.
 */
@Component({
  selector: 'app-acc-rep-income-revision',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent, AuditLogButtonComponent, LdmDetailsDialogComponent, RouterLink],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_rep_income_revision' | t : 'Rep Income Revision' }}</div><h1>{{ 'acc_rep_income_revision' | t : 'Rep Income Revision' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" [disabled]="!dirtyCount() || savingAll()" (click)="saveAll()">{{ savingAll() ? ('saving' | t : 'Saving…') : (('save_all_revisions' | t : 'Save revisions') + (dirtyCount() ? ' (' + dirtyCount() + ')' : '')) }}</button> }
        <a class="btn btn-s" routerLink="/accounting/rep-income">{{ 'acc_rep_income' | t : 'Rep Income' }}</a>
        <button class="btn btn-s" [disabled]="!filtered().length" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" [disabled]="!filtered().length" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:12px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'revision_lines' | t : 'Lines' }}</div><div class="val">{{ k().lines | number:'1.0-0' }}</div><div class="sub">{{ k().labs }} {{ 'labs' | t : 'labs' }} · {{ k().reps }} {{ 'lab_responsibles' | t : 'Lab Responsibles' }} · {{ k().days }} {{ 'days' | t : 'days' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_required' | t : 'Total required' }} ({{ 'rep_entry' | t : 'Rep data' }})</div><div class="val">{{ k().required | number:'1.2-2' }}</div><div class="sub">{{ 'paid' | t : 'Paid' }} {{ k().paid | number:'1.2-2' }} · {{ 'remaining' | t : 'Remaining' }} {{ k().remaining | number:'1.2-2' }} · {{ 'delayed_payment' | t : 'Delayed payment' }} {{ k().delayed | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'ldm_income' | t : 'LDM income' }}</div><div class="val">{{ k().ldm | number:'1.2-2' }}</div><div class="sub">{{ 'variance_entered_ldm' | t : 'Entered − LDM' }} <b [class.neg]="k().variance < 0" [class.pos]="k().variance > 0">{{ k().variance | number:'1.2-2' }}</b> · {{ k().below }} {{ 'below_ldm' | t : 'below' }} · {{ k().above }} {{ 'above_ldm' | t : 'above' }} · {{ k().matches }} {{ 'matches_ldm' | t : 'match' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'actual_income' | t : 'Actual income' }} ({{ 'revised' | t : 'revised' }})</div><div class="val">{{ k().actualIncome | number:'1.2-2' }}</div><div class="sub">{{ 'actual_paid' | t : 'Actual paid' }} {{ k().actualPaid | number:'1.2-2' }} · {{ 'actual_remaining' | t : 'Actual remaining' }} {{ k().actualRemaining | number:'1.2-2' }} · {{ 'actual_vs_entered' | t : 'Actual − entered' }} <b [class.neg]="k().actualVariance < 0" [class.pos]="k().actualVariance > 0">{{ k().actualVariance | number:'1.2-2' }}</b></div></div>
    </div>
    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'review_progress' | t : 'Review progress' }}</div><div class="val">{{ k().reviewed }} / {{ k().lines }}</div><div class="sub">{{ k().reviewedPct | number:'1.0-0' }}% {{ 'reviewed' | t : 'reviewed' }} · {{ k().pending }} {{ 'pending' | t : 'pending' }}@if (k().noEntry) { · <b class="neg">{{ k().noEntry }} {{ 'no_sheet_entry' | t : 'without a sheet entry' }}</b> }</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'accessions_tests' | t : 'Accessions / tests' }} (LDM)</div><div class="val">{{ k().accessions | number:'1.0-0' }} / {{ k().tests | number:'1.0-0' }}</div><div class="sub">{{ 'avg_per_line' | t : 'per line' }} {{ (k().lines ? k().tests / k().lines : 0) | number:'1.1-1' }} {{ 'tests' | t : 'tests' }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">{{ 'not_verified_tests' | t : 'Tests not verified' }}</div><div class="val">{{ k().notVerified | number:'1.0-0' }}</div><div class="sub">{{ (k().tests ? 100 * k().notVerified / k().tests : 0) | number:'1.0-1' }}% {{ 'of_tests' | t : 'of tests' }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'late_additions' | t : 'Tests added late' }}</div><div class="val">{{ k().within + k().after | number:'1.0-0' }}</div><div class="sub"><span class="late-note">{{ k().within }} {{ 'add_within_3h' | t : 'Within 3 Hours' }}</span> · <span class="nv-note">{{ k().after }} {{ 'add_after_3h' | t : 'After 3 Hours' }}</span></div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'lab_responsible' | t : 'Lab Responsible' }}</label><app-filter-select [(ngModel)]="repId" [options]="repOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><app-filter-select [multiple]="true" [options]="govs()" [ngModel]="gov()" (ngModelChange)="gov.set($event); city.set([]); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><app-filter-select [multiple]="true" [options]="cities()" [ngModel]="city()" (ngModelChange)="city.set($event); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [multiple]="true" [options]="areas()" [ngModel]="area()" (ngModelChange)="area.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'lab' | t : 'Lab' }}</label><app-filter-select [multiple]="true" [options]="labNames()" [ngModel]="lab()" (ngModelChange)="lab.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'review_state' | t : 'Review state' }}</label>
          <select class="select" [ngModel]="state()" (ngModelChange)="state.set($event)">
            <option value="">{{ 'all' | t : 'All' }}</option><option value="reviewed">{{ 'reviewed' | t : 'Reviewed' }}</option><option value="pending">{{ 'pending' | t : 'Pending' }}</option>
          </select></div>
        <div class="field"><label>{{ 'variance_entered_ldm' | t : 'Entered − LDM' }}</label>
          <select class="select" [ngModel]="variance()" (ngModelChange)="variance.set($event)">
            <option value="">{{ 'all' | t : 'All' }}</option><option value="match">{{ 'matches_ldm' | t : 'Matches LDM' }}</option><option value="below">{{ 'below_ldm' | t : 'Below LDM' }}</option><option value="above">{{ 'above_ldm' | t : 'Above LDM' }}</option><option value="noentry">{{ 'no_sheet_entry' | t : 'No sheet entry' }}</option>
          </select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      <div class="small muted" style="margin-top:8px">{{ 'revision_hint' | t : 'Lines = the Rep Income sheet entries of the period (by lab and date); choosing a Lab Responsible also lists that rep\\'s labs with LDM income but no entry. Enter the actual figures beside each line and save; actual remaining = actual income − actual paid. The statement keeps posting the entered figures.' }}</div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead>
            <tr>
              <th rowspan="2">#</th><th rowspan="2" class="stick">{{ 'lab' | t : 'Lab' }}</th><th rowspan="2">{{ 'lab_responsible' | t : 'Lab Responsible' }}</th><th rowspan="2">{{ 'date' | t : 'Date' }} / {{ 'time' | t : 'Time' }}</th>
              <th colspan="6" style="text-align:center">{{ 'rep_entry' | t : 'Rep data' }}</th>
              <th colspan="8" style="text-align:center;background:var(--slate-100,#f3f2f1)">LDM</th>
              <th colspan="5" style="text-align:center;background:#fffbeb">{{ 'actual_revision' | t : 'Actual (revision)' }}</th>
              <th rowspan="2" class="ar">{{ 'actions' | t : 'Actions' }}</th>
            </tr>
            <tr>
              <th class="r">{{ 'samples' | t : 'Samples' }}</th><th class="r">{{ 'total_required' | t : 'Total required' }}</th><th class="r">{{ 'paid' | t : 'Paid' }}</th><th class="r">{{ 'remaining' | t : 'Remaining' }}</th><th class="r">{{ 'delayed_payment' | t : 'Delayed payment' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
              <th class="r">{{ 'ldm_income' | t : 'LDM income' }}</th><th class="r">{{ 'variance_entered_ldm' | t : 'Entered − LDM' }}</th><th class="r">{{ 'accessions' | t : 'Accessions' }}</th><th class="r">{{ 'tests' | t : 'Tests' }}</th><th class="r">{{ 'not_verified' | t : 'Not verified' }}</th><th class="r">{{ 'add_within_3h' | t : 'Within 3 Hours' }}</th><th class="r">{{ 'add_after_3h' | t : 'After 3 Hours' }}</th><th></th>
              <th class="r">{{ 'actual_income' | t : 'Actual income' }}</th><th class="r">{{ 'actual_paid' | t : 'Actual paid' }}</th><th class="r">{{ 'actual_remaining' | t : 'Actual remaining' }}</th><th class="r">{{ 'actual_delayed' | t : 'Actual delayed payment' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of paged(); track r.laboratoryId + '|' + r.representativeId + '|' + r.date; let i = $index) {
              <tr [class.no-entry]="!r.entryId" [class.reviewed]="!!r.revisionId" [class.dirty]="r.dirty">
                <td class="mono">{{ pageStart() + i + 1 }}</td>
                <td class="stick"><b>{{ r.labName }}</b> <span class="small muted">{{ r.labDisplayCode }}</span><div class="small muted">{{ r.area || DASH }}@if (!r.entryId) { · <span class="neg">{{ 'no_sheet_entry' | t : 'No sheet entry' }}</span> }</div></td>
                <td>{{ r.repName }}</td>
                <td class="mono" style="white-space:nowrap">{{ ddmy(r.date) }} <span class="small muted">{{ day(r.date) }}</span><div class="small muted">{{ r.enteredAt ? ddmy(r.enteredAt, true) : DASH }}</div></td>
                <td class="r mono">{{ r.entryId ? r.samples : DASH }}</td><td class="r mono">{{ r.totalRequired | number:'1.2-2' }}</td><td class="r mono">{{ r.paid | number:'1.2-2' }}</td><td class="r mono" [class.neg]="r.remaining > 0">{{ r.remaining | number:'1.2-2' }}</td><td class="r mono">{{ r.delayedPayment | number:'1.2-2' }}</td><td class="small">{{ r.notes || DASH }}</td>
                <td class="r mono">{{ r.ldmIncome | number:'1.2-2' }}</td><td class="r mono" [class.neg]="varianceOf(r) < -0.004" [class.pos]="varianceOf(r) > 0.004">{{ varianceOf(r) | number:'1.2-2' }}</td>
                <td class="r mono">{{ r.accessions }}</td><td class="r mono">{{ r.tests }}</td><td class="r mono" [class.neg]="r.notVerified > 0">{{ r.notVerified }}</td><td class="r mono late-note">{{ r.addedWithin3h }}</td><td class="r mono nv-note">{{ r.addedAfter3h }}</td>
                <td><button class="btn btn-mini btn-s" [disabled]="!r.tests" (click)="openDetails(r)">{{ 'details_btn' | t : 'Details' }}</button></td>
                @if (canManage()) {
                  <td class="r"><input class="input mono" type="number" min="0" step="0.01" [ngModel]="r.incomeIn" (ngModelChange)="r.incomeIn = $event; r.dirty = true" style="width:110px;text-align:end"></td>
                  <td class="r"><input class="input mono" type="number" min="0" step="0.01" [ngModel]="r.paidIn" (ngModelChange)="r.paidIn = $event; r.dirty = true" style="width:110px;text-align:end" [class.invalid]="(r.paidIn ?? 0) > (r.incomeIn ?? 0)"></td>
                  <td class="r mono" style="font-weight:700">{{ actualRemainingOf(r) | number:'1.2-2' }}</td>
                  <td class="r"><input class="input mono" type="number" min="0" step="0.01" [ngModel]="r.delayedIn" (ngModelChange)="r.delayedIn = $event; r.dirty = true" style="width:110px;text-align:end"></td>
                  <td><input class="input" [ngModel]="r.notesIn" (ngModelChange)="r.notesIn = $event; r.dirty = true" maxlength="500" style="min-width:160px"></td>
                  <td class="ar actions" style="white-space:nowrap"><button class="btn btn-mini btn-p" [disabled]="!r.dirty || r.saving || (r.paidIn ?? 0) > (r.incomeIn ?? 0)" (click)="save(r)">{{ r.saving ? '…' : ('save' | t : 'Save') }}</button>@if (r.revisionId) { <app-audit-log entity="RepIncomeRevision" [id]="r.revisionId" [label]="r.labName + ' · ' + ddmy(r.date)"></app-audit-log><div class="small muted">{{ r.revisedBy }} · {{ ddmy(r.revisedAt, true) }}</div> }</td>
                } @else {
                  <td class="r mono">{{ r.actualIncome === null ? DASH : (r.actualIncome | number:'1.2-2') }}</td><td class="r mono">{{ r.actualPaid === null ? DASH : (r.actualPaid | number:'1.2-2') }}</td><td class="r mono" style="font-weight:700">{{ r.actualRemaining === null ? DASH : (r.actualRemaining | number:'1.2-2') }}</td><td class="r mono">{{ r.actualDelayedPayment === null ? DASH : (r.actualDelayedPayment | number:'1.2-2') }}</td><td class="small">{{ r.revisionNotes || DASH }}</td>
                  <td class="ar actions">@if (r.revisionId) { <app-audit-log entity="RepIncomeRevision" [id]="r.revisionId" [label]="r.labName + ' · ' + ddmy(r.date)"></app-audit-log><div class="small muted">{{ r.revisedBy }} · {{ ddmy(r.revisedAt, true) }}</div> }</td>
                }
              </tr>
            } @empty { <tr><td colspan="24" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (filtered().length) {
            <tfoot><tr>
              <td colspan="4">{{ 'total' | t : 'Total' }} <span class="small muted">· {{ filtered().length }} {{ 'rows_2' | t : 'row(s)' }}</span></td>
              <td class="r mono">{{ k().samples }}</td><td class="r mono">{{ k().required | number:'1.2-2' }}</td><td class="r mono">{{ k().paid | number:'1.2-2' }}</td><td class="r mono">{{ k().remaining | number:'1.2-2' }}</td><td class="r mono">{{ k().delayed | number:'1.2-2' }}</td><td></td>
              <td class="r mono">{{ k().ldm | number:'1.2-2' }}</td><td class="r mono" [class.neg]="k().variance < 0" [class.pos]="k().variance > 0">{{ k().variance | number:'1.2-2' }}</td><td class="r mono">{{ k().accessions }}</td><td class="r mono">{{ k().tests }}</td><td class="r mono">{{ k().notVerified }}</td><td class="r mono">{{ k().within }}</td><td class="r mono">{{ k().after }}</td><td></td>
              <td class="r mono">{{ k().actualIncome | number:'1.2-2' }}</td><td class="r mono">{{ k().actualPaid | number:'1.2-2' }}</td><td class="r mono">{{ k().actualRemaining | number:'1.2-2' }}</td><td class="r mono">{{ k().actualDelayed | number:'1.2-2' }}</td><td></td><td></td>
            </tr></tfoot>
          }
        </table></div>
        @if (filtered().length) {
          <div class="fu-pager">
            <button class="btn-ghost" [disabled]="curPage() <= 1" (click)="page.set(curPage() - 1)">‹ {{ 'prev' | t : 'Prev' }}</button>
            <span>{{ 'page' | t : 'Page' }} {{ curPage() }} / {{ pageCount() }} · {{ filtered().length | number:'1.0-0' }} {{ 'rows_2' | t : 'row(s)' }}</span>
            <button class="btn-ghost" [disabled]="curPage() >= pageCount()" (click)="page.set(curPage() + 1)">{{ 'next' | t : 'Next' }} ›</button>
            <select class="select" [ngModel]="pageSize()" (ngModelChange)="pageSize.set(+$event); page.set(1)" style="max-width:90px;margin-inline-start:auto">
              <option [ngValue]="50">50</option><option [ngValue]="100">100</option><option [ngValue]="200">200</option>
            </select>
          </div>
        }
      }
    </div>

    @if (details()) {
      <app-ldm-details-dialog [title]="ddmy(details()!.date)" [subtitle]="details()!.labName + ' · ' + details()!.repName" [rows]="detailRows()" [loading]="detailsLoading()" [fileStem]="'ldm-registrations-' + details()!.labDisplayCode + '-' + details()!.date" (closed)="details.set(null)"></app-ldm-details-dialog>
    }
  `,
  styles: [ACC_STYLES, `
    .stick{position:sticky;inset-inline-start:0;z-index:1} td.stick{background:var(--white,#fff)} thead .stick{z-index:2}
    .input.invalid{border-color:var(--red-600,#dc2626)}
    tr.no-entry td{background:#fff7ed} tr.reviewed td.stick{border-inline-start:3px solid #16a34a} tr.dirty td{background:#fefce8}
    .nv-note{color:#b91c1c;font-weight:600} .late-note{color:#1d4ed8;font-weight:600} .pos{color:#15803d}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150,#edebe9);font-size:12.5px;color:var(--slate-700,#605e5c)}
  `],
})
export class RepIncomeRevisionComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  private readonly i18n = inject(I18nService);
  readonly ddmy = ddmy;
  readonly DASH = DASH;

  readonly loading = signal(false);
  readonly savingAll = signal(false);
  readonly rows = signal<Row[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  from = firstOfMonth(); to = localToday(); repId = '';
  readonly gov = signal<string[]>([]);
  readonly city = signal<string[]>([]);
  readonly area = signal<string[]>([]);
  readonly lab = signal<string[]>([]);
  readonly state = signal('');
  readonly variance = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(100);
  /** The line whose LDM registrations are open in the details dialog. */
  readonly details = signal<Row | null>(null);
  readonly detailsLoading = signal(false);
  readonly detailRows = signal<StatementLdmDetail[]>([]);

  readonly repOptions = computed<Opt[]>(() => this.reps().filter((r) => r.type === 'LabResponsible').map((r) => ({ value: r.id, label: r.fullName })));
  readonly govs = computed(() => [...new Set(this.rows().map((r) => r.governorate ?? DASH))].sort());
  readonly cities = computed(() => [...new Set(this.rows().filter((r) => !this.gov().length || this.gov().includes(r.governorate ?? DASH)).map((r) => r.city ?? DASH))].sort());
  readonly areas = computed(() => [...new Set(this.rows().filter((r) => (!this.gov().length || this.gov().includes(r.governorate ?? DASH)) && (!this.city().length || this.city().includes(r.city ?? DASH))).map((r) => r.area ?? DASH))].sort());
  readonly labNames = computed(() => [...new Set(this.rows().map((r) => r.labName))].sort());

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string): string { return dayName(d, this.ui.lang()); }
  /** Entered total required − LDM income (positive = entered above LDM). */
  varianceOf(r: RepIncomeRevisionRow): number { return money(r.totalRequired - r.ldmIncome); }
  varianceClass(r: RepIncomeRevisionRow): 'match' | 'below' | 'above' | 'noentry' {
    if (!r.entryId) return 'noentry';
    const v = this.varianceOf(r); return Math.abs(v) < 0.005 ? 'match' : v < 0 ? 'below' : 'above';
  }
  actualRemainingOf(r: Row): number { return money((r.incomeIn ?? 0) - (r.paidIn ?? 0)); }

  private matches(r: Row): boolean {
    return (!this.gov().length || this.gov().includes(r.governorate ?? DASH)) &&
      (!this.city().length || this.city().includes(r.city ?? DASH)) &&
      (!this.area().length || this.area().includes(r.area ?? DASH)) &&
      (!this.lab().length || this.lab().includes(r.labName)) &&
      (!this.state() || (this.state() === 'reviewed') === !!r.revisionId) &&
      (!this.variance() || this.varianceClass(r) === this.variance());
  }
  /** Filtered lines, by lab then date (the served order). */
  readonly filtered = computed<Row[]>(() => this.rows().filter((r) => this.matches(r)));
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize())));
  readonly curPage = computed(() => Math.min(this.page(), this.pageCount()));
  readonly pageStart = computed(() => (this.curPage() - 1) * this.pageSize());
  readonly paged = computed<Row[]>(() => this.filtered().slice(this.pageStart(), this.pageStart() + this.pageSize()));
  readonly dirtyCount = computed(() => this.rows().filter((r) => r.dirty).length);

  /** Cards + sum row over the filtered lines. */
  readonly k = computed(() => {
    const f = this.filtered();
    const sum = (fn: (r: Row) => number) => money(f.reduce((a, r) => a + fn(r), 0));
    const reviewed = f.filter((r) => r.revisionId).length;
    const required = sum((r) => r.totalRequired); const ldm = sum((r) => r.ldmIncome);
    const actualIncome = sum((r) => r.actualIncome ?? 0);
    return {
      lines: f.length, labs: new Set(f.map((r) => r.laboratoryId)).size, reps: new Set(f.map((r) => r.representativeId)).size, days: new Set(f.map((r) => r.date)).size,
      samples: f.reduce((a, r) => a + r.samples, 0), required, paid: sum((r) => r.paid), remaining: sum((r) => r.remaining), delayed: sum((r) => r.delayedPayment),
      ldm, variance: money(required - ldm),
      below: f.filter((r) => this.varianceClass(r) === 'below').length, above: f.filter((r) => this.varianceClass(r) === 'above').length, matches: f.filter((r) => this.varianceClass(r) === 'match').length,
      noEntry: f.filter((r) => !r.entryId).length,
      accessions: f.reduce((a, r) => a + r.accessions, 0), tests: f.reduce((a, r) => a + r.tests, 0), notVerified: f.reduce((a, r) => a + r.notVerified, 0),
      within: f.reduce((a, r) => a + r.addedWithin3h, 0), after: f.reduce((a, r) => a + r.addedAfter3h, 0),
      reviewed, pending: f.length - reviewed, reviewedPct: f.length ? (100 * reviewed) / f.length : 0,
      actualIncome, actualPaid: sum((r) => r.actualPaid ?? 0), actualRemaining: sum((r) => r.actualRemaining ?? 0), actualDelayed: sum((r) => r.actualDelayedPayment ?? 0),
      actualVariance: money(actualIncome - money(f.filter((r) => r.revisionId).reduce((a, r) => a + r.totalRequired, 0))),
    };
  });

  load(): void {
    if (this.dirtyCount() && !confirm(this.i18n.t('unsaved_revisions_confirm', 'Unsaved revisions will be lost. Reload anyway?'))) return;
    this.loading.set(true); this.page.set(1);
    const params: Record<string, string> = { from: this.from, to: this.to }; if (this.repId) params['repId'] = this.repId;
    this.api.get<RepIncomeRevisionRow[]>('/accounting/rep-income-revision', params).subscribe({
      next: (rows) => { this.rows.set(rows.map((r) => this.toRow(r))); this.loading.set(false); },
      error: () => { this.rows.set([]); this.loading.set(false); },
    });
  }
  private toRow(r: RepIncomeRevisionRow): Row {
    return { ...r, incomeIn: r.actualIncome, paidIn: r.actualPaid, delayedIn: r.actualDelayedPayment, notesIn: r.revisionNotes ?? '', dirty: false, saving: false };
  }
  save(r: Row): void {
    if ((r.paidIn ?? 0) > (r.incomeIn ?? 0)) { this.toast.warning(this.i18n.t('actual_paid_exceeds', 'Actual paid cannot exceed the actual income.')); return; }
    r.saving = true;
    this.api.put<{ id: string | null }>('/accounting/rep-income-revision', { laboratoryId: r.laboratoryId, representativeId: r.representativeId, date: r.date,
      actualIncome: r.incomeIn ?? 0, actualPaid: r.paidIn ?? 0, actualDelayedPayment: r.delayedIn ?? 0, notes: r.notesIn || null }).subscribe({
      next: (res) => {
        const revised = !!res.id;
        Object.assign(r, { saving: false, dirty: false, revisionId: res.id, actualIncome: revised ? r.incomeIn ?? 0 : null, actualPaid: revised ? r.paidIn ?? 0 : null,
          actualRemaining: revised ? this.actualRemainingOf(r) : null, actualDelayedPayment: revised ? r.delayedIn ?? 0 : null, revisionNotes: revised ? (r.notesIn || null) : null,
          revisedAt: revised ? new Date().toISOString() : null, revisedBy: revised ? this.auth.session()?.username ?? '' : null });
        this.rows.set([...this.rows()]);
        this.toast.success(revised ? `${r.labName} · ${ddmy(r.date)}: revision saved.` : `${r.labName} · ${ddmy(r.date)}: revision cleared.`);
      },
      error: () => { r.saving = false; this.rows.set([...this.rows()]); },
    });
  }
  async saveAll(): Promise<void> {
    const dirty = this.rows().filter((r) => r.dirty && (r.paidIn ?? 0) <= (r.incomeIn ?? 0));
    if (!dirty.length) return;
    this.savingAll.set(true);
    for (const r of dirty) {
      await new Promise<void>((resolve) => {
        r.saving = true;
        this.api.put<{ id: string | null }>('/accounting/rep-income-revision', { laboratoryId: r.laboratoryId, representativeId: r.representativeId, date: r.date,
          actualIncome: r.incomeIn ?? 0, actualPaid: r.paidIn ?? 0, actualDelayedPayment: r.delayedIn ?? 0, notes: r.notesIn || null }).subscribe({
          next: (res) => {
            const revised = !!res.id;
            Object.assign(r, { saving: false, dirty: false, revisionId: res.id, actualIncome: revised ? r.incomeIn ?? 0 : null, actualPaid: revised ? r.paidIn ?? 0 : null,
              actualRemaining: revised ? this.actualRemainingOf(r) : null, actualDelayedPayment: revised ? r.delayedIn ?? 0 : null, revisionNotes: revised ? (r.notesIn || null) : null,
              revisedAt: revised ? new Date().toISOString() : null, revisedBy: revised ? this.auth.session()?.username ?? '' : null });
            resolve();
          },
          error: () => { r.saving = false; resolve(); },
        });
      });
    }
    this.rows.set([...this.rows()]); this.savingAll.set(false);
    this.toast.success(`${dirty.length} revision(s) saved.`);
  }
  /** The registrations behind the LDM figures: this lab on this day (kind follows whether the day has a sheet entry). */
  openDetails(r: Row): void {
    this.details.set(r); this.detailsLoading.set(true); this.detailRows.set([]);
    this.api.get<StatementLdmDetail[]>('/accounting/statement/ldm-details', { by: 'Lab', id: r.laboratoryId, date: r.date, kind: r.entryId ? 'TotalRequired' : 'LdmIncome' })
      .subscribe({ next: (rows) => { this.detailRows.set(rows); this.detailsLoading.set(false); }, error: () => this.detailsLoading.set(false) });
  }

  private static readonly HEADER = ['Lab', 'Code', 'Area', 'Lab Responsible', 'Date', 'Entered at', 'Samples', 'Total required', 'Paid', 'Remaining', 'Delayed payment', 'Notes',
    'LDM income', 'Entered − LDM', 'Accessions', 'Tests', 'Not verified', 'Within 3 Hours', 'After 3 Hours',
    'Actual income', 'Actual paid', 'Actual remaining', 'Actual delayed payment', 'Revision notes', 'Revised by', 'Revised at'];
  private exportRows(): SheetCell[][] {
    const k = this.k();
    const body = this.filtered().map((r) => [r.labName, r.labDisplayCode, r.area ?? '', r.repName, ddmy(r.date), r.enteredAt ? ddmy(r.enteredAt, true) : '', r.samples, money(r.totalRequired), money(r.paid), money(r.remaining), money(r.delayedPayment), r.notes ?? '',
      money(r.ldmIncome), this.varianceOf(r), r.accessions, r.tests, r.notVerified, r.addedWithin3h, r.addedAfter3h,
      r.actualIncome ?? '', r.actualPaid ?? '', r.actualRemaining ?? '', r.actualDelayedPayment ?? '', r.revisionNotes ?? '', r.revisedBy ?? '', r.revisedAt ? ddmy(r.revisedAt, true) : '']);
    body.push(['Total', '', '', '', '', '', k.samples, k.required, k.paid, k.remaining, k.delayed, '', k.ldm, k.variance, k.accessions, k.tests, k.notVerified, k.within, k.after, k.actualIncome, k.actualPaid, k.actualRemaining, k.actualDelayed, '', '', '']);
    return body;
  }
  exportExcel(): void { exportXlsx(`rep-income-revision-${localToday()}.xlsx`, RepIncomeRevisionComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Rep Income Revision (${ddmy(this.from)} → ${ddmy(this.to)})`, RepIncomeRevisionComponent.HEADER, this.exportRows() as (string | number)[][]); }
}
