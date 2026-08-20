import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface Row {
  collectorName: string; labName: string; labCode: string; visitDate: string; visitTime: string; samples: number | null;
  plannedToCollect: number | null; collectToTransfer: number | null; transferToCheckin: number | null; totalCycle: number | null;
  checkinTime: string | null; transferTime: string | null; markedAt: string | null;
}

@Component({
  selector: 'app-repintervals',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'rep_intervals' | t }}</div><h1>{{ 'rep_intervals' | t }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'planned_collect_delay' | t : 'Planned → Collect' }}</div><div class="val" style="font-size:22px">{{ dur(avg('plannedToCollect')) }}</div><div class="sub">{{ 'avg_delay' | t : 'avg delay' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'collect_to_transfer' | t : 'Collect → Transfer' }}</div><div class="val" style="font-size:22px">{{ dur(avg('collectToTransfer')) }}</div><div class="sub">{{ 'avg' | t : 'avg' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'transfer_to_checkin' | t : 'Transfer → Check-in' }}</div><div class="val" style="font-size:22px">{{ dur(avg('transferToCheckin')) }}</div><div class="sub">{{ 'avg' | t : 'avg' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_cycle_time' | t : 'Total cycle' }}</div><div class="val" style="font-size:22px">{{ dur(avg('totalCycle')) }}</div><div class="sub">{{ 'avg' | t : 'avg' }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="start"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="end"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'collector' | t }}</th><th>{{ 'samples_2' | t : 'Samples' }}</th>
            <th>{{ 'planned_collect_delay' | t : 'Planned→Collect' }}</th><th>{{ 'collect_to_transfer' | t : 'Collect→Transfer' }}</th>
            <th>{{ 'transfer_to_checkin' | t : 'Transfer→Check-in' }}</th><th>{{ 'total_cycle_time' | t : 'Total cycle' }}</th></tr></thead>
          <tbody>
            @for (r of rows(); track $index) {
              <tr>
                <td class="mono small">{{ r.visitDate }} · {{ r.visitTime }}</td>
                <td><b style="color:var(--slate-900)">{{ r.labName }}</b><div class="small muted">{{ r.labCode }}</div></td>
                <td>{{ r.collectorName }}</td><td class="mono">{{ r.samples ?? '—' }}</td>
                <td class="mono">{{ dur(r.plannedToCollect) }}</td><td class="mono">{{ dur(r.collectToTransfer) }}</td>
                <td class="mono">{{ dur(r.transferToCheckin) }}</td><td class="mono" style="font-weight:700">{{ dur(r.totalCycle) }}</td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_visits_matching_filters' | t : 'No visits match.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
})
export class RepIntervalsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<Row[]>([]);
  private readonly today = new Date().toISOString().slice(0, 10);
  start = new Date(Date.now() - 7 * 864e5).toISOString().slice(0, 10);
  end = this.today;

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<Row[]>('/reports/rep-intervals', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  avg(field: 'plannedToCollect' | 'collectToTransfer' | 'transferToCheckin' | 'totalCycle'): number | null {
    const vals = this.rows().map((r) => r[field]).filter((v): v is number => v != null);
    return vals.length ? vals.reduce((a, v) => a + v, 0) / vals.length : null;
  }
  dur(mins: number | null): string {
    if (mins == null) return '—';
    const sign = mins < 0 ? '-' : ''; const m = Math.abs(Math.round(mins));
    const h = Math.floor(m / 60); const r = m % 60;
    return sign + (h ? `${h}h ${r}m` : `${r}m`);
  }
}
