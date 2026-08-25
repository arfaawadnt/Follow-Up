import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { SampleLifecycleRow, SampleTracking, UserLookup } from '../../core/models';
import { exportCsv, printTable, localToday } from '../../shared/export.util';

interface City { id: string; name: string; governorate: string; }
interface AreaRef { id: string; name: string; cityId: string; }
interface Draft { count: number; dataEntryUser: string; reviewUser: string; sortUser: string; notes: string; dirty: boolean; }

@Component({
  selector: 'app-sampletracking',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  template: `
    <div class="pagehead">
      <div>
        <div class="breadcrumbs">Home / Sample Lifecycle Tracking</div>
        <h1>Sample Lifecycle Tracking</h1>
        <p class="muted" style="margin:4px 0 0;font-size:12.5px">Manage regional sample assignments and monitor the 6 key lifecycle stages</p>
      </div>
    </div>

    <div class="tabbar">
      <button class="tab" [class.on]="tab() === 'assignments'" (click)="tab.set('assignments')">Area Assignments</button>
      <button class="tab" [class.on]="tab() === 'report'" (click)="setReport()">Lifecycle Tracking Report</button>
    </div>

    <!-- ===================== Area Assignments ===================== -->
    @if (tab() === 'assignments') {
      <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
        <div class="kpi kpi-teal"><div class="lbl">Total received samples</div><div class="val">{{ totalSamples() | number:'1.0-0' }}</div></div>
        <div class="kpi kpi-green"><div class="lbl">Completed areas</div><div class="val">{{ completed() }}</div></div>
        <div class="kpi kpi-orange"><div class="lbl">Pending areas</div><div class="val">{{ filtered().length - completed() }}</div></div>
      </div>

      <div class="card" style="padding:20px;margin-bottom:16px">
        <h3 style="margin:0 0 12px;font-size:14px">Area Assignment Filtration</h3>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>Start Date</label><input type="date" class="input" [(ngModel)]="start"></div>
          <div class="field"><label>End Date</label><input type="date" class="input" [(ngModel)]="end"></div>
          <div class="field"><label>Governorate</label>
            <select class="select" [(ngModel)]="gov"><option value="All">All</option>@for (g of govOptions(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
          <div class="field"><label>City</label>
            <select class="select" [(ngModel)]="city"><option value="All">All</option>@for (c of cityOptions(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        </div>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
          <div class="field"><label>Area</label>
            <select class="select" [(ngModel)]="areaFilter"><option value="All">All</option>@for (a of areaOptions(); track a) { <option [value]="a">{{ a }}</option> }</select></div>
          <div class="field"><label>Search Area</label><input class="input" [(ngModel)]="search" placeholder="Search area..."></div>
          <div class="field"><label>Data Entry</label>
            <select class="select" [(ngModel)]="fDataEntry"><option value="All">All</option><option value="">Unassigned</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select></div>
          <div class="field"><label>Reviewer</label>
            <select class="select" [(ngModel)]="fReview"><option value="All">All</option><option value="">Unassigned</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select></div>
        </div>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
          <div class="field"><label>Sorted by</label>
            <select class="select" [(ngModel)]="fSort"><option value="All">All</option><option value="">Unassigned</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select></div>
          <div class="field"><label>Status</label>
            <select class="select" [(ngModel)]="fStatus"><option value="All">All</option><option value="Pending">Pending</option><option value="Completed">Completed</option></select></div>
          <div class="field" style="align-self:end"><button class="btn btn-p" (click)="load()" style="height:36px">Apply</button></div>
          <div class="field" style="align-self:end"><button class="btn btn-s" (click)="reset()" style="height:36px">Reset</button></div>
        </div>
      </div>

      @if (auth.has('SampleTracking')) {
        <div class="card" style="padding:14px 20px;margin-bottom:16px;display:flex;gap:10px;align-items:end;flex-wrap:wrap">
          <b style="font-size:12.5px;align-self:center">New area/day:</b>
          <div class="field"><label>Area</label><input class="input" style="width:160px" [(ngModel)]="newArea"></div>
          <div class="field"><label>Date</label><input type="date" class="input" [(ngModel)]="newDate"></div>
          <div class="field"><label>Samples</label><input type="number" min="0" class="input" style="width:100px" [(ngModel)]="newCount"></div>
          <button class="btn btn-s" style="height:36px" [disabled]="!newArea.trim() || busy()" (click)="add()">Add</button>
        </div>
      }

      <div class="card" style="padding:0;overflow:hidden">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:12px 16px;border-bottom:1px solid var(--slate-150)">
          <b style="font-size:13px">Sample Area Assignment</b>
          <div style="display:flex;gap:8px">
            @if (auth.has('SampleTracking')) { <button class="btn btn-p btn-mini" [disabled]="busy() || dirtyCount() === 0" (click)="batchSave()">Batch Save All ({{ dirtyCount() }})</button> }
            <button class="btn btn-s btn-mini" (click)="exportAssignExcel()" [disabled]="!filtered().length">Export Excel</button>
            <button class="btn btn-s btn-mini" (click)="exportAssignPdf()" [disabled]="!filtered().length">Export PDF</button>
          </div>
        </div>
        @if (loading()) { <div class="empty" style="padding:24px">Loading…</div> }
        @else {
          <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
            <thead><tr><th>Date</th><th>Area</th><th>Samples</th><th>Data entry</th><th>Reviewed by</th><th>Sorted by</th><th>Notes</th><th>Status</th></tr></thead>
            <tbody>
              @for (r of filtered(); track r.id) {
                <tr>
                  <td class="mono small">{{ r.date }}</td>
                  <td><b>{{ r.area }}</b></td>
                  <td><input type="number" min="0" class="input num" [ngModel]="draft(r).count" (ngModelChange)="setD(r, 'count', $event)" [disabled]="!auth.has('SampleTracking')"></td>
                  <td><select class="select sel" [ngModel]="draft(r).dataEntryUser" (ngModelChange)="setD(r, 'dataEntryUser', $event)" [disabled]="!auth.has('SampleTracking')">
                    <option value="">Unassigned</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select>
                    @if (r.dataEntryAt) { <div class="small muted mono">{{ fmt(r.dataEntryAt) }}</div> }</td>
                  <td><select class="select sel" [ngModel]="draft(r).reviewUser" (ngModelChange)="setD(r, 'reviewUser', $event)" [disabled]="!auth.has('SampleTracking') || !draft(r).dataEntryUser">
                    <option value="">Unassigned</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select>
                    @if (r.reviewAt) { <div class="small muted mono">{{ fmt(r.reviewAt) }}</div> }</td>
                  <td><select class="select sel" [ngModel]="draft(r).sortUser" (ngModelChange)="setD(r, 'sortUser', $event)" [disabled]="!auth.has('SampleTracking') || !draft(r).reviewUser">
                    <option value="">Unassigned</option>@for (u of users(); track u.id) { <option [value]="u.username">{{ u.username }}</option> }</select>
                    @if (r.sortAt) { <div class="small muted mono">{{ fmt(r.sortAt) }}</div> }</td>
                  <td><input class="input" style="min-width:140px" [ngModel]="draft(r).notes" (ngModelChange)="setD(r, 'notes', $event)" [disabled]="!auth.has('SampleTracking')"></td>
                  <td><span class="badge" [class]="r.isComplete ? 'b-ok' : 'b-warn'">{{ r.isComplete ? 'Completed' : 'Pending' }}</span></td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">No records matching filters</td></tr> }
            </tbody>
          </table></div>
        }
      </div>
    }

    <!-- ===================== Lifecycle Tracking Report ===================== -->
    @if (tab() === 'report') {
      <div class="card" style="padding:20px;margin-bottom:16px">
        <h3 style="margin:0 0 12px;font-size:14px">Report Filtration</h3>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>Start Date</label><input type="date" class="input" [(ngModel)]="rStart"></div>
          <div class="field"><label>End Date</label><input type="date" class="input" [(ngModel)]="rEnd"></div>
          <div class="field"><label>Area</label>
            <select class="select" [(ngModel)]="rArea"><option value="All">All</option>@for (a of reportAreas(); track a) { <option [value]="a">{{ a }}</option> }</select></div>
          <div class="field"><label>Group By</label>
            <select class="select" [(ngModel)]="groupBy"><option value="Area">Area</option><option value="Laboratory">Laboratory</option></select></div>
        </div>
        <div style="display:flex;gap:8px;margin-top:12px">
          <button class="btn btn-p" (click)="loadReport()" style="height:36px">Apply</button>
          <button class="btn btn-s" (click)="resetReport()" style="height:36px">Reset</button>
        </div>
      </div>

      <div class="card" style="padding:0;overflow:hidden">
        <div style="display:flex;justify-content:space-between;align-items:center;padding:12px 16px;border-bottom:1px solid var(--slate-150)">
          <b style="font-size:13px">Sample Life Cycle History</b>
          <div style="display:flex;gap:8px">
            <button class="btn btn-s btn-mini" (click)="exportReportExcel()" [disabled]="!reportFiltered().length">Export Excel</button>
            <button class="btn btn-s btn-mini" (click)="exportLifecyclePdf()" [disabled]="!reportFiltered().length">Lifecycle PDF</button>
            <button class="btn btn-s btn-mini" (click)="exportMotionPdf()" [disabled]="!reportFiltered().length">Motion Tracking PDF</button>
          </div>
        </div>
        @if (reportLoading()) { <div class="empty" style="padding:24px">Loading…</div> }
        @else {
          @for (grp of reportGroups(); track grp.key) {
            <div style="background:var(--slate-100);padding:8px 16px;font-weight:700;font-size:12.5px;border-bottom:1px solid var(--slate-150)">{{ groupBy }}: {{ grp.key }}</div>
            <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
              <thead><tr><th>Laboratory</th><th>Visit datetime</th><th>Samples</th><th>Collected</th><th>Transferred</th><th>Received</th><th>Data entry</th><th>Revised</th><th>Sorted</th><th>Notes</th></tr></thead>
              <tbody>
                @for (r of grp.rows; track $index) {
                  <tr>
                    <td><b>{{ r.lab }}</b><div class="small muted">{{ r.labDisplayCode }}</div></td>
                    <td class="mono small">{{ r.visitDate }} {{ r.visitTime }}</td>
                    <td class="mono" style="font-weight:700">{{ r.samples ?? '—' }}</td>
                    <td class="mono small">{{ fmt(r.collectedAt) }}</td>
                    <td class="mono small">{{ fmt(r.transferredAt) }}</td>
                    <td class="mono small">{{ fmt(r.receivedAt) }}</td>
                    <td class="small">{{ stage(r.dataEntryBy, r.dataEntryAt) }}</td>
                    <td class="small">{{ stage(r.reviewBy, r.reviewAt) }}</td>
                    <td class="small">{{ stage(r.sortBy, r.sortAt) }}</td>
                    <td class="small">{{ r.notes ?? '—' }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          } @empty { <div class="empty" style="text-align:center;padding:24px">No records found</div> }
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

  private readonly today = localToday();
  start = this.today; end = this.today;
  gov = 'All'; city = 'All'; areaFilter = 'All'; search = '';
  fDataEntry = 'All'; fReview = 'All'; fSort = 'All'; fStatus = 'All';
  newArea = ''; newDate = this.today; newCount = 0;
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
  cityOptions(): string[] { return [...new Set(this.cities().filter((c) => this.gov === 'All' || c.governorate === this.gov).map((c) => c.name))].sort(); }
  areaOptions(): string[] { return [...new Set(this.items().map((i) => i.area))].sort(); }

  readonly filtered = computed(() => {
    const q = this.search.trim().toLowerCase();
    return this.items().filter((r) => {
      const g = this.geo(r.area);
      return (this.gov === 'All' || g.gov === this.gov) &&
        (this.city === 'All' || g.city === this.city) &&
        (this.areaFilter === 'All' || r.area === this.areaFilter) &&
        (!q || r.area.toLowerCase().includes(q)) &&
        (this.fDataEntry === 'All' || (r.dataEntryBy ?? '') === this.fDataEntry) &&
        (this.fReview === 'All' || (r.reviewBy ?? '') === this.fReview) &&
        (this.fSort === 'All' || (r.sortBy ?? '') === this.fSort) &&
        (this.fStatus === 'All' || (this.fStatus === 'Completed') === r.isComplete);
    });
  });
  readonly totalSamples = computed(() => this.filtered().reduce((a, r) => a + r.count, 0));
  readonly completed = computed(() => this.filtered().filter((r) => r.isComplete).length);

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
  fmt(iso: string | null): string { return iso ? iso.slice(0, 16).replace('T', ' ') : '—'; }
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
    this.gov = this.city = this.areaFilter = this.fDataEntry = this.fReview = this.fSort = this.fStatus = 'All';
    this.search = ''; this.load();
  }

  add(): void {
    this.busy.set(true);
    this.api.post('/sample-tracking', { area: this.newArea.trim(), date: this.newDate, count: this.newCount || 0 }).subscribe({
      next: () => { this.busy.set(false); this.newArea = ''; this.newCount = 0; this.load(); },
      error: () => this.busy.set(false),
    });
  }

  batchSave(): void {
    const lines = this.items()
      .filter((r) => this.drafts.get(r.id)?.dirty)
      .map((r) => { const d = this.draft(r); return {
        area: r.area, date: r.date, count: d.count || 0,
        dataEntryUser: d.dataEntryUser || null, reviewUser: d.reviewUser || null, sortUser: d.sortUser || null,
        notes: d.notes || null,
      }; });
    if (!lines.length) return;
    this.busy.set(true);
    this.api.post('/sample-tracking/assignments', { lines }).subscribe({
      next: () => { this.busy.set(false); this.load(); },
      error: () => this.busy.set(false),
    });
  }

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

  exportAssignExcel(): void {
    exportCsv('sample-assignments.csv',
      ['Date', 'Area', 'Samples', 'Data entry', 'Reviewed by', 'Sorted by', 'Notes', 'Status'],
      this.filtered().map((r) => [r.date, r.area, r.count, r.dataEntryBy, r.reviewBy, r.sortBy, r.notes, r.isComplete ? 'Completed' : 'Pending']));
  }
  exportAssignPdf(): void {
    printTable('Sample Area Assignment',
      ['Date', 'Area', 'Samples', 'Data entry', 'Reviewed by', 'Sorted by', 'Notes', 'Status'],
      this.filtered().map((r) => [r.date, r.area, r.count, r.dataEntryBy, r.reviewBy, r.sortBy, r.notes, r.isComplete ? 'Completed' : 'Pending']));
  }
  private reportRows(): (string | number | null)[][] {
    return this.reportFiltered().map((r) => [r.lab, r.labDisplayCode, r.area, `${r.visitDate} ${r.visitTime}`, r.samples,
      this.fmt(r.collectedAt), this.fmt(r.transferredAt), this.fmt(r.receivedAt),
      this.stage(r.dataEntryBy, r.dataEntryAt), this.stage(r.reviewBy, r.reviewAt), this.stage(r.sortBy, r.sortAt), r.notes]);
  }
  private static readonly REPORT_HEADER = ['Laboratory', 'Code', 'Area', 'Visit datetime', 'Samples', 'Collected', 'Transferred', 'Received', 'Data entry', 'Revised', 'Sorted', 'Notes'];
  exportReportExcel(): void { exportCsv('sample-lifecycle.csv', SampleTrackingComponent.REPORT_HEADER, this.reportRows()); }
  exportLifecyclePdf(): void { printTable('Sample Life Cycle History', SampleTrackingComponent.REPORT_HEADER, this.reportRows()); }
  exportMotionPdf(): void {
    printTable('Sample Motion Tracking',
      ['Laboratory', 'Area', 'Visit', 'Stage timeline'],
      this.reportFiltered().map((r) => [r.lab, r.area, `${r.visitDate} ${r.visitTime}`,
        ['Collected ' + this.fmt(r.collectedAt), 'Transferred ' + this.fmt(r.transferredAt), 'Received ' + this.fmt(r.receivedAt),
         'Data entry ' + this.fmt(r.dataEntryAt), 'Revised ' + this.fmt(r.reviewAt), 'Sorted ' + this.fmt(r.sortAt)].join(' → ')]));
  }
}
