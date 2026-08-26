import { exportCsv, localToday, printTable } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface TestStat { date: string; testCode: string; testName: string | null; groupName: string | null; count: number; income: number; }
interface Row { period: string; testCode: string; testName: string; groupName: string; count: number; income: number; }
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
        }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>
    @if (summary()) { <div class="inline-banner" [class.inline-banner-error]="summaryError()">{{ summary() }}</div> }
    <div class="small muted" style="margin-bottom:10px">{{ 'import_hint_tests' | t : 'Import columns: Date, TestCode, Count, Income.' }}</div>

    <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'total_tests' | t : 'Total tests' }}</div><div class="val">{{ totals().count | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_income' | t : 'Total income' }}</div><div class="val">{{ totals().income | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'distinct_tests' | t : 'Distinct tests' }}</div><div class="val">{{ totals().distinct }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(6,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'view' | t : 'View by' }}</label><select class="select" [ngModel]="view()" (ngModelChange)="view.set($event)"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'search' | t : 'Search' }}</label><input class="input" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="test name or code"></div>
        <div class="field"><label>{{ 'group' | t : 'Group' }}</label><select class="select" [ngModel]="group()" (ngModelChange)="group.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (g of groups(); track g.id) { <option [value]="g.nameEn">{{ g.nameEn }}</option> }</select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ view() === 'daily' ? ('date' | t : 'Date') : ('period' | t : 'Period') }}</th>
            <th>{{ 'test_code' | t : 'Test code' }}</th><th>{{ 'test_name' | t : 'Test name' }}</th><th>{{ 'parent_group' | t : 'Parent group' }}</th>
            <th class="r">{{ 'count' | t : 'Count' }}</th><th class="r">{{ 'income' | t : 'Income' }}</th>
          </tr></thead>
          <tbody>
            @for (r of table(); track r.period + r.testCode) {
              <tr>
                <td class="mono small">{{ colLabel(r.period) }}</td>
                <td class="mono" style="font-weight:600">{{ r.testCode }}</td>
                <td>{{ r.testName }}</td>
                <td>@if (r.groupName !== '—') { <span class="badge b-neu">{{ r.groupName }}</span> } @else { — }</td>
                <td class="r mono" style="font-weight:700">{{ r.count | number:'1.0-0' }}</td>
                <td class="r mono">{{ r.income | number:'1.0-1' }}</td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`th.r,td.r{text-align:right}`],
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
  private readonly today = localToday();
  from = new Date(Date.now() - 90 * 864e5).toISOString().slice(0, 10); to = this.today;

  private periodKey(date: string): string { const v = this.view(); return v === 'yearly' ? date.slice(0, 4) : v === 'monthly' ? date.slice(0, 7) : date; }
  colLabel(c: string): string { if (this.view() === 'monthly' && c.length === 7) { const [y, m] = c.split('-'); return `${MO[+m - 1]} ${y}`; } return c; }

  readonly filtered = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.rows().filter((s) =>
      (!q || s.testCode.toLowerCase().includes(q) || (s.testName ?? '').toLowerCase().includes(q)) &&
      (!this.group() || s.groupName === this.group()));
  });
  readonly table = computed<Row[]>(() => {
    const map = new Map<string, Row>();
    for (const s of this.filtered()) {
      const period = this.periodKey(s.date);
      const key = `${period}|${s.testCode}`;
      let r = map.get(key);
      if (!r) { r = { period, testCode: s.testCode, testName: s.testName ?? '—', groupName: s.groupName ?? '—', count: 0, income: 0 }; map.set(key, r); }
      r.count += s.count; r.income += s.income;
    }
    return [...map.values()].sort((a, b) => b.period.localeCompare(a.period) || b.count - a.count);
  });
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

  private exportRows(): (string | number)[][] {
    return this.table().map((r) => [this.colLabel(r.period), r.testCode, r.testName, r.groupName, r.count, Math.round(r.income * 10) / 10]);
  }
  exportExcel(): void { exportCsv(`test-statistics-${this.today}.csv`, ['Period', 'Test code', 'Test name', 'Parent group', 'Count', 'Income'], this.exportRows()); }
  exportPdf(): void { printTable('Test statistics', ['Period', 'Test code', 'Test name', 'Parent group', 'Count', 'Income'], this.exportRows()); }

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
