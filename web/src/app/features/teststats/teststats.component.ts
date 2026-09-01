import { exportCsv, localToday, printTable } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface TestStat { date: string; testCode: string; testName: string | null; groupName: string | null; count: number; income: number; }
interface Cell { count: number; income: number; }
interface PivotRow { testCode: string; testName: string; groupName: string; cells: Record<string, Cell>; totalCount: number; totalIncome: number; }
interface Group { id: string; code: string; nameEn: string; }
type View = 'daily' | 'monthly' | 'yearly';
const MO = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

@Component({
  selector: 'app-teststats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'teststats' | t : 'Test statistics' }}</div><h1>{{ 'teststats' | t : 'Test statistics' }}</h1></div>
      <div class="pagehead-actions">
        @if (auth.has('AddTeststats')) {
          <input type="file" #fileIn accept=".xlsx,.xls,.csv" hidden (change)="onImport($event)">
          <button class="btn btn-s" [disabled]="importing()" (click)="fileIn.click()">
            <i data-lucide="upload" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ importing() ? ('importing' | t : 'Importing…') : ('import_excel' | t : 'Import Excel') }}
          </button>
          <button class="btn btn-s" [disabled]="syncing()" (click)="openSync()" title="{{ 'sync_oracle_hint' | t : 'Pull the latest test statistics from Oracle for a date range' }}">
            <i data-lucide="database" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
          </button>
        }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>
    @if (summary()) { <div class="inline-banner" [class.inline-banner-error]="summaryError()">{{ summary() }}</div> }
    <div class="small muted" style="margin-bottom:10px">{{ 'import_hint_tests' | t : 'Import columns: Date, TestCode, Count, Income.' }}</div>

    <div class="kpis" style="grid-template-columns:repeat(2,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'total_tests' | t : 'Total tests' }}</div><div class="val">{{ totals().count | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_income' | t : 'Total income' }}</div><div class="val">{{ totals().income | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(6,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'view_by' | t : 'View By' }}</label><select class="select" [ngModel]="view()" (ngModelChange)="view.set($event)"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><label>{{ 'search_test_code' | t : 'Search (Test/Code)' }}</label><input class="input" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="test name or code"></div>
        <div class="field"><label>{{ 'group' | t : 'Group' }}</label><select class="select" [ngModel]="group()" (ngModelChange)="group.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (g of groups(); track g.id) { <option [value]="g.nameEn">{{ g.nameEn }}</option> }</select></div>
        <div class="field"><label>{{ 'sort_by' | t : 'Sort By' }}</label><select class="select" [ngModel]="sortDir()" (ngModelChange)="sortDir.set($event)"><option value="desc">{{ 'sort_count_desc' | t : 'Test Count (High → Low)' }}</option><option value="asc">{{ 'sort_count_asc' | t : 'Test Count (Low → High)' }}</option></select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'test_code_2' | t : 'Test Code' }}</th><th>{{ 'test_name_2' | t : 'Test Name' }}</th><th>{{ 'parent_group' | t : 'Parent Group' }}</th>
            @for (p of periods(); track p) { <th class="r">{{ colLabel(p) }}</th> }
            <th class="r tot">{{ 'total_count' | t : 'Total Count' }}</th><th class="r">{{ 'total_income' | t : 'Total Income' }}</th>
          </tr></thead>
          <tbody>
            @for (r of pivot(); track r.testCode) {
              <tr>
                <td class="mono" style="font-weight:600">{{ r.testCode }}</td>
                <td>{{ r.testName }}</td>
                <td>@if (r.groupName !== '—') { <span class="badge b-neu">{{ r.groupName }}</span> } @else { — }</td>
                @for (p of periods(); track p) { <td class="r mono">{{ cell(r, p) | number : '1.0-0' }}</td> }
                <td class="r mono tot" style="font-weight:700">{{ r.totalCount | number : '1.0-0' }}</td>
                <td class="r mono" style="font-weight:700">{{ r.totalIncome | number : '1.0-1' }}</td>
              </tr>
            } @empty { <tr><td [attr.colspan]="periods().length + 5" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (pivot().length) {
            <tfoot><tr>
              <td style="font-weight:700">{{ 'total' | t : 'Total' }}</td><td></td><td></td>
              @for (p of periods(); track p) { <td class="r mono" style="font-weight:700">{{ colTotal(p) | number : '1.0-0' }}</td> }
              <td class="r mono tot" style="font-weight:800">{{ totals().count | number : '1.0-0' }}</td>
              <td class="r mono" style="font-weight:800">{{ totals().income | number : '1.0-1' }}</td>
            </tr></tfoot>
          }
        </table>
      }
    </div>

    @if (syncOpen()) {
      <div class="ts-overlay" (click)="syncOpen.set(false)">
        <div class="ts-dlg" (click)="$event.stopPropagation()">
          <div class="ts-dlg-head">
            <h2>{{ 'sync_oracle' | t : 'Sync from Oracle' }}</h2>
            <button class="btn btn-mini btn-s" (click)="syncOpen.set(false)">✕</button>
          </div>
          <div style="padding:16px">
            <div class="small muted" style="margin-bottom:12px">{{ 'sync_range_hint' | t : 'Pull per-test daily statistics from Oracle for this date range and merge them into the existing data.' }}</div>
            @if (syncErr()) { <div class="inline-banner inline-banner-error" style="margin-bottom:12px">{{ syncErr() }}</div> }
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="syncFrom"></div>
              <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="syncTo"></div>
            </div>
          </div>
          <div class="ts-dlg-foot">
            <button class="btn btn-s" (click)="syncOpen.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="syncing()" (click)="runSync()">{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync' | t : 'Sync') }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    th.r,td.r{text-align:right}
    td.tot,th.tot{border-inline-start:2px solid var(--slate-150,#edebe9)}
    tfoot td{border-top:2px solid var(--slate-150,#edebe9);background:var(--white,#fff)}
    .ts-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .ts-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,460px)}
    .ts-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
    .ts-dlg-head h2{font-size:15px;margin:0}
    .ts-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
  `],
})
export class TestStatsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly importing = signal(false);
  readonly summary = signal<string | null>(null);
  readonly summaryError = signal(false);
  readonly rows = signal<TestStat[]>([]);
  readonly groups = signal<Group[]>([]);
  readonly q = signal('');
  readonly group = signal('');
  readonly view = signal<View>('monthly');
  readonly sortDir = signal<'desc' | 'asc'>('desc');
  readonly syncOpen = signal(false);
  readonly syncing = signal(false);
  readonly syncErr = signal<string | null>(null);
  syncFrom = ''; syncTo = '';
  private readonly today = localToday();
  from = this.today.slice(0, 7) + '-01'; to = this.today; // first of the current month → today

  private periodKey(date: string): string { const v = this.view(); return v === 'yearly' ? date.slice(0, 4) : v === 'monthly' ? date.slice(0, 7) : date; }
  colLabel(c: string): string { if (this.view() === 'monthly' && c.length === 7) { const [y, m] = c.split('-'); return `${MO[+m - 1]} ${y}`; } return c; }

  readonly filtered = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.rows().filter((s) =>
      (!q || s.testCode.toLowerCase().includes(q) || (s.testName ?? '').toLowerCase().includes(q)) &&
      (!this.group() || s.groupName === this.group()));
  });
  readonly periods = computed<string[]>(() => {
    const set = new Set<string>();
    for (const s of this.filtered()) set.add(this.periodKey(s.date));
    return [...set].sort((a, b) => a.localeCompare(b));
  });
  readonly pivot = computed<PivotRow[]>(() => {
    const map = new Map<string, PivotRow>();
    for (const s of this.filtered()) {
      const period = this.periodKey(s.date);
      let r = map.get(s.testCode);
      if (!r) { r = { testCode: s.testCode, testName: s.testName ?? '—', groupName: s.groupName ?? '—', cells: {}, totalCount: 0, totalIncome: 0 }; map.set(s.testCode, r); }
      const c = r.cells[period] ?? (r.cells[period] = { count: 0, income: 0 });
      c.count += s.count; c.income += s.income;
      r.totalCount += s.count; r.totalIncome += s.income;
    }
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...map.values()].sort((a, b) => dir * (a.totalCount - b.totalCount) || a.testName.localeCompare(b.testName));
  });
  readonly columnTotals = computed<Record<string, Cell>>(() => {
    const m: Record<string, Cell> = {};
    for (const s of this.filtered()) {
      const p = this.periodKey(s.date);
      const c = m[p] ?? (m[p] = { count: 0, income: 0 });
      c.count += s.count; c.income += s.income;
    }
    return m;
  });
  cell(r: PivotRow, p: string): number { return r.cells[p]?.count ?? 0; }
  colTotal(p: string): number { return this.columnTotals()[p]?.count ?? 0; }
  readonly totals = computed(() => {
    const f = this.filtered();
    return { count: f.reduce((a, s) => a + s.count, 0), income: f.reduce((a, s) => a + s.income, 0), distinct: new Set(f.map((s) => s.testCode)).size };
  });

  constructor() {
    this.load();
    this.api.get<Group[]>('/test-groups').subscribe({ next: (g) => this.groups.set(g) });
  }
  load(): void {
    this.loading.set(true);
    this.api.get<TestStat[]>('/test-statistics', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
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
    this.api.post<{ statsUpserted: number }>('/test-statistics/sync', { from: this.syncFrom, to: this.syncTo }).subscribe({
      next: (r) => {
        this.syncing.set(false); this.syncOpen.set(false); this.summaryError.set(false);
        this.summary.set(`Synced from Oracle: ${r.statsUpserted} test-day record(s) updated for ${this.syncFrom} → ${this.syncTo}.`);
        // Widen the view range to include what was just synced, then reload.
        if (this.syncFrom < this.from) this.from = this.syncFrom;
        if (this.syncTo > this.to) this.to = this.syncTo;
        this.load();
      },
      error: (e) => { this.syncing.set(false); this.syncErr.set(e?.error?.detail ?? 'Oracle sync failed.'); },
    });
  }

  private exportHeaders(): string[] { return ['Test code', 'Test name', 'Parent group', ...this.periods().map((p) => this.colLabel(p)), 'Total count', 'Total income']; }
  private exportRows(): (string | number)[][] {
    const periods = this.periods();
    return this.pivot().map((r) => [r.testCode, r.testName, r.groupName, ...periods.map((p) => this.cell(r, p)), r.totalCount, Math.round(r.totalIncome * 10) / 10]);
  }
  exportExcel(): void { exportCsv(`test-statistics-${this.today}.csv`, this.exportHeaders(), this.exportRows()); }
  exportPdf(): void { printTable('Test statistics', this.exportHeaders(), this.exportRows()); }

  onImport(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0]; if (!file) return;
    this.importing.set(true); this.summary.set(null); this.summaryError.set(false);
    const reader = new FileReader();
    reader.onload = () => {
      const content = String(reader.result).split(',')[1] ?? '';
      this.api.post<{ processed: number; upserted: number; skipped: number; warnings: string[] }>('/test-statistics/import', { content }).subscribe({
        next: (s) => { this.importing.set(false); this.summary.set(`Imported ${s.processed}: ${s.upserted} upserted, ${s.skipped} skipped${s.warnings.length ? ' · ' + s.warnings.length + ' warning(s)' : ''}.`); input.value = ''; this.load(); },
        error: (e) => { this.importing.set(false); this.summaryError.set(true); this.summary.set(e?.error?.detail ?? 'Import failed.'); input.value = ''; },
      });
    };
    reader.readAsDataURL(file);
  }
}
