import { exportXlsx, localToday, printTable } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabStat } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface LabPivotRow { labCode: string; name: string; category: string; segment: string; governorate: string; city: string; area: string; cells: Record<string, number>; totalTests: number; totalIncome: number; }
type View = 'daily' | 'monthly' | 'yearly';
const MO = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

@Component({
  selector: 'app-labstats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'labstats' | t : 'Lab statistics' }}</div><h1>{{ 'labstats' | t : 'Lab statistics' }}</h1></div>
      @if (auth.has('ViewLabStats')) {
        <div class="pagehead-actions">
          <input type="file" #fileIn accept=".xlsx,.xls,.csv" hidden (change)="onImport($event)">
          <button class="btn btn-s" [disabled]="importing()" (click)="fileIn.click()">
            <i data-lucide="upload" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ importing() ? ('importing' | t : 'Importing…') : ('import_excel' | t : 'Import Excel') }}
          </button>
          <button class="btn btn-s" [disabled]="syncing()" (click)="openSync()" title="{{ 'sync_oracle_hint_labs' | t : 'Pull the latest lab statistics from Oracle for a date range' }}">
            <i data-lucide="database" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
          </button>
          <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
          <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
        </div>
      }
    </div>
    @if (summary()) { <div class="inline-banner" [class.inline-banner-error]="summaryError()">{{ summary() }}</div> }
    <div class="small muted" style="margin-bottom:10px">{{ 'import_hint' | t : 'Import columns: Date, LabCode, Registrations, TestCount, Income.' }}</div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_tests' | t : 'Total tests' }}</div><div class="val">{{ k().tests | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_income' | t : 'Total income' }}</div><div class="val">{{ k().income | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'active_labs_in_stats' | t : 'Active Labs in Stats' }}</div><div class="val">{{ k().labs }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'avg_tests_visit' | t : 'Avg tests / visit' }}</div><div class="val">{{ k().avg | number:'1.0-0' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'view_type' | t : 'View Type' }}</label><select class="select" [ngModel]="view()" (ngModelChange)="view.set($event)"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
        <div class="field"><label>{{ 'search_lab' | t : 'Search Lab' }}</label><input class="input" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="name or code"></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><app-filter-select [multiple]="true" [options]="govs()" [ngModel]="gov()" (ngModelChange)="gov.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><app-filter-select [multiple]="true" [options]="cities()" [ngModel]="city()" (ngModelChange)="city.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [multiple]="true" [options]="areas()" [ngModel]="area()" (ngModelChange)="area.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'segment' | t : 'Segment' }}</label><app-filter-select [options]="segments()" [ngModel]="segment()" (ngModelChange)="segment.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'lab_status' | t : 'Lab Status' }}</label><app-filter-select [options]="statuses()" [ngModel]="status()" (ngModelChange)="status.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'category' | t : 'Category' }}</label><app-filter-select [multiple]="true" [options]="categories()" [ngModel]="category()" (ngModelChange)="category.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'sort_by' | t : 'Sort By' }}</label><select class="select" [ngModel]="sortBy()" (ngModelChange)="sortBy.set($event)">
          <option value="tests_desc">{{ 'sort_tests_desc' | t : 'Total Tests (High → Low)' }}</option>
          <option value="tests_asc">{{ 'sort_tests_asc' | t : 'Total Tests (Low → High)' }}</option>
          <option value="income_desc">{{ 'sort_income_desc' | t : 'Total Income (High → Low)' }}</option>
          <option value="income_asc">{{ 'sort_income_asc' | t : 'Total Income (Low → High)' }}</option>
        </select></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th class="stick">{{ 'lab_name' | t : 'Lab name' }}</th>
            <th>{{ 'category' | t : 'Category' }}</th><th>{{ 'segment' | t }}</th>
            <th>{{ 'governorate_2' | t }}</th><th>{{ 'city' | t : 'City' }}</th><th>{{ 'area_2' | t }}</th>
            @for (p of periods(); track p) { <th class="r">{{ colLabel(p) }}</th> }
            <th class="r tot">{{ 'total_tests' | t : 'Total tests' }}</th><th class="r">{{ 'total_income' | t : 'Total income' }}</th>
          </tr></thead>
          <tbody>
            @for (r of paged(); track r.labCode) {
              <tr>
                <td class="stick" style="font-weight:600">{{ r.name }}<div class="small muted mono">{{ r.labCode }}</div></td>
                <td>{{ r.category }}</td>
                <td><span class="badge b-info">{{ r.segment }}</span></td>
                <td>{{ r.governorate }}</td><td>{{ r.city }}</td><td>{{ r.area }}</td>
                @for (p of periods(); track p) { <td class="r mono">{{ cell(r, p) | number:'1.0-0' }}</td> }
                <td class="r mono tot" style="font-weight:700">{{ r.totalTests | number:'1.0-0' }}</td>
                <td class="r mono" style="font-weight:700">{{ r.totalIncome | number:'1.0-1' }}</td>
              </tr>
            } @empty { <tr><td [attr.colspan]="periods().length + 8" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (pivot().length) {
            <tfoot><tr>
              <td class="stick" style="font-weight:700">{{ 'total' | t : 'Total' }}</td><td></td><td></td><td></td><td></td><td></td>
              @for (p of periods(); track p) { <td class="r mono" style="font-weight:700">{{ colTotal(p) | number:'1.0-0' }}</td> }
              <td class="r mono tot" style="font-weight:800">{{ k().tests | number:'1.0-0' }}</td>
              <td class="r mono" style="font-weight:800">{{ k().income | number:'1.0-1' }}</td>
            </tr></tfoot>
          }
        </table>
        @if (pivot().length) {
          <div class="fu-pager">
            <button class="btn-ghost" [disabled]="curPage() <= 1" (click)="page.set(curPage() - 1)">‹ {{ 'prev' | t : 'Prev' }}</button>
            <span>{{ 'page' | t : 'Page' }} {{ curPage() }} / {{ pageCount() }} · {{ pivot().length }} {{ 'labs_lc' | t : 'labs' }}</span>
            <button class="btn-ghost" [disabled]="curPage() >= pageCount()" (click)="page.set(curPage() + 1)">{{ 'next' | t : 'Next' }} ›</button>
            <select class="select" [ngModel]="pageSize()" (ngModelChange)="pageSize.set(+$event); page.set(1)" style="max-width:90px;margin-inline-start:auto">
              <option [ngValue]="25">25</option><option [ngValue]="50">50</option><option [ngValue]="100">100</option>
            </select>
          </div>
        }
      }
    </div>

    @if (syncOpen()) {
      <div class="ls-overlay" (click)="syncOpen.set(false)">
        <div class="ls-dlg" (click)="$event.stopPropagation()">
          <div class="ls-dlg-head">
            <h2>{{ 'sync_oracle' | t : 'Sync from Oracle' }}</h2>
            <button class="btn btn-mini btn-s" (click)="syncOpen.set(false)">✕</button>
          </div>
          <div style="padding:16px">
            <div class="small muted" style="margin-bottom:12px">{{ 'sync_range_hint_labs' | t : 'Pull per-lab daily statistics from Oracle for this date range and merge them into the existing data.' }}</div>
            @if (syncErr()) { <div class="inline-banner inline-banner-error" style="margin-bottom:12px">{{ syncErr() }}</div> }
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="syncFrom"></app-date-input></div>
              <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="syncTo"></app-date-input></div>
            </div>
          </div>
          <div class="ls-dlg-foot">
            <button class="btn btn-s" (click)="syncOpen.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="syncing()" (click)="runSync()">{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync' | t : 'Sync') }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    th.r,td.r{text-align:right}
    .stick{position:sticky;inset-inline-start:0;z-index:1}
    td.stick{background:var(--white,#fff)}
    thead .stick{z-index:2}
    td.tot,th.tot{border-inline-start:2px solid var(--slate-150,#edebe9)}
    tfoot td{border-top:2px solid var(--slate-150,#edebe9);background:var(--white,#fff)}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150,#edebe9);font-size:12.5px;color:var(--slate-700,#605e5c)}
    .ls-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .ls-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,460px)}
    .ls-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
    .ls-dlg-head h2{font-size:15px;margin:0}
    .ls-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
  `],
})
export class LabStatsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly importing = signal(false);
  readonly summary = signal<string | null>(null);
  readonly summaryError = signal(false);
  readonly rows = signal<LabStat[]>([]);
  readonly q = signal('');
  readonly gov = signal<string[]>([]);
  readonly city = signal<string[]>([]);
  readonly area = signal<string[]>([]);
  readonly segment = signal('');
  readonly status = signal('');
  readonly category = signal<string[]>([]);
  readonly sortBy = signal<'tests_desc' | 'tests_asc' | 'income_desc' | 'income_asc'>('tests_desc');
  readonly view = signal<View>('monthly');
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly syncOpen = signal(false);
  readonly syncing = signal(false);
  readonly syncErr = signal<string | null>(null);
  syncFrom = ''; syncTo = '';
  private readonly today = localToday();
  from = this.today.slice(0, 7) + '-01'; to = this.today; // first of the current month → today

  readonly govs = computed(() => [...new Set(this.rows().map((s) => s.governorate).filter((v): v is string => !!v))].sort());
  readonly cities = computed(() => [...new Set(this.rows().map((s) => s.city).filter((v): v is string => !!v))].sort());
  readonly areas = computed(() => [...new Set(this.rows().map((s) => s.area).filter((v): v is string => !!v))].sort());
  readonly segments = computed(() => [...new Set(this.rows().map((s) => s.segment).filter((v): v is string => !!v))].sort());
  readonly statuses = computed(() => [...new Set(this.rows().map((s) => s.status).filter((v): v is string => !!v))].sort());
  readonly categories = computed(() => [...new Set(this.rows().map((s) => s.category).filter((v): v is string => !!v))].sort());

  readonly filtered = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.rows().filter((s) =>
      (!q || s.labCode.toLowerCase().includes(q) || (s.name ?? '').toLowerCase().includes(q)) &&
      (!this.gov().length || this.gov().includes(s.governorate ?? '')) &&
      (!this.city().length || this.city().includes(s.city ?? '')) &&
      (!this.area().length || this.area().includes(s.area ?? '')) &&
      (!this.segment() || s.segment === this.segment()) &&
      (!this.status() || s.status === this.status()) &&
      (!this.category().length || this.category().includes(s.category ?? '')));
  });
  private periodKey(date: string): string { const v = this.view(); return v === 'yearly' ? date.slice(0, 4) : v === 'monthly' ? date.slice(0, 7) : date; }
  colLabel(c: string): string { if (this.view() === 'monthly' && c.length === 7) { const [y, m] = c.split('-'); return `${MO[+m - 1]} ${y}`; } return c; }

  readonly periods = computed<string[]>(() => {
    const set = new Set<string>();
    for (const s of this.filtered()) set.add(this.periodKey(s.date));
    return [...set].sort((a, b) => a.localeCompare(b));
  });
  readonly pivot = computed<LabPivotRow[]>(() => {
    const map = new Map<string, LabPivotRow>();
    for (const s of this.filtered()) {
      const period = this.periodKey(s.date);
      let r = map.get(s.labCode);
      if (!r) { r = { labCode: s.labCode, name: s.name ?? s.labCode, category: s.category ?? '—', segment: s.segment ?? '—', governorate: s.governorate ?? '—', city: s.city ?? '—', area: s.area ?? '—', cells: {}, totalTests: 0, totalIncome: 0 }; map.set(s.labCode, r); }
      r.cells[period] = (r.cells[period] ?? 0) + s.testCount;
      r.totalTests += s.testCount; r.totalIncome += s.income;
    }
    const sb = this.sortBy();
    return [...map.values()].sort((a, b) => {
      const tie = a.name.localeCompare(b.name);
      switch (sb) {
        case 'tests_asc': return (a.totalTests - b.totalTests) || tie;
        case 'income_desc': return (b.totalIncome - a.totalIncome) || tie;
        case 'income_asc': return (a.totalIncome - b.totalIncome) || tie;
        default: return (b.totalTests - a.totalTests) || tie;
      }
    });
  });
  readonly columnTotals = computed<Record<string, number>>(() => {
    const m: Record<string, number> = {};
    for (const s of this.filtered()) { const p = this.periodKey(s.date); m[p] = (m[p] ?? 0) + s.testCount; }
    return m;
  });
  cell(r: LabPivotRow, p: string): number { return r.cells[p] ?? 0; }
  colTotal(p: string): number { return this.columnTotals()[p] ?? 0; }
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.pivot().length / this.pageSize())));
  readonly curPage = computed(() => Math.min(this.page(), this.pageCount()));
  readonly paged = computed<LabPivotRow[]>(() => {
    const start = (this.curPage() - 1) * this.pageSize();
    return this.pivot().slice(start, start + this.pageSize());
  });
  readonly k = computed(() => {
    const f = this.filtered();
    const tests = f.reduce((a, s) => a + s.testCount, 0);
    const labs = new Set(f.map((s) => s.labCode)).size;
    // Each row is one (lab, day) statistic = one lab visit; average per lab per visit divides by visits,
    // not by distinct labs (a lab active over N days is N visits, not one).
    const visits = f.length;
    return { tests, income: f.reduce((a, s) => a + s.income, 0), labs, visits, avg: visits ? tests / visits : 0 };
  });

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true); this.page.set(1);
    this.api.get<LabStat[]>('/labstats', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private ymd(d: Date): string { return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; }
  openSync(): void {
    const y = new Date(); y.setDate(y.getDate() - 1);
    this.syncFrom = this.ymd(y); this.syncTo = this.today; // default: yesterday → today
    this.syncErr.set(null); this.syncOpen.set(true);
  }
  runSync(): void {
    if (!this.syncFrom || !this.syncTo) { this.syncErr.set('Please choose a start and end date.'); return; }
    if (this.syncFrom > this.syncTo) { this.syncErr.set('The start date must be on or before the end date.'); return; }
    this.syncing.set(true); this.syncErr.set(null);
    this.api.post<{ labsUpserted: number; upserts?: Record<string, number> }>('/labstats/sync', { from: this.syncFrom, to: this.syncTo }).subscribe({
      next: (r) => {
        this.syncing.set(false); this.syncOpen.set(false); this.summaryError.set(false);
        const restatused = r.upserts?.['LabStatus'] ?? 0;
        this.summary.set(`Synced from Oracle: ${r.labsUpserted} lab-day record(s) for ${this.syncFrom} → ${this.syncTo}; ${restatused} lab status(es) updated.`);
        if (this.syncFrom < this.from) this.from = this.syncFrom;
        if (this.syncTo > this.to) this.to = this.syncTo;
        this.load();
      },
      error: (e) => { this.syncing.set(false); this.syncErr.set(e?.error?.detail ?? 'Oracle sync failed.'); },
    });
  }

  private exportHeaders(): string[] { return ['Lab name', 'Code', 'Category', 'Segment', 'Governorate', 'City', 'Area', ...this.periods().map((p) => this.colLabel(p)), 'Total tests', 'Total income']; }
  private exportRows(): (string | number)[][] {
    const periods = this.periods();
    return this.pivot().map((r) => [r.name, r.labCode, r.category, r.segment, r.governorate, r.city, r.area, ...periods.map((p) => this.cell(r, p)), r.totalTests, Math.round(r.totalIncome * 10) / 10]);
  }
  exportExcel(): void { exportXlsx(`lab-statistics-${this.today}.xlsx`, this.exportHeaders(), this.exportRows()); }
  exportPdf(): void { printTable('Lab statistics', this.exportHeaders(), this.exportRows()); }

  onImport(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0]; if (!file) return;
    this.importing.set(true); this.summary.set(null); this.summaryError.set(false);
    const reader = new FileReader();
    reader.onload = () => {
      const content = String(reader.result).split(',')[1] ?? ''; // strip data: prefix → base64
      this.api.post<{ processed: number; upserted: number; skipped: number; warnings: string[] }>('/labstats/import', { content }).subscribe({
        next: (s) => { this.importing.set(false); this.summary.set(`Imported ${s.processed}: ${s.upserted} upserted, ${s.skipped} skipped${s.warnings.length ? ' · ' + s.warnings.length + ' warning(s)' : ''}.`); input.value = ''; this.load(); },
        error: (e) => { this.importing.set(false); this.summaryError.set(true); this.summary.set(e?.error?.detail ?? 'Import failed.'); input.value = ''; },
      });
    };
    reader.readAsDataURL(file);
  }
}
