import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface LabStat { date: string; labCode: string; registrations: number; testCount: number; income: number; }

@Component({
  selector: 'app-labstats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'labstats' | t : 'Lab statistics' }}</div><h1>{{ 'labstats' | t : 'Lab statistics' }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'registrations' | t : 'Registrations' }}</div><div class="val">{{ k().reg | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'tests' | t : 'Tests' }}</div><div class="val">{{ k().tests | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'income' | t : 'Income' }}</div><div class="val">{{ k().income | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'rows' | t : 'Rows' }}</div><div class="val">{{ rows().length }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'search' | t : 'Search lab' }}</label><input class="input" [(ngModel)]="q"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'code_2' | t : 'Lab' }}</th><th>{{ 'registrations' | t : 'Registrations' }}</th><th>{{ 'tests' | t : 'Tests' }}</th><th>{{ 'income' | t : 'Income' }}</th></tr></thead>
          <tbody>
            @for (s of filtered(); track $index) {
              <tr><td class="mono small">{{ s.date }}</td><td class="mono">{{ s.labCode }}</td><td class="mono">{{ s.registrations | number:'1.0-0' }}</td><td class="mono">{{ s.testCount | number:'1.0-0' }}</td><td class="mono">{{ s.income | number:'1.0-2' }}</td></tr>
            } @empty { <tr><td colspan="5" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No statistics in range.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
})
export class LabStatsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<LabStat[]>([]);
  private readonly today = new Date().toISOString().slice(0, 10);
  from = new Date(Date.now() - 30 * 864e5).toISOString().slice(0, 10); to = this.today; q = '';

  readonly filtered = computed(() => { const q = this.q.trim().toLowerCase(); return this.rows().filter((s) => !q || s.labCode.toLowerCase().includes(q)); });
  readonly k = computed(() => { const f = this.filtered(); return { reg: f.reduce((a, s) => a + s.registrations, 0), tests: f.reduce((a, s) => a + s.testCount, 0), income: f.reduce((a, s) => a + s.income, 0) }; });

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<LabStat[]>('/labstats', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
}
