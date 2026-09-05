import { exportXlsx, localToday, printTable, SheetCell } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';

interface DetailRow {
  date: string; governorate: string | null; city: string | null; area: string | null;
  category: string | null; branch: string | null; labCode: string | null; labName: string | null;
  accNo: string; patientName: string; testCode: string; testType: number; testName: string | null; fee: number;
}
/** A grid row with repeated group cells blanked (grouped look); the raw values stay for export. */
interface GridRow {
  gov: string; city: string; area: string; lab: string; date: string; labTotal: number | '';
  patient: string; accession: string; patientTotal: number | ''; test: string; fee: number;
  newLab: boolean; newPatient: boolean;
}
const DASH = '—';
const NOLAB = 'No lab';

@Component({
  selector: 'app-detailedstats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'detailedstats' | t : 'Detailed statistics' }}</div><h1>{{ 'detailedstats' | t : 'Detailed statistics' }}</h1></div>
      <div class="pagehead-actions">
        <button class="btn btn-s" [disabled]="syncing()" (click)="openSync()" title="{{ 'sync_oracle_hint_detailed' | t : 'Pull detailed registrations from Oracle for a date range' }}">
          <i data-lucide="database" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
        </button>
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>
    <div class="small muted" style="margin-bottom:10px">{{ 'detailedstats_hint' | t : 'Transaction-level test lines grouped by governorate → city → area → lab → date → patient. Detailed data syncs automatically each night.' }}</div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'test_lines' | t : 'Test lines' }}</div><div class="val">{{ k().lines | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_fees' | t : 'Total fees' }}</div><div class="val">{{ k().fee | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'labs' | t : 'Labs' }}</div><div class="val">{{ k().labs }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'patients' | t : 'Patients' }}</div><div class="val">{{ k().patients }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><app-filter-select [multiple]="true" [options]="govs()" [ngModel]="gov()" (ngModelChange)="gov.set($event); city.set([]); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><app-filter-select [multiple]="true" [options]="cities()" [ngModel]="city()" (ngModelChange)="city.set($event); area.set([])" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [multiple]="true" [options]="areas()" [ngModel]="area()" (ngModelChange)="area.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'category' | t : 'Lab Category' }}</label><app-filter-select [multiple]="true" [options]="categories()" [ngModel]="category()" (ngModelChange)="category.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'serving_branch' | t : 'Serving branch' }}</label><app-filter-select [multiple]="true" [options]="branches()" [ngModel]="branch()" (ngModelChange)="branch.set($event)" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th class="stick">{{ 'governorate_2' | t : 'Governorate' }}</th>
            <th>{{ 'city' | t : 'City' }}</th><th>{{ 'area_2' | t : 'Area' }}</th><th>{{ 'lab_name' | t : 'Lab' }}</th>
            <th>{{ 'reg_date' | t : 'Reg Date' }}</th><th class="r">{{ 'lab_total_required' | t : 'Lab Total' }}</th>
            <th>{{ 'patient_name' | t : 'Patient' }}</th><th>{{ 'acc_no' | t : 'Accession' }}</th><th class="r">{{ 'patient_total_required' | t : 'Patient Total' }}</th>
            <th>{{ 'test_name_2' | t : 'Test' }}</th><th class="r">{{ 'test_fee' | t : 'Test Fee' }}</th>
          </tr></thead>
          <tbody>
            @for (r of paged(); track $index) {
              <tr [class.lab-row]="r.newLab">
                <td class="stick">{{ r.gov }}</td><td>{{ r.city }}</td><td>{{ r.area }}</td><td>{{ r.lab }}</td>
                <td class="mono">{{ r.date }}</td><td class="r mono tot">{{ r.labTotal === '' ? '' : (r.labTotal | number:'1.0-2') }}</td>
                <td>{{ r.patient }}</td><td class="mono">{{ r.accession }}</td><td class="r mono">{{ r.patientTotal === '' ? '' : (r.patientTotal | number:'1.0-2') }}</td>
                <td>{{ r.test }}</td><td class="r mono">{{ r.fee | number:'1.0-2' }}</td>
              </tr>
            } @empty { <tr><td colspan="11" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table>
        @if (grid().length) {
          <div class="fu-pager">
            <button class="btn-ghost" [disabled]="curPage() <= 1" (click)="page.set(curPage() - 1)">‹ {{ 'prev' | t : 'Prev' }}</button>
            <span>{{ 'page' | t : 'Page' }} {{ curPage() }} / {{ pageCount() }} · {{ grid().length | number:'1.0-0' }} {{ 'rows_2' | t : 'row(s)' }}</span>
            <button class="btn-ghost" [disabled]="curPage() >= pageCount()" (click)="page.set(curPage() + 1)">{{ 'next' | t : 'Next' }} ›</button>
            <select class="select" [ngModel]="pageSize()" (ngModelChange)="pageSize.set(+$event); page.set(1)" style="max-width:90px;margin-inline-start:auto">
              <option [ngValue]="50">50</option><option [ngValue]="100">100</option><option [ngValue]="200">200</option>
            </select>
          </div>
        }
      }
    </div>

    @if (syncOpen()) {
      <div class="ds-overlay" (click)="syncOpen.set(false)">
        <div class="ds-dlg" (click)="$event.stopPropagation()">
          <div class="ds-dlg-head">
            <h2>{{ 'sync_oracle' | t : 'Sync from Oracle' }}</h2>
            <button class="btn btn-mini btn-s" (click)="syncOpen.set(false)">✕</button>
          </div>
          <div style="padding:16px">
            <div class="small muted" style="margin-bottom:12px">{{ 'sync_range_hint_detailed' | t : 'Pull detailed registration test-lines from Oracle for this date range and replace the stored data for it. The last day is also synced automatically every night.' }}</div>
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="syncFrom"></app-date-input></div>
              <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="syncTo"></app-date-input></div>
            </div>
          </div>
          <div class="ds-dlg-foot">
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
    tr.lab-row td{border-top:2px solid var(--slate-150,#edebe9)}
    tr.lab-row td.stick{font-weight:700}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150,#edebe9);font-size:12.5px;color:var(--slate-700,#605e5c)}
    .ds-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .ds-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(94vw,460px)}
    .ds-dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
    .ds-dlg-head h2{font-size:15px;margin:0}
    .ds-dlg-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
  `],
})
export class DetailedStatsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly loading = signal(true);
  readonly rows = signal<DetailRow[]>([]);
  readonly gov = signal<string[]>([]);
  readonly city = signal<string[]>([]);
  readonly area = signal<string[]>([]);
  readonly category = signal<string[]>([]);
  readonly branch = signal<string[]>([]);
  readonly page = signal(1);
  readonly pageSize = signal(100);
  readonly syncOpen = signal(false);
  readonly syncing = signal(false);
  syncFrom = ''; syncTo = '';
  private readonly today = localToday();
  from = this.yesterday(); to = this.today; // default: yesterday → today

  private yesterday(): string { const d = new Date(); d.setDate(d.getDate() - 1); return this.ymd(d); }
  private ymd(d: Date): string { return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; }

  // Cascading option lists (city narrows by governorate, area by city).
  readonly govs = computed(() => [...new Set(this.rows().map((s) => s.governorate ?? NOLAB))].sort());
  readonly cities = computed(() => [...new Set(this.rows().filter((s) => !this.gov().length || this.gov().includes(s.governorate ?? NOLAB)).map((s) => s.city ?? DASH))].sort());
  readonly areas = computed(() => [...new Set(this.rows()
    .filter((s) => (!this.gov().length || this.gov().includes(s.governorate ?? NOLAB)) && (!this.city().length || this.city().includes(s.city ?? DASH)))
    .map((s) => s.area ?? DASH))].sort());
  readonly categories = computed(() => [...new Set(this.rows().map((s) => s.category ?? DASH))].sort());
  readonly branches = computed(() => [...new Set(this.rows().map((s) => s.branch ?? DASH))].sort());

  private matches(s: DetailRow): boolean {
    return (!this.gov().length || this.gov().includes(s.governorate ?? NOLAB)) &&
      (!this.city().length || this.city().includes(s.city ?? DASH)) &&
      (!this.area().length || this.area().includes(s.area ?? DASH)) &&
      (!this.category().length || this.category().includes(s.category ?? DASH)) &&
      (!this.branch().length || this.branch().includes(s.branch ?? DASH));
  }

  // Filtered rows sorted by the reporting hierarchy.
  readonly sorted = computed<DetailRow[]>(() => {
    const cmp = (a: string, b: string) => a.localeCompare(b);
    return this.rows().filter((s) => this.matches(s)).sort((a, b) =>
      cmp(a.governorate ?? NOLAB, b.governorate ?? NOLAB) ||
      cmp(a.city ?? '', b.city ?? '') || cmp(a.area ?? '', b.area ?? '') ||
      cmp(a.labName ?? a.labCode ?? '', b.labName ?? b.labCode ?? '') || cmp(a.date, b.date) ||
      cmp(a.patientName, b.patientName) || cmp(a.accNo, b.accNo) || cmp(a.testName ?? a.testCode, b.testName ?? b.testCode));
  });

  private readonly labTotals = computed<Record<string, number>>(() => {
    const m: Record<string, number> = {};
    for (const s of this.sorted()) { const key = (s.labCode ?? NOLAB) + '|' + s.date; m[key] = (m[key] ?? 0) + s.fee; }
    return m;
  });
  private readonly patientTotals = computed<Record<string, number>>(() => {
    const m: Record<string, number> = {};
    for (const s of this.sorted()) { const key = (s.labCode ?? NOLAB) + '|' + s.date + '|' + s.accNo; m[key] = (m[key] ?? 0) + s.fee; }
    return m;
  });

  // Display rows: repeated group cells blanked so the hierarchy reads top-down.
  readonly grid = computed<GridRow[]>(() => {
    const labT = this.labTotals(); const patT = this.patientTotals();
    let prevLab = ''; let prevPat = '';
    return this.sorted().map((s) => {
      const labKey = (s.labCode ?? NOLAB) + '|' + s.date;
      const patKey = labKey + '|' + s.accNo;
      const newLab = labKey !== prevLab; prevLab = labKey;
      const newPat = patKey !== prevPat; prevPat = patKey;
      const labName = s.labName ? `${s.labName}${s.labCode ? ' (' + s.labCode + ')' : ''}` : (s.labCode ?? NOLAB);
      return {
        gov: newLab ? (s.governorate ?? NOLAB) : '', city: newLab ? (s.city ?? DASH) : '', area: newLab ? (s.area ?? DASH) : '',
        lab: newLab ? labName : '', date: newLab ? s.date : '', labTotal: newLab ? (labT[labKey] ?? 0) : '',
        patient: newPat ? s.patientName : '', accession: newPat ? s.accNo : '', patientTotal: newPat ? (patT[patKey] ?? 0) : '',
        test: s.testName ?? s.testCode, fee: s.fee, newLab, newPatient: newPat,
      };
    });
  });

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.grid().length / this.pageSize())));
  readonly curPage = computed(() => Math.min(this.page(), this.pageCount()));
  readonly paged = computed<GridRow[]>(() => {
    const start = (this.curPage() - 1) * this.pageSize();
    return this.grid().slice(start, start + this.pageSize());
  });

  readonly k = computed(() => {
    const f = this.sorted();
    const labs = new Set(f.filter((s) => s.labCode).map((s) => s.labCode)).size;
    const patients = new Set(f.map((s) => (s.labCode ?? NOLAB) + '|' + s.date + '|' + s.accNo)).size;
    return { lines: f.length, fee: f.reduce((a, s) => a + s.fee, 0), labs, patients };
  });

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true); this.page.set(1);
    this.api.get<DetailRow[]>('/detailed-statistics', { from: this.from, to: this.to }).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  openSync(): void {
    const y = new Date(); y.setDate(y.getDate() - 1);
    this.syncFrom = this.ymd(y); this.syncTo = this.today; // default: yesterday → today
    this.syncOpen.set(true);
  }
  runSync(): void {
    if (!this.syncFrom || !this.syncTo) { this.toast.warning('Please choose a start and end date.'); return; }
    if (this.syncFrom > this.syncTo) { this.toast.warning('The start date must be on or before the end date.'); return; }
    this.syncing.set(true);
    this.api.post<{ statsUpserted: number }>('/detailed-statistics/sync', { from: this.syncFrom, to: this.syncTo }).subscribe({
      next: (r) => {
        this.syncing.set(false); this.syncOpen.set(false);
        this.toast.success(`Synced from Oracle: ${r.statsUpserted} registration line(s) for ${this.syncFrom} → ${this.syncTo}.`);
        if (this.syncFrom < this.from) this.from = this.syncFrom;
        if (this.syncTo > this.to) this.to = this.syncTo;
        this.load();
      },
      error: () => { this.syncing.set(false); },
    });
  }

  private exportHeaders(): string[] {
    return ['Governorate', 'City', 'Area', 'Lab', 'Reg Date', 'Lab Total', 'Patient', 'Accession', 'Patient Total', 'Test', 'Test Fee'];
  }
  /** Export rows: full values on every line (no blanking) so Excel/PDF can be filtered and pivoted. */
  private exportRows(): SheetCell[][] {
    const labT = this.labTotals(); const patT = this.patientTotals();
    const dec = (v: number) => Math.round(v * 100) / 100;
    return this.sorted().map((s) => {
      const labKey = (s.labCode ?? NOLAB) + '|' + s.date;
      const patKey = labKey + '|' + s.accNo;
      const labName = s.labName ? `${s.labName}${s.labCode ? ' (' + s.labCode + ')' : ''}` : (s.labCode ?? NOLAB);
      return [s.governorate ?? NOLAB, s.city ?? DASH, s.area ?? DASH, labName, s.date, dec(labT[labKey] ?? 0),
        s.patientName, s.accNo, dec(patT[patKey] ?? 0), s.testName ?? s.testCode, dec(s.fee)];
    });
  }
  exportExcel(): void { exportXlsx(`detailed-statistics-${this.today}.xlsx`, this.exportHeaders(), this.exportRows()); }
  exportPdf(): void { printTable('Detailed statistics', this.exportHeaders(), this.exportRows() as (string | number)[][]); }
}
