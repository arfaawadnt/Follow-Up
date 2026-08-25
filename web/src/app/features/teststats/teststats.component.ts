import { localToday } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface TestStat { date: string; testCode: string; count: number; }
interface PivotRow { testCode: string; total: number; periods: Record<string, number>; }
type View = 'daily' | 'monthly' | 'yearly';
const MO = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

@Component({
  selector: 'app-teststats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'teststats' | t : 'Test statistics' }}</div><h1>{{ 'teststats' | t : 'Test statistics' }}</h1></div>
      @if (auth.has('AddTeststats')) {
        <div class="pagehead-actions">
          <input type="file" #fileIn accept=".xlsx,.xls,.csv" hidden (change)="onImport($event)">
          <button class="btn btn-s" [disabled]="importing()" (click)="fileIn.click()">
            <i data-lucide="upload" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ importing() ? ('importing' | t : 'Importing…') : ('import_excel' | t : 'Import Excel') }}
          </button>
        </div>
      }
    </div>
    @if (summary()) { <div class="inline-banner" [class.inline-banner-error]="summaryError()">{{ summary() }}</div> }

    <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'total_tests' | t : 'Total tests' }}</div><div class="val">{{ total() | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'distinct_tests' | t : 'Distinct tests' }}</div><div class="val">{{ pivot().length }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'periods' | t : 'Periods' }}</div><div class="val">{{ cols().length }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'view' | t : 'View' }}</label><select class="select" [(ngModel)]="view"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'search' | t : 'Test' }}</label><input class="input" [(ngModel)]="q"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th style="position:sticky;left:0;background:var(--white)">{{ 'test_code' | t : 'Test' }}</th>
            <th class="r">{{ 'total' | t : 'Total' }}</th>
            @for (c of cols(); track c) { <th class="r">{{ colLabel(c) }}</th> }
          </tr></thead>
          <tbody>
            @for (r of pivot(); track r.testCode) {
              <tr>
                <td class="mono" style="font-weight:600;position:sticky;left:0;background:var(--white)">{{ r.testCode }}</td>
                <td class="r mono" style="font-weight:700">{{ r.total | number:'1.0-0' }}</td>
                @for (c of cols(); track c) { <td class="r mono">{{ (r.periods[c] || 0) | number:'1.0-0' }}</td> }
              </tr>
            } @empty { <tr><td [attr.colspan]="2 + cols().length" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
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
  private readonly today = localToday();
  from = new Date(Date.now() - 90 * 864e5).toISOString().slice(0, 10); to = this.today; q = ''; view: View = 'monthly';

  private periodKey(date: string): string { return this.view === 'yearly' ? date.slice(0, 4) : this.view === 'monthly' ? date.slice(0, 7) : date; }
  colLabel(c: string): string { if (this.view === 'yearly') return c; if (this.view === 'monthly') { const [y, m] = c.split('-'); return `${MO[+m - 1]} ${y}`; } return c; }

  readonly filtered = computed(() => { const q = this.q.trim().toLowerCase(); return this.rows().filter((s) => !q || s.testCode.toLowerCase().includes(q)); });
  readonly cols = computed(() => [...new Set(this.filtered().map((s) => this.periodKey(s.date)))].sort());
  readonly pivot = computed<PivotRow[]>(() => {
    const map = new Map<string, PivotRow>();
    for (const s of this.filtered()) {
      let r = map.get(s.testCode);
      if (!r) { r = { testCode: s.testCode, total: 0, periods: {} }; map.set(s.testCode, r); }
      r.total += s.count; const k = this.periodKey(s.date); r.periods[k] = (r.periods[k] || 0) + s.count;
    }
    return [...map.values()].sort((a, b) => b.total - a.total);
  });
  readonly total = computed(() => this.filtered().reduce((a, s) => a + s.count, 0));

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<TestStat[]>('/test-statistics', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

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
