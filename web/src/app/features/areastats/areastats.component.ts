import { escHtml, exportXlsx, localToday, printDoc, SheetCell } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';

interface AreaStat { date: string; governorate: string | null; city: string | null; area: string | null; governorateRealName: string | null; areaRealName: string | null; testCount: number; income: number; }
interface Cell { count: number; income: number; }
interface AreaRow { area: string; realName: string | null; cells: Record<string, Cell>; total: number; income: number; refCount: number; refIncome: number; }
interface GovGroup { gov: string; realName: string | null; areas: AreaRow[]; cells: Record<string, Cell>; total: number; income: number; refCount: number; refIncome: number; }
type View = 'daily' | 'monthly' | 'yearly';
type Metric = 'count' | 'income';
const MO = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DASH = '—';

@Component({
  selector: 'app-areastats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'areastats' | t : 'Area statistics' }}</div><h1>{{ 'areastats' | t : 'Area statistics' }}</h1></div>
      <div class="pagehead-actions">
        <button class="btn btn-s" [disabled]="syncing()" (click)="openSync()" title="{{ 'sync_oracle_hint_area' | t : 'Pull the latest statistics from Oracle for a date range' }}">
          <i data-lucide="database" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
        </button>
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>
    <div class="small muted" style="margin-bottom:10px">{{ 'areastats_hint' | t : 'Test volumes grouped by governorate and area, compared against a reference month. Green cells beat the reference, red fall short. Daily data syncs automatically each night.' }}</div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_tests' | t : 'Total tests' }}</div><div class="val">{{ k().tests | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_income' | t : 'Total income' }}</div><div class="val">{{ k().income | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'governorates' | t : 'Governorates' }}</div><div class="val">{{ k().govs }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'areas' | t : 'Areas' }}</div><div class="val">{{ k().areas }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'view_by' | t : 'View By' }}</label><select class="select" [ngModel]="view()" (ngModelChange)="view.set($event)"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><label>{{ 'reference_month' | t : 'Reference Month' }}</label><input class="input" type="month" [ngModel]="refMonth()" (ngModelChange)="onRefMonthChange($event)"></div>
        <div class="field"><label>{{ 'compare_by' | t : 'Compare By' }}</label><select class="select" [ngModel]="metric()" (ngModelChange)="metric.set($event)"><option value="count">{{ 'compare_test_count' | t : 'Test Count' }}</option><option value="income">{{ 'compare_test_income' | t : 'Test Income' }}</option></select></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><app-filter-select [multiple]="true" [options]="govs()" [ngModel]="gov()" (ngModelChange)="gov.set($event); city.set([]); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><app-filter-select [multiple]="true" [options]="cities()" [ngModel]="city()" (ngModelChange)="city.set($event); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [multiple]="true" [options]="areas()" [ngModel]="area()" (ngModelChange)="area.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'sort_by' | t : 'Sort By' }}</label><select class="select" [ngModel]="sortDir()" (ngModelChange)="sortDir.set($event)"><option value="desc">{{ 'sort_count_desc' | t : 'Test Count (High → Low)' }}</option><option value="asc">{{ 'sort_count_asc' | t : 'Test Count (Low → High)' }}</option></select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th class="stick">{{ 'governorate_area' | t : 'Governorate / Area' }}</th>
            <th class="r ref">{{ 'ref_by_month' | t : 'Ref by Month' }}</th>
            <th class="r ref">{{ 'ref_by_day' | t : 'Ref by Day' }}</th>
            <th class="r tot">{{ 'total_test_count' | t : 'Total Test Count' }}</th>
            <th class="r">{{ 'total_income' | t : 'Total Income' }}</th>
            @for (p of periods(); track p) { <th class="r">{{ colLabel(p) }}</th> }
          </tr></thead>
          <tbody>
            @for (g of groups(); track g.gov) {
              <tr class="gov-row">
                <td class="stick" style="font-weight:700">{{ g.gov }}@if (g.realName) { <span class="rn">{{ g.realName }}</span> }</td>
                <td class="r mono ref">{{ rv(g) | number: numFmt() }}</td>
                <td class="r mono ref">{{ refDay(rv(g)) | number: numFmt() }}</td>
                <td class="r mono tot" style="font-weight:700">{{ g.total | number:'1.0-0' }}</td>
                <td class="r mono" style="font-weight:700">{{ g.income | number:'1.0-1' }}</td>
                @for (p of periods(); track p) { <td class="r mono" [class]="flag(gcell(g, p), rv(g))">{{ gcell(g, p) | number: numFmt() }}</td> }
              </tr>
              @for (a of g.areas; track a.area) {
                <tr>
                  <td class="stick area-cell">{{ a.area }}@if (a.realName) { <span class="rn">{{ a.realName }}</span> }</td>
                  <td class="r mono ref">{{ rv(a) | number: numFmt() }}</td>
                  <td class="r mono ref">{{ refDay(rv(a)) | number: numFmt() }}</td>
                  <td class="r mono tot" style="font-weight:600">{{ a.total | number:'1.0-0' }}</td>
                  <td class="r mono">{{ a.income | number:'1.0-1' }}</td>
                  @for (p of periods(); track p) { <td class="r mono" [class]="flag(acell(a, p), rv(a))">{{ acell(a, p) | number: numFmt() }}</td> }
                </tr>
              }
            } @empty { <tr><td [attr.colspan]="periods().length + 5" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (groups().length) {
            <tfoot><tr>
              <td class="stick" style="font-weight:800">{{ 'total' | t : 'Total' }}</td>
              <td class="r mono ref" style="font-weight:800">{{ k().refMonth | number: numFmt() }}</td>
              <td class="r mono ref" style="font-weight:800">{{ refDay(k().refMonth) | number: numFmt() }}</td>
              <td class="r mono tot" style="font-weight:800">{{ k().tests | number:'1.0-0' }}</td>
              <td class="r mono" style="font-weight:800">{{ k().income | number:'1.0-1' }}</td>
              @for (p of periods(); track p) { <td class="r mono" style="font-weight:800">{{ colTotal(p) | number: numFmt() }}</td> }
            </tr></tfoot>
          }
        </table>
      }
    </div>

    @if (syncOpen()) {
      <div class="as-overlay" (click)="syncOpen.set(false)">
        <div class="as-dlg" (click)="$event.stopPropagation()">
          <div class="as-dlg-head">
            <h2>{{ 'sync_oracle' | t : 'Sync from Oracle' }}</h2>
            <button class="btn btn-mini btn-s" (click)="syncOpen.set(false)">✕</button>
          </div>
          <div style="padding:16px">
            <div class="small muted" style="margin-bottom:12px">{{ 'sync_range_hint_area' | t : 'Pull daily statistics from Oracle for this date range and merge them into the existing data. The last day is also synced automatically every night.' }}</div>
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="syncFrom"></app-date-input></div>
              <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="syncTo"></app-date-input></div>
            </div>
          </div>
          <div class="as-dlg-foot">
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
    td.ref{background:var(--slate-50,#f8fafc)}
    .area-cell{padding-inline-start:22px;color:var(--slate-700,#605e5c)}
    .rn{display:inline-block;margin-inline-start:6px;font-weight:400;color:var(--primary-blue,#0078D4)}
    .gov-row .rn{color:#004578}
    .gov-row td{background:var(--slate-100,#f1f5f9)}
    .gov-row .stick{background:var(--slate-100,#f1f5f9)}
    td.pos{background:rgba(22,163,74,.14);color:#15803d;font-weight:700}
    td.neg{background:rgba(220,38,38,.12);color:#b91c1c;font-weight:700}
    tfoot td{border-top:2px solid var(--slate-150,#edebe9);background:var(--white,#fff)}
    .as-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .as-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,460px)}
    .as-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
    .as-dlg-head h2{font-size:15px;margin:0}
    .as-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
  `],
})
export class AreaStatsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly loading = signal(true);
  readonly rows = signal<AreaStat[]>([]);
  readonly refRows = signal<AreaStat[]>([]);
  readonly gov = signal<string[]>([]);
  readonly city = signal<string[]>([]);
  readonly area = signal<string[]>([]);
  readonly view = signal<View>('monthly');
  readonly metric = signal<Metric>('count');
  readonly numFmt = computed(() => (this.metric() === 'income' ? '1.0-1' : '1.0-0'));
  readonly sortDir = signal<'desc' | 'asc'>('desc');
  readonly syncOpen = signal(false);
  readonly syncing = signal(false);
  syncFrom = ''; syncTo = '';
  private readonly today = localToday();
  from = this.today.slice(0, 7) + '-01'; to = this.today; // first of the current month → today
  readonly refMonth = signal(this.prevMonth()); // default: previous calendar month

  private prevMonth(): string {
    const d = new Date(); d.setDate(1); d.setMonth(d.getMonth() - 1);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  }
  private daysInRefMonth(): number {
    const [y, m] = this.refMonth().split('-').map(Number);
    return y && m ? new Date(y, m, 0).getDate() : 30;
  }
  refDay(refMonthTotal: number): number { return refMonthTotal / this.daysInRefMonth(); }

  // Cascading filter option lists (a city/area list narrows to the selected governorate(s)/cities).
  readonly govs = computed(() => [...new Set(this.rows().map((s) => s.governorate ?? DASH))].sort());
  readonly cities = computed(() => [...new Set(this.rows().filter((s) => !this.gov().length || this.gov().includes(s.governorate ?? DASH)).map((s) => s.city ?? DASH))].sort());
  readonly areas = computed(() => [...new Set(this.rows()
    .filter((s) => (!this.gov().length || this.gov().includes(s.governorate ?? DASH)) && (!this.city().length || this.city().includes(s.city ?? DASH)))
    .map((s) => s.area ?? DASH))].sort());

  private matches(s: AreaStat): boolean {
    return (!this.gov().length || this.gov().includes(s.governorate ?? DASH)) &&
      (!this.city().length || this.city().includes(s.city ?? DASH)) &&
      (!this.area().length || this.area().includes(s.area ?? DASH));
  }
  readonly filtered = computed(() => this.rows().filter((s) => this.matches(s)));

  private periodKey(date: string): string { const v = this.view(); return v === 'yearly' ? date.slice(0, 4) : v === 'monthly' ? date.slice(0, 7) : date; }
  colLabel(c: string): string { if (this.view() === 'monthly' && c.length === 7) { const [y, m] = c.split('-'); return `${MO[+m - 1]} ${y}`; } return c; }

  readonly periods = computed<string[]>(() => {
    const set = new Set<string>();
    for (const s of this.filtered()) set.add(this.periodKey(s.date));
    return [...set].sort((a, b) => a.localeCompare(b));
  });

  // Reference-month totals (count + income) per governorate and per (governorate|area), respecting the geography filters.
  private readonly refByGov = computed<Record<string, Cell>>(() => {
    const m: Record<string, Cell> = {};
    for (const s of this.refRows()) { if (!this.matches(s)) continue; const g = s.governorate ?? DASH; const c = m[g] ?? (m[g] = { count: 0, income: 0 }); c.count += s.testCount; c.income += s.income; }
    return m;
  });
  private readonly refByArea = computed<Record<string, Cell>>(() => {
    const m: Record<string, Cell> = {};
    for (const s of this.refRows()) { if (!this.matches(s)) continue; const key = (s.governorate ?? DASH) + '|' + (s.area ?? DASH); const c = m[key] ?? (m[key] = { count: 0, income: 0 }); c.count += s.testCount; c.income += s.income; }
    return m;
  });

  readonly groups = computed<GovGroup[]>(() => {
    const refG = this.refByGov(); const refA = this.refByArea();
    const govMap = new Map<string, GovGroup>();
    for (const s of this.filtered()) {
      const p = this.periodKey(s.date);
      const govName = s.governorate ?? DASH;
      const areaName = s.area ?? DASH;
      let g = govMap.get(govName);
      if (!g) { const r = refG[govName]; g = { gov: govName, realName: s.governorateRealName ?? null, areas: [], cells: {}, total: 0, income: 0, refCount: r?.count ?? 0, refIncome: r?.income ?? 0 }; govMap.set(govName, g); }
      const gc = g.cells[p] ?? (g.cells[p] = { count: 0, income: 0 }); gc.count += s.testCount; gc.income += s.income; g.total += s.testCount; g.income += s.income;
      let a = g.areas.find((x) => x.area === areaName);
      if (!a) { const r = refA[govName + '|' + areaName]; a = { area: areaName, realName: s.areaRealName ?? null, cells: {}, total: 0, income: 0, refCount: r?.count ?? 0, refIncome: r?.income ?? 0 }; g.areas.push(a); }
      const ac = a.cells[p] ?? (a.cells[p] = { count: 0, income: 0 }); ac.count += s.testCount; ac.income += s.income; a.total += s.testCount; a.income += s.income;
    }
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    const list = [...govMap.values()];
    for (const g of list) g.areas.sort((a, b) => dir * (a.total - b.total) || a.area.localeCompare(b.area));
    list.sort((a, b) => dir * (a.total - b.total) || a.gov.localeCompare(b.gov));
    return list;
  });

  readonly columnTotals = computed<Record<string, Cell>>(() => {
    const m: Record<string, Cell> = {};
    for (const s of this.filtered()) { const p = this.periodKey(s.date); const c = m[p] ?? (m[p] = { count: 0, income: 0 }); c.count += s.testCount; c.income += s.income; }
    return m;
  });
  private mv(c: Cell | undefined): number { return c ? (this.metric() === 'income' ? c.income : c.count) : 0; }
  /** Reference-month total for a governorate/area row in the selected metric. */
  rv(row: { refCount: number; refIncome: number }): number { return this.metric() === 'income' ? row.refIncome : row.refCount; }
  gcell(g: GovGroup, p: string): number { return this.mv(g.cells[p]); }
  acell(a: AreaRow, p: string): number { return this.mv(a.cells[p]); }
  colTotal(p: string): number { return this.mv(this.columnTotals()[p]); }

  /** Baseline the current view's period columns are compared against, scaled from the reference month. */
  private baseline(refMonthTotal: number): number {
    const v = this.view();
    return v === 'daily' ? refMonthTotal / this.daysInRefMonth() : v === 'yearly' ? refMonthTotal * 12 : refMonthTotal;
  }
  /** Green when a period cell beats the reference baseline, red when it falls short, neutral otherwise. */
  flag(value: number, refMonthTotal: number): string {
    const b = this.baseline(refMonthTotal);
    if (b <= 0) return '';
    return value > b ? 'pos' : value < b ? 'neg' : '';
  }

  readonly k = computed(() => {
    const f = this.filtered();
    const tests = f.reduce((a, s) => a + s.testCount, 0);
    const govs = new Set(f.map((s) => s.governorate ?? DASH)).size;
    const areas = new Set(f.map((s) => (s.governorate ?? DASH) + '|' + (s.area ?? DASH))).size;
    const ref = this.refRows().filter((s) => this.matches(s));
    const refMonth = this.metric() === 'income' ? ref.reduce((a, s) => a + s.income, 0) : ref.reduce((a, s) => a + s.testCount, 0);
    return { tests, income: f.reduce((a, s) => a + s.income, 0), govs, areas, refMonth };
  });

  constructor() { this.load(); }

  private monthRange(ym: string): { from: string; to: string } {
    const [y, m] = ym.split('-').map(Number);
    const from = `${ym}-01`;
    const to = `${ym}-${String(new Date(y, m, 0).getDate()).padStart(2, '0')}`;
    return { from, to };
  }
  load(): void {
    this.loading.set(true);
    this.api.get<AreaStat[]>('/area-statistics', { from: this.from, to: this.to }).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
    this.loadRef();
  }
  private loadRef(): void {
    const { from, to } = this.monthRange(this.refMonth());
    this.api.get<AreaStat[]>('/area-statistics', { from, to }).subscribe({ next: (r) => this.refRows.set(r), error: () => this.refRows.set([]) });
  }
  onRefMonthChange(v: string): void { this.refMonth.set(v); this.loadRef(); }

  private ymd(d: Date): string { return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; }
  openSync(): void {
    const y = new Date(); y.setDate(y.getDate() - 1);
    this.syncFrom = this.ymd(y); this.syncTo = this.today; // default: yesterday → today
    this.syncOpen.set(true);
  }
  runSync(): void {
    if (!this.syncFrom || !this.syncTo) { this.toast.warning('Please choose a start and end date.'); return; }
    if (this.syncFrom > this.syncTo) { this.toast.warning('The start date must be on or before the end date.'); return; }
    this.syncing.set(true);
    this.api.post<{ labsUpserted: number }>('/area-statistics/sync', { from: this.syncFrom, to: this.syncTo }).subscribe({
      next: (r) => {
        this.syncing.set(false); this.syncOpen.set(false);
        this.toast.success(`Synced from Oracle: ${r.labsUpserted} lab-day record(s) for ${this.syncFrom} → ${this.syncTo}.`);
        if (this.syncFrom < this.from) this.from = this.syncFrom;
        if (this.syncTo > this.to) this.to = this.syncTo;
        this.load();
      },
      error: () => { this.syncing.set(false); },
    });
  }

  private exportHeaders(): string[] {
    return ['Governorate', 'Area', 'Real Name', 'Ref by Month', 'Ref by Day', 'Total Test Count', 'Total Income', ...this.periods().map((p) => this.colLabel(p))];
  }
  /** A flagged period cell: green/red fill vs the reference baseline (govFill = light band on a governorate row). */
  private pcell(v: number, refVal: number, govFill = false): SheetCell {
    const f = this.flag(v, refVal);
    if (f) return { v, fill: f as 'pos' | 'neg' };
    return govFill ? { v, fill: 'gov', bold: true } : v;
  }
  private exportRows(): SheetCell[][] {
    const periods = this.periods();
    const g0 = (v: string | number): SheetCell => ({ v, fill: 'gov', bold: true }); // governorate-row cell
    const dec = (v: number) => Math.round(v * 10) / 10;
    const out: SheetCell[][] = [];
    for (const g of this.groups()) {
      const gr = this.rv(g);
      out.push([g0(g.gov), g0(''), g0(g.realName ?? ''), g0(dec(gr)), g0(dec(this.refDay(gr))), g0(g.total), g0(dec(g.income)),
        ...periods.map((p) => this.pcell(dec(this.gcell(g, p)), gr, true))]);
      for (const a of g.areas) {
        const ar = this.rv(a);
        out.push(['', a.area, a.realName ?? '', dec(ar), dec(this.refDay(ar)), a.total, dec(a.income),
          ...periods.map((p) => this.pcell(dec(this.acell(a, p)), ar))]);
      }
    }
    return out;
  }
  exportExcel(): void { exportXlsx(`area-statistics-${this.today}.xlsx`, this.exportHeaders(), this.exportRows()); }

  /** PDF report: renders the grouped grid with the real name beside each name and the same green/red flags. */
  exportPdf(): void {
    const periods = this.periods();
    const head = ['Governorate / Area', 'Ref by Month', 'Ref by Day', 'Total Test Count', 'Total Income', ...periods.map((p) => this.colLabel(p))];
    const thead = '<thead><tr>' + head.map((h, i) => `<th class="${i >= 1 ? 'r' : ''}">${escHtml(h)}</th>`).join('') + '</tr></thead>';
    const rn = (v: string | null) => (v ? `<span class="rn">${escHtml(v)}</span>` : '');
    const dec = (v: number) => Math.round(v * 10) / 10;
    let body = '';
    for (const g of this.groups()) {
      const gr = this.rv(g);
      body += `<tr class="gov"><td>${escHtml(g.gov)}${rn(g.realName)}</td>`
        + `<td class="r">${dec(gr)}</td><td class="r">${dec(this.refDay(gr))}</td>`
        + `<td class="r">${g.total}</td><td class="r">${dec(g.income)}</td>`
        + periods.map((p) => { const v = dec(this.gcell(g, p)); return `<td class="r ${this.flag(this.gcell(g, p), gr)}">${v}</td>`; }).join('') + '</tr>';
      for (const a of g.areas) {
        const ar = this.rv(a);
        body += `<tr><td style="padding-inline-start:22px">${escHtml(a.area)}${rn(a.realName)}</td>`
          + `<td class="r">${dec(ar)}</td><td class="r">${dec(this.refDay(ar))}</td>`
          + `<td class="r">${a.total}</td><td class="r">${dec(a.income)}</td>`
          + periods.map((p) => { const v = dec(this.acell(a, p)); return `<td class="r ${this.flag(this.acell(a, p), ar)}">${v}</td>`; }).join('') + '</tr>';
      }
    }
    printDoc(`Area statistics (${this.from} → ${this.to})`, `<table>${thead}<tbody>${body}</tbody></table>`);
  }
}
