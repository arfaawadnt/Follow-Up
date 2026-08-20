import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface TestStat { date: string; testCode: string; count: number; }

@Component({
  selector: 'app-teststats',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'teststats' | t : 'Test statistics' }}</div><h1>{{ 'teststats' | t : 'Test statistics' }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'tests' | t : 'Total tests' }}</div><div class="val">{{ total() | number:'1.0-0' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'distinct_tests' | t : 'Distinct tests' }}</div><div class="val">{{ distinct() }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'rows' | t : 'Rows' }}</div><div class="val">{{ filtered().length }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'search' | t : 'Search test' }}</label><input class="input" [(ngModel)]="q"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'test_code' | t : 'Test' }}</th><th>{{ 'count' | t : 'Count' }}</th></tr></thead>
          <tbody>
            @for (s of filtered(); track $index) { <tr><td class="mono small">{{ s.date }}</td><td class="mono">{{ s.testCode }}</td><td class="mono">{{ s.count | number:'1.0-0' }}</td></tr> }
            @empty { <tr><td colspan="3" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No statistics in range.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
})
export class TestStatsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<TestStat[]>([]);
  private readonly today = new Date().toISOString().slice(0, 10);
  from = new Date(Date.now() - 30 * 864e5).toISOString().slice(0, 10); to = this.today; q = '';

  readonly filtered = computed(() => { const q = this.q.trim().toLowerCase(); return this.rows().filter((s) => !q || s.testCode.toLowerCase().includes(q)); });
  readonly total = computed(() => this.filtered().reduce((a, s) => a + s.count, 0));
  readonly distinct = computed(() => new Set(this.filtered().map((s) => s.testCode)).size);

  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<TestStat[]>('/test-statistics', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
}
