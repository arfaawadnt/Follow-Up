import { exportCsv, localToday, printTable } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabStat } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface AggRow { labCode: string; name: string; category: string; segment: string; governorate: string; city: string; area: string; totalTests: number; totalIncome: number; }
type View = 'daily' | 'monthly' | 'yearly';

@Component({
  selector: 'app-labstats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'labstats' | t : 'Lab statistics' }}</div><h1>{{ 'labstats' | t : 'Lab statistics' }}</h1></div>
      @if (auth.has('ViewLabStats')) {
        <div class="pagehead-actions">
          <input type="file" #fileIn accept=".xlsx,.xls,.csv" hidden (change)="onImport($event)">
          <button class="btn btn-s" [disabled]="importing()" (click)="fileIn.click()">
            <i data-lucide="upload" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ importing() ? ('importing' | t : 'Importing…') : ('import_excel' | t : 'Import Excel') }}
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
      <div class="kpi kpi-blue"><div class="lbl">{{ 'active_labs_stats' | t : 'Labs in stats' }}</div><div class="val">{{ k().labs }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'avg_tests_lab' | t : 'Avg tests / lab' }}</div><div class="val">{{ k().avg | number:'1.0-0' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'view' | t : 'View' }}</label><select class="select" [(ngModel)]="view"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
        <div class="field"><label>{{ 'search' | t : 'Search lab' }}</label><input class="input" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="name or code"></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><select class="select" [ngModel]="gov()" (ngModelChange)="gov.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (g of govs(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><select class="select" [ngModel]="city()" (ngModelChange)="city.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (c of cities(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><select class="select" [ngModel]="area()" (ngModelChange)="area.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (a of areas(); track a) { <option [value]="a">{{ a }}</option> }</select></div>
        <div class="field"><label>{{ 'segment' | t : 'Segment' }}</label><select class="select" [ngModel]="segment()" (ngModelChange)="segment.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (s of segments(); track s) { <option [value]="s">{{ s }}</option> }</select></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th style="position:sticky;left:0;background:var(--white)">{{ 'lab_name' | t : 'Lab name' }}</th>
            <th>{{ 'category' | t : 'Category' }}</th><th>{{ 'segment' | t }}</th>
            <th>{{ 'governorate_2' | t }}</th><th>{{ 'city' | t : 'City' }}</th><th>{{ 'area_2' | t }}</th>
            <th class="r">{{ 'total_tests' | t : 'Total tests' }}</th><th class="r">{{ 'total_income' | t : 'Total income' }}</th>
          </tr></thead>
          <tbody>
            @for (r of agg(); track r.labCode) {
              <tr>
                <td style="font-weight:600;position:sticky;left:0;background:var(--white)">{{ r.name }}<div class="small muted mono">{{ r.labCode }}</div></td>
                <td>{{ r.category }}</td>
                <td><span class="badge b-info">{{ r.segment }}</span></td>
                <td>{{ r.governorate }}</td><td>{{ r.city }}</td><td>{{ r.area }}</td>
                <td class="r mono" style="font-weight:700">{{ r.totalTests | number:'1.0-0' }}</td>
                <td class="r mono" style="font-weight:700">{{ r.totalIncome | number:'1.0-1' }}</td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`th.r,td.r{text-align:right}`],
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
  readonly gov = signal('');
  readonly city = signal('');
  readonly area = signal('');
  readonly segment = signal('');
  private readonly today = localToday();
  from = new Date(Date.now() - 90 * 864e5).toISOString().slice(0, 10); to = this.today; view: View = 'monthly';

  readonly govs = computed(() => [...new Set(this.rows().map((s) => s.governorate).filter((v): v is string => !!v))].sort());
  readonly cities = computed(() => [...new Set(this.rows().map((s) => s.city).filter((v): v is string => !!v))].sort());
  readonly areas = computed(() => [...new Set(this.rows().map((s) => s.area).filter((v): v is string => !!v))].sort());
  readonly segments = computed(() => [...new Set(this.rows().map((s) => s.segment).filter((v): v is string => !!v))].sort());

  readonly filtered = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.rows().filter((s) =>
      (!q || s.labCode.toLowerCase().includes(q) || (s.name ?? '').toLowerCase().includes(q)) &&
      (!this.gov() || s.governorate === this.gov()) &&
      (!this.city() || s.city === this.city()) &&
      (!this.area() || s.area === this.area()) &&
      (!this.segment() || s.segment === this.segment()));
  });
  readonly agg = computed<AggRow[]>(() => {
    const map = new Map<string, AggRow>();
    for (const s of this.filtered()) {
      let r = map.get(s.labCode);
      if (!r) { r = { labCode: s.labCode, name: s.name ?? s.labCode, category: s.category ?? '—', segment: s.segment ?? '—', governorate: s.governorate ?? '—', city: s.city ?? '—', area: s.area ?? '—', totalTests: 0, totalIncome: 0 }; map.set(s.labCode, r); }
      r.totalTests += s.testCount; r.totalIncome += s.income;
    }
    return [...map.values()].sort((a, b) => b.totalTests - a.totalTests);
  });
  readonly k = computed(() => {
    const f = this.filtered();
    const tests = f.reduce((a, s) => a + s.testCount, 0);
    const labs = new Set(f.map((s) => s.labCode)).size;
    return { tests, income: f.reduce((a, s) => a + s.income, 0), labs, avg: labs ? tests / labs : 0 };
  });

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<LabStat[]>('/labstats', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private exportRows(): (string | number)[][] {
    return this.agg().map((r) => [r.name, r.labCode, r.category, r.segment, r.governorate, r.city, r.area, r.totalTests, Math.round(r.totalIncome * 10) / 10]);
  }
  exportExcel(): void { exportCsv(`lab-statistics-${this.today}.csv`, ['Lab name', 'Code', 'Category', 'Segment', 'Governorate', 'City', 'Area', 'Total tests', 'Total income'], this.exportRows()); }
  exportPdf(): void { printTable('Lab statistics', ['Lab name', 'Code', 'Category', 'Segment', 'Governorate', 'City', 'Area', 'Total tests', 'Total income'], this.exportRows()); }

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
