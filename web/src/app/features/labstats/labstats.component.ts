import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface LabStat { date: string; labCode: string; name: string | null; segment: string | null; governorate: string | null; city: string | null; area: string | null; registrations: number; testCount: number; income: number; }
interface PivotRow { labCode: string; name: string; segment: string; governorate: string; city: string; area: string; totalTests: number; totalIncome: number; periods: Record<string, number>; }
type View = 'daily' | 'monthly' | 'yearly';
const MO = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

@Component({
  selector: 'app-labstats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'labstats' | t : 'Lab statistics' }}</div><h1>{{ 'labstats' | t : 'Lab statistics' }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_tests' | t : 'Total tests' }}</div><div class="val">{{ k().tests | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_income' | t : 'Total income' }}</div><div class="val">{{ k().income | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'total_labs' | t : 'Labs' }}</div><div class="val">{{ k().labs }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'registrations' | t : 'Registrations' }}</div><div class="val">{{ k().reg | number:'1.0-0' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'view' | t : 'View' }}</label><select class="select" [(ngModel)]="view"><option value="daily">{{ 'daily_2' | t : 'Daily' }}</option><option value="monthly">{{ 'monthly' | t : 'Monthly' }}</option><option value="yearly">{{ 'yearly' | t : 'Yearly' }}</option></select></div>
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'search' | t : 'Lab' }}</label><input class="input" [(ngModel)]="q"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th style="position:sticky;left:0;background:var(--white)">{{ 'laboratory_3' | t : 'Laboratory' }}</th>
            <th>{{ 'segment' | t }}</th><th>{{ 'governorate_2' | t }}</th><th>{{ 'area_2' | t }}</th>
            <th class="r">{{ 'total_tests' | t : 'Total tests' }}</th><th class="r">{{ 'total_income' | t : 'Income' }}</th>
            @for (c of cols(); track c) { <th class="r">{{ colLabel(c) }}</th> }
          </tr></thead>
          <tbody>
            @for (r of pivot(); track r.labCode) {
              <tr>
                <td style="font-weight:600;position:sticky;left:0;background:var(--white)">{{ r.name }}<div class="small muted mono">{{ r.labCode }}</div></td>
                <td><span class="badge b-info">{{ r.segment }}</span></td><td>{{ r.governorate }}</td><td>{{ r.area }}</td>
                <td class="r mono" style="font-weight:700">{{ r.totalTests | number:'1.0-0' }}</td>
                <td class="r mono" style="font-weight:700">{{ r.totalIncome | number:'1.0-1' }}</td>
                @for (c of cols(); track c) { <td class="r mono">{{ (r.periods[c] || 0) | number:'1.0-0' }}</td> }
              </tr>
            } @empty { <tr><td [attr.colspan]="6 + cols().length" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`th.r,td.r{text-align:right}`],
})
export class LabStatsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<LabStat[]>([]);
  private readonly today = new Date().toISOString().slice(0, 10);
  from = new Date(Date.now() - 90 * 864e5).toISOString().slice(0, 10); to = this.today; q = ''; view: View = 'monthly';

  private periodKey(date: string): string {
    if (this.view === 'yearly') return date.slice(0, 4);
    if (this.view === 'monthly') return date.slice(0, 7);
    return date;
  }
  colLabel(c: string): string {
    if (this.view === 'yearly') return c;
    if (this.view === 'monthly') { const [y, m] = c.split('-'); return `${MO[+m - 1]} ${y}`; }
    return c;
  }

  readonly filtered = computed(() => { const q = this.q.trim().toLowerCase(); return this.rows().filter((s) => !q || s.labCode.toLowerCase().includes(q) || (s.name ?? '').toLowerCase().includes(q)); });
  readonly cols = computed(() => [...new Set(this.filtered().map((s) => this.periodKey(s.date)))].sort());
  readonly pivot = computed<PivotRow[]>(() => {
    const map = new Map<string, PivotRow>();
    for (const s of this.filtered()) {
      let r = map.get(s.labCode);
      if (!r) { r = { labCode: s.labCode, name: s.name ?? s.labCode, segment: s.segment ?? '—', governorate: s.governorate ?? '—', city: s.city ?? '—', area: s.area ?? '—', totalTests: 0, totalIncome: 0, periods: {} }; map.set(s.labCode, r); }
      r.totalTests += s.testCount; r.totalIncome += s.income;
      const k = this.periodKey(s.date); r.periods[k] = (r.periods[k] || 0) + s.testCount;
    }
    return [...map.values()].sort((a, b) => b.totalTests - a.totalTests);
  });
  readonly k = computed(() => { const f = this.filtered(); return { tests: f.reduce((a, s) => a + s.testCount, 0), income: f.reduce((a, s) => a + s.income, 0), labs: new Set(f.map((s) => s.labCode)).size, reg: f.reduce((a, s) => a + s.registrations, 0) }; });

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<LabStat[]>('/labstats', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
}
