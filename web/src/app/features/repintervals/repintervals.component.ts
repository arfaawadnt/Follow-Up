import { exportCsv, localToday, printTable, ddmy } from '../../shared/export.util';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface Row {
  collectorName: string; labName: string; labCode: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; visitTime: string; samples: number | null;
  plannedToCollect: number | null; collectToTransfer: number | null; transferToCheckin: number | null; totalCycle: number | null;
  checkinTime: string | null; transferTime: string | null; markedAt: string | null;
}
interface GroupRow {
  key: string; visits: number; samples: number;
  plannedToCollect: number | null; collectToTransfer: number | null; transferToCheckin: number | null; totalCycle: number | null;
}
type GroupBy = 'rep' | 'lab' | 'area' | 'none';
type AvgField = 'plannedToCollect' | 'collectToTransfer' | 'transferToCheckin' | 'totalCycle';

@Component({
  selector: 'app-repintervals',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, AppDatePipe, DateInputComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'rep_intervals' | t }}</div><h1>{{ 'rep_intervals' | t }}</h1></div>
      <div class="pagehead-actions">
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'planned_collect_delay' | t : 'Planned → Collect' }}</div><div class="val" style="font-size:22px">{{ dur(avg(filtered(), 'plannedToCollect')) }}</div><div class="sub">{{ 'avg_delay' | t : 'avg delay' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'collect_to_transfer' | t : 'Collect → Transfer' }}</div><div class="val" style="font-size:22px">{{ dur(avg(filtered(), 'collectToTransfer')) }}</div><div class="sub">{{ 'avg' | t : 'avg' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'transfer_to_checkin' | t : 'Transfer → Check-in' }}</div><div class="val" style="font-size:22px">{{ dur(avg(filtered(), 'transferToCheckin')) }}</div><div class="sub">{{ 'avg' | t : 'avg' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_cycle_time' | t : 'Total cycle' }}</div><div class="val" style="font-size:22px">{{ dur(avg(filtered(), 'totalCycle')) }}</div><div class="sub">{{ 'avg' | t : 'avg' }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="start"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="end"></app-date-input></div>
        <div class="field" style="display:flex;gap:8px"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button><button class="btn btn-s" (click)="reset()" style="height:36px">{{ 'reset' | t : 'Reset' }}</button></div>
        <div class="field"><label>{{ 'collector' | t : 'Collector' }}</label><select class="select" [ngModel]="fCollector()" (ngModelChange)="fCollector.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (v of collectors(); track v) { <option [value]="v">{{ v }}</option> }</select></div>
        <div class="field"><label>{{ 'laboratory' | t : 'Laboratory' }}</label><select class="select" [ngModel]="fLab()" (ngModelChange)="fLab.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (v of labNames(); track v) { <option [value]="v">{{ v }}</option> }</select></div>
        <div class="field"><label>{{ 'branch' | t : 'Branch' }}</label><select class="select" [ngModel]="fBranch()" (ngModelChange)="fBranch.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (v of branches(); track v) { <option [value]="v">{{ v }}</option> }</select></div>
        <div class="field"><label>{{ 'governorate_2' | t : 'Governorate' }}</label><select class="select" [ngModel]="fGov()" (ngModelChange)="fGov.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (v of govs(); track v) { <option [value]="v">{{ v }}</option> }</select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><select class="select" [ngModel]="fCity()" (ngModelChange)="fCity.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (v of cities(); track v) { <option [value]="v">{{ v }}</option> }</select></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><select class="select" [ngModel]="fArea()" (ngModelChange)="fArea.set($event)"><option value="">{{ 'all' | t : 'All' }}</option>@for (v of areas(); track v) { <option [value]="v">{{ v }}</option> }</select></div>
        <div class="field"><label>{{ 'group_by' | t : 'Group by' }}</label>
          <select class="select" [ngModel]="groupBy()" (ngModelChange)="groupBy.set($event)">
            <option value="rep">{{ 'representative' | t : 'Representative' }}</option>
            <option value="lab">{{ 'laboratory' | t : 'Laboratory' }}</option>
            <option value="area">{{ 'area_2' | t : 'Area' }}</option>
            <option value="none">{{ 'none_detailed_list' | t : 'None (Detailed List)' }}</option>
          </select></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else if (groupBy() !== 'none') {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ groupLabel() }}</th><th class="r">{{ 'visits_count' | t : 'Visits count' }}</th><th class="r">{{ 'total_samples' | t : 'Total samples' }}</th>
            <th class="r">{{ 'planned_collect_delay' | t : 'Planned→Collect' }}</th><th class="r">{{ 'collect_to_transfer' | t : 'Collect→Transfer' }}</th>
            <th class="r">{{ 'transfer_to_checkin' | t : 'Transfer→Check-in' }}</th><th class="r">{{ 'total_cycle_time' | t : 'Total cycle' }}</th></tr></thead>
          <tbody>
            @for (g of grouped(); track g.key) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ g.key }}</b></td>
                <td class="r mono">{{ g.visits }}</td><td class="r mono">{{ g.samples | number:'1.0-0' }}</td>
                <td class="r mono">{{ dur(g.plannedToCollect) }}</td><td class="r mono">{{ dur(g.collectToTransfer) }}</td>
                <td class="r mono">{{ dur(g.transferToCheckin) }}</td><td class="r mono" style="font-weight:700">{{ dur(g.totalCycle) }}</td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:24px">{{ 'no_visits_matching_filters' | t : 'No visits match.' }}</td></tr> }
          </tbody>
        </table></div>
      }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'collector' | t }}</th><th>{{ 'samples_2' | t : 'Samples' }}</th>
            <th>{{ 'planned_collect_delay' | t : 'Planned→Collect' }}</th><th>{{ 'collect_to_transfer' | t : 'Collect→Transfer' }}</th>
            <th>{{ 'transfer_to_checkin' | t : 'Transfer→Check-in' }}</th><th>{{ 'total_cycle_time' | t : 'Total cycle' }}</th></tr></thead>
          <tbody>
            @for (r of filtered(); track $index) {
              <tr>
                <td class="mono small">{{ r.visitDate | appDate }} · {{ r.visitTime }}</td>
                <td><b style="color:var(--slate-900)">{{ r.labName }}</b><div class="small muted">{{ r.labCode }}@if (r.area) { · {{ r.area }} }</div></td>
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
  styles: [`th.r,td.r{text-align:right}`],
})
export class RepIntervalsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<Row[]>([]);
  readonly groupBy = signal<GroupBy>('rep');
  readonly fCollector = signal('');
  readonly fLab = signal('');
  readonly fBranch = signal('');
  readonly fGov = signal('');
  readonly fCity = signal('');
  readonly fArea = signal('');
  private readonly today = localToday();
  start = new Date(Date.now() - 7 * 864e5).toISOString().slice(0, 10);
  end = this.today;

  readonly collectors = computed(() => [...new Set(this.rows().map((r) => r.collectorName))].sort());
  readonly labNames = computed(() => [...new Set(this.rows().map((r) => r.labName))].sort());
  readonly branches = computed(() => [...new Set(this.rows().map((r) => r.branch).filter((v): v is string => !!v))].sort());
  readonly govs = computed(() => [...new Set(this.rows().map((r) => r.governorate).filter((v): v is string => !!v))].sort());
  readonly cities = computed(() => [...new Set(this.rows().map((r) => r.city).filter((v): v is string => !!v))].sort());
  readonly areas = computed(() => [...new Set(this.rows().map((r) => r.area).filter((v): v is string => !!v))].sort());

  readonly filtered = computed(() => this.rows().filter((r) =>
    (!this.fCollector() || r.collectorName === this.fCollector()) &&
    (!this.fLab() || r.labName === this.fLab()) &&
    (!this.fBranch() || r.branch === this.fBranch()) &&
    (!this.fGov() || r.governorate === this.fGov()) &&
    (!this.fCity() || r.city === this.fCity()) &&
    (!this.fArea() || r.area === this.fArea())));

  readonly grouped = computed<GroupRow[]>(() => {
    const by = this.groupBy();
    if (by === 'none') return [];
    const keyOf = (r: Row) => by === 'rep' ? r.collectorName : by === 'lab' ? r.labName : (r.area ?? '—');
    const map = new Map<string, Row[]>();
    for (const r of this.filtered()) {
      const k = keyOf(r);
      const list = map.get(k) ?? [];
      list.push(r); map.set(k, list);
    }
    return [...map.entries()].map(([key, list]) => ({
      key, visits: list.length, samples: list.reduce((a, r) => a + (r.samples ?? 0), 0),
      plannedToCollect: this.avg(list, 'plannedToCollect'), collectToTransfer: this.avg(list, 'collectToTransfer'),
      transferToCheckin: this.avg(list, 'transferToCheckin'), totalCycle: this.avg(list, 'totalCycle'),
    })).sort((a, b) => b.visits - a.visits);
  });

  groupLabel(): string { const by = this.groupBy(); return by === 'rep' ? 'Representative' : by === 'lab' ? 'Laboratory' : 'Area'; }

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<Row[]>('/reports/rep-intervals', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  reset(): void {
    this.start = new Date(Date.now() - 7 * 864e5).toISOString().slice(0, 10);
    this.end = this.today;
    this.groupBy.set('rep');
    this.fCollector.set(''); this.fLab.set(''); this.fBranch.set(''); this.fGov.set(''); this.fCity.set(''); this.fArea.set('');
    this.load();
  }

  avg(rows: Row[], field: AvgField): number | null {
    const vals = rows.map((r) => r[field]).filter((v): v is number => v != null);
    return vals.length ? vals.reduce((a, v) => a + v, 0) / vals.length : null;
  }
  dur(mins: number | null): string {
    if (mins == null) return '—';
    const sign = mins < 0 ? '-' : ''; const m = Math.abs(Math.round(mins));
    const h = Math.floor(m / 60); const r = m % 60;
    return sign + (h ? `${h}h ${r}m` : `${r}m`);
  }

  private exportData(): { header: string[]; rows: (string | number)[][] } {
    if (this.groupBy() !== 'none') {
      return {
        header: [this.groupLabel(), 'Visits count', 'Total samples', 'Planned→Collect', 'Collect→Transfer', 'Transfer→Check-in', 'Total cycle'],
        rows: this.grouped().map((g) => [g.key, g.visits, g.samples, this.dur(g.plannedToCollect), this.dur(g.collectToTransfer), this.dur(g.transferToCheckin), this.dur(g.totalCycle)]),
      };
    }
    return {
      header: ['Date', 'Time', 'Laboratory', 'Code', 'Collector', 'Samples', 'Planned→Collect', 'Collect→Transfer', 'Transfer→Check-in', 'Total cycle'],
      rows: this.filtered().map((r) => [ddmy(r.visitDate), r.visitTime, r.labName, r.labCode, r.collectorName, r.samples ?? '—', this.dur(r.plannedToCollect), this.dur(r.collectToTransfer), this.dur(r.transferToCheckin), this.dur(r.totalCycle)]),
    };
  }
  exportExcel(): void { const { header, rows } = this.exportData(); exportCsv(`rep-performance-${this.today}.csv`, header, rows); }
  exportPdf(): void { const { header, rows } = this.exportData(); printTable('Rep performance', header, rows); }
}
