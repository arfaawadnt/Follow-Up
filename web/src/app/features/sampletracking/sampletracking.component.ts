import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { SampleLifecycleRow, SampleTracking, UserLookup } from '../../core/models';
import { I18nService, TranslatePipe } from '../../core/i18n';
import { exportXlsx, printTable, localToday, localDateTime, ddmy } from '../../shared/export.util';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ToastService } from '../../core/toast.service';

interface City { id: string; name: string; governorate: string; }
interface AreaRef { id: string; name: string; cityId: string; }
interface Draft { count: number; dataEntryUser: string; reviewUser: string; sortUser: string; notes: string; dirty: boolean; }

@Component({
  selector: 'app-sampletracking',
  standalone: true,
  imports: [FormsModule, TranslatePipe, AppDatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div>
        <div class="breadcrumbs">Home / {{ 'sample_lifecycle_tracking' | t : 'Sample Lifecycle Tracking' }}</div>
        <h1>{{ 'sample_lifecycle_tracking' | t : 'Sample Lifecycle Tracking' }}</h1>
        <p class="muted" style="margin:4px 0 0;font-size:12.5px">{{ 'manage_regional_sample_assignments_and_mon' | t : 'Manage regional sample assignments and monitor the 6 key lifecycle stages' }}</p>
      </div>
    </div>

    <div class="tabbar">
      <button class="tab" [class.on]="tab() === 'assignments'" (click)="tab.set('assignments')">{{ 'area_assignments' | t : 'Area Assignments' }}</button>
      <button class="tab" [class.on]="tab() === 'report'" (click)="setReport()">{{ 'lifecycle_tracking_report' | t : 'Lifecycle Tracking Report' }}</button>
    </div>

    <!-- ===================== Area Assignments ===================== -->
    @if (tab() === 'assignments') {
      <div class="card" style="padding:20px;margin-bottom:16px">
        <h3 style="margin:0 0 12px;font-size:14px">{{ 'area_assignment_filtration' | t : 'Area Assignment Filtration' }}</h3>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>{{ 'start_date' | t : 'Start Date' }}</label><app-date-input [(ngModel)]="start"></app-date-input></div>
          <div class="field"><label>{{ 'end_date' | t : 'End Date' }}</label><app-date-input [(ngModel)]="end"></app-date-input></div>
          <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label>
            <app-filter-select [multiple]="true" [options]="govOptions()" [(ngModel)]="gov" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
          <div class="field"><label>{{ 'city_2' | t : 'City' }}</label>
            <app-filter-select [multiple]="true" [options]="cityOptions()" [(ngModel)]="city" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
        </div>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
          <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label>
            <app-filter-select [multiple]="true" [options]="areaOptions()" [(ngModel)]="areaFilter" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
          <div class="field"><label>{{ 'search_area' | t : 'Search Area' }}</label><input class="input" [(ngModel)]="search" [placeholder]="'search_area_placeholder' | t : 'Search area...'"></div>
          <div class="field"><label>{{ 'data_entry' | t : 'Data Entry' }}</label>
            <app-filter-select [options]="userOptions()" [(ngModel)]="fDataEntry" [allValue]="'All'" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
          <div class="field"><label>{{ 'reviewer' | t : 'Reviewer' }}</label>
            <app-filter-select [options]="userOptions()" [(ngModel)]="fReview" [allValue]="'All'" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
        </div>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
          <div class="field"><label>{{ 'sorted_by' | t : 'Sorted by' }}</label>
            <app-filter-select [options]="userOptions()" [(ngModel)]="fSort" [allValue]="'All'" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
          <div class="field"><label>{{ 'status' | t : 'Status' }}</label>
            <select class="select" [(ngModel)]="fStatus"><option value="Pending">{{ 'pending_2' | t : 'Pending' }}</option><option value="Completed">{{ 'completed' | t : 'Completed' }}</option><option value="All">{{ 'all_2' | t : 'All' }}</option></select></div>
          <div class="field" style="align-self:end"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
          <div class="field" style="align-self:end"><button class="btn btn-s" (click)="reset()" style="height:36px">{{ 'reset' | t : 'Reset' }}</button></div>
        </div>
      </div>

      <div class="card" style="padding:0;overflow:hidden">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:12px 16px;border-bottom:1px solid var(--slate-150)">
          <b style="font-size:13px">{{ 'sample_area_assignment' | t : 'Sample Area Assignment' }}</b>
          <div style="display:flex;gap:8px">
            @if (auth.has('SampleTracking')) { <button class="btn btn-p btn-mini" [disabled]="busy() || dirtyCount() === 0" (click)="batchSave()">{{ 'batch_save_all' | t : 'Batch Save All' }} ({{ dirtyCount() }})</button> }
          </div>
        </div>
        @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
            <thead><tr><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'area_2' | t : 'Area' }}</th><th>{{ 'samples' | t : 'Samples' }}</th><th>{{ 'data_entry' | t : 'Data Entry' }}</th><th>{{ 'reviewed_by' | t : 'Reviewed By' }}</th><th>{{ 'sorted_by_2' | t : 'Sorted By' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th>{{ 'actions' | t : 'Actions' }}</th></tr></thead>
            <tbody>
              @for (r of filtered(); track r.id) {
                <tr>
                  <td class="mono small">{{ r.date | appDate }}</td>
                  <td><b>{{ r.area }}</b></td>
                  <td class="mono" style="font-weight:700">{{ r.count }}</td>
                  <td><select class="select sel" [ngModel]="draft(r).dataEntryUser" (ngModelChange)="setD(r, 'dataEntryUser', $event)" [disabled]="!auth.has('SampleTracking')">
                    <option value="">{{ 'unassigned' | t : 'Unassigned' }}</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select>
                    @if (r.dataEntryAt) { <div class="small muted mono">{{ fmt(r.dataEntryAt) }}</div> }</td>
                  <td><select class="select sel" [ngModel]="draft(r).reviewUser" (ngModelChange)="setD(r, 'reviewUser', $event)" [disabled]="!auth.has('SampleTracking') || !draft(r).dataEntryUser">
                    <option value="">{{ 'unassigned' | t : 'Unassigned' }}</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select>
                    @if (r.reviewAt) { <div class="small muted mono">{{ fmt(r.reviewAt) }}</div> }</td>
                  <td><select class="select sel" [ngModel]="draft(r).sortUser" (ngModelChange)="setD(r, 'sortUser', $event)" [disabled]="!auth.has('SampleTracking') || !draft(r).reviewUser">
                    <option value="">{{ 'unassigned' | t : 'Unassigned' }}</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select>
                    @if (r.sortAt) { <div class="small muted mono">{{ fmt(r.sortAt) }}</div> }</td>
                  <td><input class="input" style="min-width:140px" [ngModel]="draft(r).notes" (ngModelChange)="setD(r, 'notes', $event)" [disabled]="!auth.has('SampleTracking')"></td>
                  <td>@if (auth.has('SampleTracking')) { <button class="btn btn-mini btn-p" [disabled]="busy() || !draft(r).dirty" (click)="saveRow(r)">{{ 'save_2' | t : 'Save' }}</button> }</td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_records_matching_filters' | t : 'No records matching filters' }}</td></tr> }
            </tbody>
          </table></div>
        }
      </div>
    }

    <!-- ===================== Lifecycle Tracking Report ===================== -->
    @if (tab() === 'report') {
      <div class="card" style="padding:20px;margin-bottom:16px">
        <h3 style="margin:0 0 12px;font-size:14px">{{ 'report_filtration' | t : 'Report Filtration' }}</h3>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>{{ 'start_date' | t : 'Start Date' }}</label><app-date-input [(ngModel)]="rStart"></app-date-input></div>
          <div class="field"><label>{{ 'end_date' | t : 'End Date' }}</label><app-date-input [(ngModel)]="rEnd"></app-date-input></div>
          <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label>
            <app-filter-select [options]="reportAreas()" [(ngModel)]="rArea" [allValue]="'All'" [placeholder]="'all_2' | t : 'All'"></app-filter-select></div>
          <div class="field"><label>{{ 'group_by' | t : 'Group By' }}</label>
            <select class="select" [(ngModel)]="groupBy"><option value="Area">{{ 'area_2' | t : 'Area' }}</option><option value="Laboratory">{{ 'laboratory' | t : 'Laboratory' }}</option></select></div>
        </div>
        <div style="display:flex;gap:8px;margin-top:12px">
          <button class="btn btn-p" (click)="loadReport()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button>
          <button class="btn btn-s" (click)="resetReport()" style="height:36px">{{ 'reset' | t : 'Reset' }}</button>
        </div>
      </div>

      <div class="card" style="padding:0;overflow:hidden">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:12px 16px;border-bottom:1px solid var(--slate-150)">
          <b style="font-size:13px">{{ 'sample_life_cycle_history' | t : 'Sample Life Cycle History' }}</b>
          <div style="display:flex;gap:8px">
            <button class="btn btn-s btn-mini" (click)="exportReportExcel()" [disabled]="!reportFiltered().length">{{ 'export_excel' | t : 'Export Excel' }}</button>
            <button class="btn btn-s btn-mini" (click)="exportLifecyclePdf()" [disabled]="!reportFiltered().length">{{ 'lifecycle_pdf' | t : 'Lifecycle PDF' }}</button>
            <button class="btn btn-s btn-mini" (click)="exportMotionPdf()" [disabled]="!reportFiltered().length">{{ 'motion_tracking_pdf' | t : 'Motion Tracking PDF' }}</button>
          </div>
        </div>
        @if (reportLoading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          @for (grp of reportGroups(); track grp.key) {
            <div style="background:var(--slate-100);padding:8px 16px;font-weight:700;font-size:12.5px;border-bottom:1px solid var(--slate-150)">{{ (groupBy === 'Area' ? 'area_2' : 'laboratory') | t : groupBy }}: {{ grp.key }}</div>
            <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
              <thead><tr><th>{{ 'laboratory' | t : 'Laboratory' }}</th><th>{{ 'visit_datetime' | t : 'Visit datetime' }}</th><th>{{ 'samples' | t : 'Samples' }}</th><th>{{ 'collected' | t : 'Collected' }}</th><th>{{ 'transferred' | t : 'Transferred' }}</th><th>{{ 'received' | t : 'Received' }}</th><th>{{ 'data_entry' | t : 'Data entry' }}</th><th>{{ 'revised' | t : 'Revised' }}</th><th>{{ 'sorted' | t : 'Sorted' }}</th><th>{{ 'notes' | t : 'Notes' }}</th></tr></thead>
              <tbody>
                @for (r of grp.rows; track $index) {
                  <tr>
                    <td><b>{{ r.lab }}</b><div class="small muted">{{ r.labDisplayCode }}</div></td>
                    <td class="mono small">{{ r.visitDate | appDate }} {{ r.visitTime }}</td>
                    <td class="mono" style="font-weight:700">{{ r.samples ?? '—' }}</td>
                    <td class="small">{{ r.collectorName ?? '—' }}<div class="mono small muted">{{ fmt(r.collectedAt) }}</div></td>
                    <td class="small">{{ r.transferRepName ?? '—' }}<div class="mono small muted">{{ fmt(r.transferredAt) }}</div>
                      @if (r.driverName) { <div class="small muted">{{ r.driverName }} ({{ r.carPlate ?? '—' }} · {{ r.driverMobile ?? '—' }})</div> }</td>
                    <td class="mono small">{{ fmt(r.receivedAt) }}</td>
                    <td class="small">{{ stage(r.dataEntryBy, r.dataEntryAt) }}</td>
                    <td class="small">{{ stage(r.reviewBy, r.reviewAt) }}</td>
                    <td class="small">{{ stage(r.sortBy, r.sortAt) }}</td>
                    <td class="small">{{ r.notes ?? '—' }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          } @empty { <div class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records found' }}</div> }
        }
      </div>
    }
  `,
  styles: [`
    .tabbar { display:flex; gap:6px; margin-bottom:16px }
    .tab { background:var(--white); border:1px solid var(--slate-300); color:var(--slate-700); border-radius:var(--r-btn); padding:8px 16px; font:600 12.5px var(--ui); cursor:pointer }
    .tab.on { background:var(--primary-blue); color:#fff; border-color:var(--primary-blue) }
    .field label { display:block; font:600 11px var(--ui); color:var(--slate-600); margin-bottom:4px }
    .num { width:80px } .sel { min-width:130px; padding:4px 8px }
    .muted { color:var(--slate-500) }
  `],
})
export class SampleTrackingComponent {
  private readonly api = inject(ApiService);
  private readonly i18n = inject(I18nService);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly reportLoading = signal(false);
  readonly busy = signal(false);
  readonly tab = signal<'assignments' | 'report'>('assignments');
  readonly items = signal<SampleTracking[]>([]);
  readonly report = signal<SampleLifecycleRow[]>([]);
  readonly users = signal<UserLookup[]>([]);
  readonly cities = signal<City[]>([]);
  readonly areaRefs = signal<AreaRef[]>([]);
  private readonly drafts = new Map<string, Draft>();
  private reportLoaded = false;

  // Both tabs default to today, like the reference (its local-date variant); older days via the range filter.
  private readonly today = localToday();
  start = this.today; end = this.today;
  gov: string[] = []; city: string[] = []; areaFilter: string[] = []; search = '';
  fDataEntry = 'All'; fReview = 'All'; fSort = 'All'; fStatus = 'All';
  rStart = this.today; rEnd = this.today; rArea = 'All'; groupBy: 'Area' | 'Laboratory' = 'Area';

  constructor() {
    this.load();
    this.api.get<UserLookup[]>('/users/lookup').subscribe({ next: (u) => this.users.set(u) });
    this.api.get<City[]>('/setup/cities').subscribe({ next: (c) => this.cities.set(c) });
    this.api.get<AreaRef[]>('/setup/areas').subscribe({ next: (a) => this.areaRefs.set(a) });
  }

  // Area name -> { city, governorate } via the setup reference data.
  private geo(area: string): { city: string | null; gov: string | null } {
    const a = this.areaRefs().find((x) => x.name === area);
    const c = a ? this.cities().find((x) => x.id === a.cityId) : undefined;
    return { city: c?.name ?? null, gov: c?.governorate ?? null };
  }
  govOptions(): string[] { return [...new Set(this.cities().map((c) => c.governorate))].sort(); }
  cityOptions(): string[] { return [...new Set(this.cities().filter((c) => !this.gov.length || this.gov.includes(c.governorate)).map((c) => c.name))].sort(); }
  areaOptions(): string[] { return [...new Set(this.items().map((i) => i.area))].sort(); }
  readonly userOptions = computed(() => [
    { value: '', label: this.i18n.t('unassigned', 'Unassigned') },
    ...this.users().map((u) => ({ value: u.username, label: u.username })),
  ]);

  readonly filtered = computed(() => {
    const q = this.search.trim().toLowerCase();
    return this.items().filter((r) => {
      const g = this.geo(r.area);
      return (!this.gov.length || this.gov.includes(g.gov ?? '')) &&
        (!this.city.length || this.city.includes(g.city ?? '')) &&
        (!this.areaFilter.length || this.areaFilter.includes(r.area)) &&
        (!q || r.area.toLowerCase().includes(q)) &&
        (this.fDataEntry === 'All' || (r.dataEntryBy ?? '') === this.fDataEntry) &&
        (this.fReview === 'All' || (r.reviewBy ?? '') === this.fReview) &&
        (this.fSort === 'All' || (r.sortBy ?? '') === this.fSort) &&
        (this.fStatus === 'All' || (this.fStatus === 'Completed') === r.isComplete);
    });
  });

  draft(r: SampleTracking): Draft {
    let d = this.drafts.get(r.id);
    if (!d) {
      d = { count: r.count, dataEntryUser: r.dataEntryBy ?? '', reviewUser: r.reviewBy ?? '', sortUser: r.sortBy ?? '', notes: r.notes ?? '', dirty: false };
      this.drafts.set(r.id, d);
    }
    return d;
  }
  setD(r: SampleTracking, key: 'count' | 'dataEntryUser' | 'reviewUser' | 'sortUser' | 'notes', value: string | number): void {
    const d = this.draft(r);
    if (key === 'count') d.count = Number(value) || 0;
    else d[key] = String(value ?? '');
    if (key === 'dataEntryUser' && !d.dataEntryUser) { d.reviewUser = ''; d.sortUser = ''; }
    if (key === 'reviewUser' && !d.reviewUser) { d.sortUser = ''; }
    d.dirty = true;
  }
  dirtyCount(): number { return [...this.drafts.values()].filter((d) => d.dirty).length; }
  fmt(iso: string | null): string { return localDateTime(iso); }
  stage(by: string | null, at: string | null): string { return by ? `${by} · ${this.fmt(at)}` : '—'; }

  load(): void {
    this.loading.set(true);
    this.drafts.clear();
    this.api.get<SampleTracking[]>('/sample-tracking', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  reset(): void {
    this.start = this.today; this.end = this.today;
    this.gov = []; this.city = []; this.areaFilter = [];
    this.fDataEntry = this.fReview = this.fSort = this.fStatus = 'All';
    this.search = ''; this.load();
  }

  private line(r: SampleTracking): Record<string, unknown> {
    const d = this.draft(r);
    return {
      area: r.area, date: r.date, count: d.count || 0,
      dataEntryUser: d.dataEntryUser || null, reviewUser: d.reviewUser || null, sortUser: d.sortUser || null,
      notes: d.notes || null,
    };
  }

  private post(lines: Record<string, unknown>[]): void {
    if (!lines.length) return;
    this.busy.set(true);
    this.api.post('/sample-tracking/assignments', { lines }).subscribe({
      next: () => { this.toast.success('Assignments saved.'); this.busy.set(false); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  saveRow(r: SampleTracking): void { this.post([this.line(r)]); }
  batchSave(): void { this.post(this.items().filter((r) => this.drafts.get(r.id)?.dirty).map((r) => this.line(r))); }

  // ---- Report tab ----

  setReport(): void { this.tab.set('report'); if (!this.reportLoaded) this.loadReport(); }
  loadReport(): void {
    this.reportLoaded = true;
    this.reportLoading.set(true);
    this.api.get<SampleLifecycleRow[]>('/sample-tracking/lifecycle', { from: this.rStart, to: this.rEnd }).subscribe({
      next: (r) => { this.report.set(r); this.reportLoading.set(false); }, error: () => this.reportLoading.set(false),
    });
  }
  resetReport(): void { this.rStart = this.today; this.rEnd = this.today; this.rArea = 'All'; this.groupBy = 'Area'; this.loadReport(); }
  reportAreas(): string[] { return [...new Set(this.report().map((r) => r.area).filter((x): x is string => !!x))].sort(); }
  readonly reportFiltered = computed(() => this.report().filter((r) => this.rArea === 'All' || r.area === this.rArea));
  reportGroups(): { key: string; rows: SampleLifecycleRow[] }[] {
    const by = new Map<string, SampleLifecycleRow[]>();
    for (const r of this.reportFiltered()) {
      const key = this.groupBy === 'Area' ? (r.area ?? '—') : r.lab;
      (by.get(key) ?? by.set(key, []).get(key)!).push(r);
    }
    return [...by.entries()].map(([key, rows]) => ({ key, rows })).sort((a, b) => a.key.localeCompare(b.key));
  }

  // ---- Exports ----

  private driver(r: SampleLifecycleRow): string {
    return r.driverName ? `${r.driverName} (${r.carPlate ?? '—'} · ${r.driverMobile ?? '—'})` : '—';
  }
  private reportRows(): (string | number | null)[][] {
    return this.reportFiltered().map((r) => [r.lab, r.labDisplayCode, r.area, `${ddmy(r.visitDate)} ${r.visitTime}`, r.samples,
      this.stage(r.collectorName, r.collectedAt),
      `${this.stage(r.transferRepName, r.transferredAt)}${r.driverName ? ' — ' + this.driver(r) : ''}`,
      this.fmt(r.receivedAt),
      this.stage(r.dataEntryBy, r.dataEntryAt), this.stage(r.reviewBy, r.reviewAt), this.stage(r.sortBy, r.sortAt), r.notes]);
  }
  private static readonly REPORT_HEADER = ['Laboratory', 'Code', 'Area', 'Visit datetime', 'Samples', 'Collected', 'Transferred', 'Received', 'Data entry', 'Revised', 'Sorted', 'Notes'];
  exportReportExcel(): void { exportXlsx('sample-lifecycle.xlsx', SampleTrackingComponent.REPORT_HEADER, this.reportRows()); }
  exportLifecyclePdf(): void { printTable('Sample Life Cycle History', SampleTrackingComponent.REPORT_HEADER, this.reportRows()); }
  exportMotionPdf(): void {
    // Motion tracking mirrors the reference: who moved the samples at each leg, incl. the driver details.
    printTable('Sample Motion Tracking',
      ['Laboratory', 'Area', 'Visit', 'Collector', 'Transfer rep', 'Driver (car · mobile)', 'Stage timeline'],
      this.reportFiltered().map((r) => [r.lab, r.area, `${ddmy(r.visitDate)} ${r.visitTime}`,
        r.collectorName ?? '—', r.transferRepName ?? '—', this.driver(r),
        ['Collected ' + this.fmt(r.collectedAt), 'Transferred ' + this.fmt(r.transferredAt), 'Received ' + this.fmt(r.receivedAt),
         'Data entry ' + this.fmt(r.dataEntryAt), 'Revised ' + this.fmt(r.reviewAt), 'Sorted ' + this.fmt(r.sortAt)].join(' → ')]));
  }
}
