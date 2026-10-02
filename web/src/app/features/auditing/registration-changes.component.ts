import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, exportXlsx, localToday, printTable, SheetCell } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { I18nService, TranslatePipe } from '../../core/i18n';
import { RegistrationChange } from '../../core/models';
import { firstOfMonth } from '../accounting/accounting.util';

const DASH = '—';
const NOLAB = 'No lab';
type DelayBucket = 'same_hour' | 'same_day' | 'within_week' | 'later' | 'unknown';

/**
 * Auditing → Registration Changes (2026-09-30): the LDM REG_LOG audit — every edit made to a registration (which column,
 * old → new value, who, when) beside the registration it touched (accession, patient, creation time, lab) and how long
 * after the registration was created the edit came. Synced nightly (yesterday's edits) with a manual sync for any
 * modification-date window; filters by registration-date range, modification-date range, change type, user, lab,
 * geography, delay and accession; the cards and the summary row follow the filtered lines.
 */
@Component({
  selector: 'app-registration-changes',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'auditing' | t : 'Auditing' }} / {{ 'reg_changes' | t : 'Registration Changes' }}</div><h1>{{ 'reg_changes' | t : 'Registration Changes' }}</h1></div>
      <div class="pagehead-actions">
        <button class="btn btn-s" [disabled]="syncing()" (click)="openSync()" title="{{ 'sync_reg_changes_hint' | t : 'Pull registration changes from Oracle for a modification-date range' }}">
          <i data-lucide="database" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
        </button>
        <button class="btn btn-s" [disabled]="!filtered().length" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" [disabled]="!filtered().length" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>
    <div class="small muted" style="margin-bottom:10px">{{ 'reg_changes_hint' | t : 'Every edit made to a registration in LDM (REG_LOG): the changed field, the old and new value, who made it and how long after the registration was created. Yesterday\\'s edits sync automatically every night.' }}</div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:12px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'changes' | t : 'Changes' }}</div><div class="val">{{ k().changes | number:'1.0-0' }}</div><div class="sub">{{ k().registrations | number:'1.0-0' }} {{ 'registrations' | t : 'registrations' }} · {{ k().perReg | number:'1.1-1' }} {{ 'per_registration' | t : 'per registration' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'change_types' | t : 'Change types' }}</div><div class="val">{{ k().types }}</div><div class="sub">{{ 'top_change_type' | t : 'Most edited' }}: {{ k().topType || DASH }} ({{ k().topTypeCount | number:'1.0-0' }})</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'modified_by_users' | t : 'Users who edited' }}</div><div class="val">{{ k().users }}</div><div class="sub">{{ 'top_user' | t : 'Most active' }}: {{ k().topUser || DASH }} ({{ k().topUserCount | number:'1.0-0' }})</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'labs' | t : 'Labs' }}</div><div class="val">{{ k().labs }}</div><div class="sub">{{ k().noLab | number:'1.0-0' }} {{ 'changes_no_lab' | t : 'changes on registrations without a lab' }}</div></div>
    </div>
    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'avg_delay' | t : 'Average delay after registration' }}</div><div class="val">{{ delayText(k().avgDelay) }}</div><div class="sub">{{ 'median' | t : 'Median' }} {{ delayText(k().medianDelay) }} · {{ 'max' | t : 'Max' }} {{ delayText(k().maxDelay) }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'delay_same_day' | t : 'Edited the same day' }}</div><div class="val">{{ k().sameDay | number:'1.0-0' }}</div><div class="sub">{{ (k().changes ? 100 * k().sameDay / k().changes : 0) | number:'1.0-0' }}% · {{ k().sameHour | number:'1.0-0' }} {{ 'delay_same_hour' | t : 'within an hour' }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'delay_later' | t : 'Edited on a later day' }}</div><div class="val">{{ k().later | number:'1.0-0' }}</div><div class="sub">{{ (k().changes ? 100 * k().later / k().changes : 0) | number:'1.0-0' }}% · {{ k().afterWeek | number:'1.0-0' }} {{ 'delay_after_week' | t : 'after more than a week' }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">{{ 'patient_name_edits' | t : 'Patient name edits' }}</div><div class="val">{{ k().nameEdits | number:'1.0-0' }}</div><div class="sub">{{ (k().changes ? 100 * k().nameEdits / k().changes : 0) | number:'1.0-0' }}% {{ 'of_changes' | t : 'of changes' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'reg_date' | t : 'Reg Date' }} {{ 'from' | t : 'from' }}</label><app-date-input [(ngModel)]="regFrom"></app-date-input></div>
        <div class="field"><label>{{ 'reg_date' | t : 'Reg Date' }} {{ 'to' | t : 'to' }}</label><app-date-input [(ngModel)]="regTo"></app-date-input></div>
        <div class="field"><label>{{ 'modification_date' | t : 'Modification date' }} {{ 'from' | t : 'from' }}</label><app-date-input [(ngModel)]="modFrom"></app-date-input></div>
        <div class="field"><label>{{ 'modification_date' | t : 'Modification date' }} {{ 'to' | t : 'to' }}</label><app-date-input [(ngModel)]="modTo"></app-date-input></div>
        <div class="field"><label>{{ 'change_type' | t : 'Change type' }}</label><app-filter-select [multiple]="true" [options]="types()" [ngModel]="type()" (ngModelChange)="type.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'modified_by' | t : 'Modified by' }}</label><app-filter-select [multiple]="true" [options]="users()" [ngModel]="user()" (ngModelChange)="user.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><app-filter-select [multiple]="true" [options]="govs()" [ngModel]="gov()" (ngModelChange)="gov.set($event); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [multiple]="true" [options]="areas()" [ngModel]="area()" (ngModelChange)="area.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'lab_name' | t : 'Lab name' }}</label><app-filter-select [multiple]="true" [options]="labNames()" [ngModel]="lab()" (ngModelChange)="lab.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'delay_after_reg' | t : 'Delay after registration' }}</label>
          <select class="select" [ngModel]="delay()" (ngModelChange)="delay.set($event)">
            <option value="">{{ 'all' | t : 'All' }}</option><option value="same_hour">{{ 'delay_same_hour' | t : 'Within an hour' }}</option><option value="same_day">{{ 'delay_same_day' | t : 'Same day' }}</option><option value="within_week">{{ 'delay_within_week' | t : 'Later, within a week' }}</option><option value="later">{{ 'delay_after_week' | t : 'After more than a week' }}</option><option value="unknown">{{ 'unknown' | t : 'Unknown' }}</option>
          </select></div>
        <div class="field"><label>{{ 'acc_no' | t : 'Accession' }} / {{ 'patient_name' | t : 'Patient' }}</label><input class="input" [ngModel]="search()" (ngModelChange)="search.set($event)" placeholder="{{ 'search' | t : 'Search' }}"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      <div class="small muted" style="margin-top:8px">{{ 'reg_changes_filter_hint' | t : 'Choose a registration-date range, a modification-date range, or both (each at most one year); the other filters narrow the loaded lines.' }}</div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>#</th><th class="stick">{{ 'modified_at' | t : 'Modified at' }}</th><th>{{ 'modified_by' | t : 'Modified by' }}</th><th>{{ 'acc_no' | t : 'Accession' }}</th><th>{{ 'patient_name' | t : 'Patient' }}</th>
            <th>{{ 'reg_created' | t : 'Reg Created' }}</th><th>{{ 'reg_date' | t : 'Reg Date' }}</th><th class="r">{{ 'delay' | t : 'Delay' }}</th><th>{{ 'lab_name' | t : 'Lab' }}</th><th>{{ 'reg_branch' | t : 'Reg branch' }}</th>
            <th>{{ 'change_type' | t : 'Change type' }}</th><th>{{ 'old_value' | t : 'Old value' }}</th><th>{{ 'new_value' | t : 'New value' }}</th>
          </tr></thead>
          <tbody>
            @for (r of paged(); track r.transId; let i = $index) {
              <tr [class.late]="bucket(r) === 'later' || bucket(r) === 'within_week'">
                <td class="mono">{{ pageStart() + i + 1 }}</td>
                <td class="stick mono" style="white-space:nowrap">{{ ddmy(r.modifiedAt, true) }}</td>
                <td>{{ r.modifiedBy }}</td><td class="mono">{{ r.accNo }}</td><td>{{ r.patientName }}</td>
                <td class="mono small">{{ r.regCreatedAt ? ddmy(r.regCreatedAt, true) : DASH }}</td><td class="mono">{{ r.regDate ? ddmy(r.regDate) : DASH }}</td>
                <td class="r mono" [class.neg]="bucket(r) === 'later'" [class.late-note]="bucket(r) === 'within_week'">{{ delayText(r.delayMinutes) }}</td>
                <td>{{ r.labName ?? r.labCode ?? NOLAB }}<div class="small muted">{{ r.area || DASH }}</div></td><td class="small">{{ r.regBranch || DASH }}</td>
                <td><span class="badge b-info">{{ r.column }}</span></td>
                <td class="val old">{{ r.oldValue || DASH }}</td><td class="val new">{{ r.newValue || DASH }}</td>
              </tr>
            } @empty { <tr><td colspan="13" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (filtered().length) {
            <tfoot><tr><td colspan="3">{{ 'total' | t : 'Total' }} · {{ k().changes | number:'1.0-0' }} {{ 'changes' | t : 'changes' }}</td><td colspan="2">{{ k().registrations | number:'1.0-0' }} {{ 'registrations' | t : 'registrations' }} · {{ k().users }} {{ 'reg_users' | t : 'users' }}</td><td colspan="2"></td><td class="r mono">{{ delayText(k().avgDelay) }}</td><td colspan="2">{{ k().labs }} {{ 'labs' | t : 'labs' }}</td><td>{{ k().types }} {{ 'change_types' | t : 'change types' }}</td><td colspan="2"></td></tr></tfoot>
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

    @if (syncOpen()) {
      <div class="rc-overlay" (click)="syncOpen.set(false)">
        <div class="rc-dlg" (click)="$event.stopPropagation()">
          <div class="rc-dlg-head"><h2>{{ 'sync_oracle' | t : 'Sync from Oracle' }}</h2><button class="btn btn-mini btn-s" (click)="syncOpen.set(false)">✕</button></div>
          <div style="padding:16px">
            <div class="small muted" style="margin-bottom:12px">{{ 'sync_range_hint_reg_changes' | t : 'Pull the registration changes MADE in this modification-date range from Oracle and replace the stored data for it (at most three months at a time). Yesterday is also synced automatically every night.' }}</div>
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="syncFrom"></app-date-input></div>
              <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="syncTo"></app-date-input></div>
            </div>
          </div>
          <div class="rc-dlg-foot">
            <button class="btn btn-s" (click)="syncOpen.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="syncing()" (click)="runSync()">{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync' | t : 'Sync') }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    th.r,td.r{text-align:right}
    .stick{position:sticky;inset-inline-start:0;z-index:1} td.stick{background:var(--white,#fff)} thead .stick{z-index:2}
    td.val{max-width:260px;white-space:pre-wrap;word-break:break-word} td.old{color:#b91c1c;text-decoration:line-through} td.new{color:#15803d;font-weight:600}
    tr.late td.stick{border-inline-start:3px solid #f97316}
    .neg{color:#b91c1c} .late-note{color:#1d4ed8;font-weight:600}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150,#edebe9);font-size:12.5px;color:var(--slate-700,#605e5c)}
    .rc-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .rc-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,460px)}
    .rc-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)} .rc-dlg-head h2{font-size:15px;margin:0}
    .rc-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
  `],
})
export class RegistrationChangesComponent {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  readonly ddmy = ddmy;
  readonly DASH = DASH;
  readonly NOLAB = NOLAB;

  readonly loading = signal(false);
  readonly rows = signal<RegistrationChange[]>([]);
  private readonly today = localToday();
  /** Default: this month's modifications; the registration-date range is optional. */
  regFrom = ''; regTo = ''; modFrom = firstOfMonth(); modTo = this.today;
  readonly type = signal<string[]>([]);
  readonly user = signal<string[]>([]);
  readonly gov = signal<string[]>([]);
  readonly area = signal<string[]>([]);
  readonly lab = signal<string[]>([]);
  readonly delay = signal('');
  readonly search = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(100);
  readonly syncOpen = signal(false);
  readonly syncing = signal(false);
  syncFrom = ''; syncTo = '';

  readonly types = computed(() => [...new Set(this.rows().map((r) => r.column))].sort());
  readonly users = computed(() => [...new Set(this.rows().map((r) => r.modifiedBy))].sort());
  readonly govs = computed(() => [...new Set(this.rows().map((r) => r.governorate ?? NOLAB))].sort());
  readonly areas = computed(() => [...new Set(this.rows().filter((r) => !this.gov().length || this.gov().includes(r.governorate ?? NOLAB)).map((r) => r.area ?? DASH))].sort());
  readonly labNames = computed(() => [...new Set(this.rows().map((r) => r.labName ?? r.labCode ?? NOLAB))].sort());

  constructor() { this.load(); }

  /** Delay buckets: within an hour / same day (≤ 24 h) / later within a week / after a week / unknown. */
  bucket(r: RegistrationChange): DelayBucket {
    const m = r.delayMinutes;
    if (m === null || m === undefined) return 'unknown';
    if (m <= 60) return 'same_hour';
    if (m <= 1440) return 'same_day';
    if (m <= 10080) return 'within_week';
    return 'later';
  }
  delayText(minutes: number | null | undefined): string {
    if (minutes === null || minutes === undefined || Number.isNaN(minutes)) return DASH;
    if (minutes < 60) return `${Math.round(minutes)} ${this.i18n.t('min_short', 'min')}`;
    if (minutes < 1440) return `${(minutes / 60).toFixed(1)} ${this.i18n.t('hour_short', 'h')}`;
    return `${(minutes / 1440).toFixed(1)} ${this.i18n.t('day_short', 'd')}`;
  }

  private matches(r: RegistrationChange): boolean {
    const s = this.search().trim().toLowerCase();
    return (!this.type().length || this.type().includes(r.column)) &&
      (!this.user().length || this.user().includes(r.modifiedBy)) &&
      (!this.gov().length || this.gov().includes(r.governorate ?? NOLAB)) &&
      (!this.area().length || this.area().includes(r.area ?? DASH)) &&
      (!this.lab().length || this.lab().includes(r.labName ?? r.labCode ?? NOLAB)) &&
      (!this.delay() || this.bucket(r) === this.delay()) &&
      (!s || r.accNo.toLowerCase().includes(s) || r.patientName.toLowerCase().includes(s));
  }
  /** Filtered lines, newest modification first (the served order). */
  readonly filtered = computed<RegistrationChange[]>(() => this.rows().filter((r) => this.matches(r)));
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize())));
  readonly curPage = computed(() => Math.min(this.page(), this.pageCount()));
  readonly pageStart = computed(() => (this.curPage() - 1) * this.pageSize());
  readonly paged = computed<RegistrationChange[]>(() => this.filtered().slice(this.pageStart(), this.pageStart() + this.pageSize()));

  /** Cards + summary row over the filtered lines. */
  readonly k = computed(() => {
    const f = this.filtered();
    const count = (fn: (r: RegistrationChange) => string) => { const m = new Map<string, number>(); for (const r of f) m.set(fn(r), (m.get(fn(r)) ?? 0) + 1); return m; };
    const top = (m: Map<string, number>) => [...m.entries()].sort((a, b) => b[1] - a[1])[0] ?? ['', 0];
    const byType = count((r) => r.column); const byUser = count((r) => r.modifiedBy);
    const delays = f.map((r) => r.delayMinutes).filter((d): d is number => d !== null && d !== undefined).sort((a, b) => a - b);
    const registrations = new Set(f.map((r) => r.regKey)).size;
    return {
      changes: f.length, registrations, perReg: registrations ? f.length / registrations : 0,
      types: byType.size, topType: top(byType)[0], topTypeCount: top(byType)[1],
      users: byUser.size, topUser: top(byUser)[0], topUserCount: top(byUser)[1],
      labs: new Set(f.filter((r) => r.labCode).map((r) => r.labCode)).size, noLab: f.filter((r) => !r.labCode).length,
      avgDelay: delays.length ? delays.reduce((a, d) => a + d, 0) / delays.length : null,
      medianDelay: delays.length ? delays[Math.floor(delays.length / 2)] : null, maxDelay: delays.length ? delays[delays.length - 1] : null,
      sameHour: f.filter((r) => this.bucket(r) === 'same_hour').length,
      sameDay: f.filter((r) => this.bucket(r) === 'same_hour' || this.bucket(r) === 'same_day').length,
      later: f.filter((r) => this.bucket(r) === 'within_week' || this.bucket(r) === 'later').length,
      afterWeek: f.filter((r) => this.bucket(r) === 'later').length,
      nameEdits: f.filter((r) => /patient\s*name/i.test(r.column)).length,
    };
  });

  load(): void {
    const hasReg = !!this.regFrom && !!this.regTo; const hasMod = !!this.modFrom && !!this.modTo;
    if (!hasReg && !hasMod) { this.toast.warning(this.i18n.t('reg_changes_range_required', 'Choose a registration-date range or a modification-date range.')); return; }
    this.loading.set(true); this.page.set(1);
    const params: Record<string, string> = {};
    if (hasReg) { params['regFrom'] = this.regFrom; params['regTo'] = this.regTo; }
    if (hasMod) { params['modFrom'] = this.modFrom; params['modTo'] = this.modTo; }
    this.api.get<RegistrationChange[]>('/registration-changes', params).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => { this.rows.set([]); this.loading.set(false); },
    });
  }
  openSync(): void {
    const y = new Date(); y.setDate(y.getDate() - 1);
    this.syncFrom = `${y.getFullYear()}-${String(y.getMonth() + 1).padStart(2, '0')}-${String(y.getDate()).padStart(2, '0')}`; this.syncTo = this.today;
    this.syncOpen.set(true);
  }
  runSync(): void {
    if (!this.syncFrom || !this.syncTo) { this.toast.warning('Please choose a start and end date.'); return; }
    if (this.syncFrom > this.syncTo) { this.toast.warning('The start date must be on or before the end date.'); return; }
    this.syncing.set(true);
    this.api.post<{ statsUpserted: number }>('/registration-changes/sync', { from: this.syncFrom, to: this.syncTo }).subscribe({
      next: (r) => {
        this.syncing.set(false); this.syncOpen.set(false);
        this.toast.success(`Synced from Oracle: ${r.statsUpserted} registration change(s) modified ${this.syncFrom} → ${this.syncTo}.`);
        if (!this.modFrom || this.syncFrom < this.modFrom) this.modFrom = this.syncFrom;
        if (!this.modTo || this.syncTo > this.modTo) this.modTo = this.syncTo;
        this.load();
      },
      error: () => this.syncing.set(false),
    });
  }

  private static readonly HEADER = ['Modified at', 'Modified by', 'Acc No', 'Patient', 'Reg created', 'Reg date', 'Delay', 'Lab', 'Lab code', 'Area', 'Reg branch', 'Change type', 'Old value', 'New value'];
  private exportRows(): SheetCell[][] {
    return this.filtered().map((r) => [ddmy(r.modifiedAt, true), r.modifiedBy, r.accNo, r.patientName, r.regCreatedAt ? ddmy(r.regCreatedAt, true) : '', r.regDate ? ddmy(r.regDate) : '',
      this.delayText(r.delayMinutes), r.labName ?? r.labCode ?? NOLAB, r.labCode ?? '', r.area ?? '', r.regBranch ?? '', r.column, r.oldValue ?? '', r.newValue ?? '']);
  }
  exportExcel(): void { exportXlsx(`registration-changes-${this.today}.xlsx`, RegistrationChangesComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable('Registration changes', RegistrationChangesComponent.HEADER, this.exportRows() as (string | number)[][]); }
}
