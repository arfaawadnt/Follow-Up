import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { LabListItem, PagedResult } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface ChartPoint { m: string; v: number; }
interface Overview {
  samplesMtd: number; completionPct: number; completionDetail: string; avgPerLab: number; activeLabs: number;
  resolutionPct: number; resolutionDetail: string; newLabsYtd: number;
  trend: ChartPoint[]; cats: { c: string; n: number }[]; govRows: { g: string; v: number }[]; segMix: { seg: string; c: number }[];
}
interface Perf { repId: string; name: string; type: string; goalType: string; target: number; achieved: number; pct: number; paceLabel: string; onTrack: boolean; salary: number; }
interface LabHistory { labDisplayCode: string; name: string; segment: string; status: string; avgMonth: number; mtd: number; complaints: number; months: ChartPoint[]; }

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'stats_reports' | t : 'Reports' }}</div><h1>{{ 'stats_reports' | t : 'Reports' }}</h1></div></div>

    <div class="toolbar" style="margin-bottom:14px;display:flex;gap:6px">
      <span class="pill" [class.on]="tab() === 'overview'" (click)="tab.set('overview')">{{ 'network_overview' | t : 'Network overview' }}</span>
      <span class="pill" [class.on]="tab() === 'performance'" (click)="setPerf()">{{ 'rep_performance' | t : 'Rep performance' }}</span>
      <span class="pill" [class.on]="tab() === 'labhistory'" (click)="setHist()">{{ 'lab_histories' | t : 'Lab histories' }}</span>
    </div>

    @if (tab() === 'overview') {
      @if (ov(); as d) {
        <div class="kpis" style="margin-bottom:14px">
          <div class="kpi kpi-teal"><div class="lbl">{{ 'samples_mtd' | t : 'Samples MTD' }}</div><div class="val">{{ d.samplesMtd | number:'1.0-0' }}</div><div class="sub">{{ 'this_calendar_month' | t : 'this month' }}</div></div>
          <div class="kpi kpi-green"><div class="lbl">{{ 'visit_completion' | t : 'Visit completion' }}</div><div class="val">{{ d.completionPct }}%</div><div class="sub">{{ d.completionDetail }}</div></div>
          <div class="kpi kpi-blue"><div class="lbl">{{ 'avg_active_lab' | t : 'Avg / active lab' }}</div><div class="val">{{ d.avgPerLab | number:'1.0-0' }}</div><div class="sub">{{ d.activeLabs }} {{ 'active_labs_2' | t : 'active labs' }}</div></div>
          <div class="kpi kpi-red"><div class="lbl">{{ 'complaint_resolution' | t : 'Complaint resolution' }}</div><div class="val">{{ d.resolutionPct }}%</div><div class="sub">{{ d.resolutionDetail }}</div></div>
          <div class="kpi kpi-amber"><div class="lbl">{{ 'new_labs_ytd' | t : 'New labs YTD' }}</div><div class="val">{{ d.newLabsYtd }}</div><div class="sub">{{ 'joined_this_year' | t : 'joined this year' }}</div></div>
        </div>
        <div class="grid" style="grid-template-columns:1.4fr 1fr;margin-bottom:14px">
          <div class="card"><div class="chead">{{ 'network_samples_6mo' | t }}</div>
            <div class="vchart">@for (p of d.trend; track $index) { <div class="vcol" [class.cur]="$index === d.trend.length - 1"><span class="n">{{ p.v | number:'1.0-0' }}</span><div class="stick" [style.height.px]="h(p.v, d.trend)"></div><span class="m">{{ p.m }}</span></div> }</div>
          </div>
          <div class="card"><div class="chead">{{ 'complaints_by_cat' | t : 'Complaints by category' }}</div>
            @for (c of d.cats; track c.c) { <div class="hrow"><span class="badge b-neu">{{ c.c }}</span><div style="flex:1"><div class="bar"><div [style.width.%]="pct(c.n, maxCat(d.cats))"></div></div></div><span class="mono">{{ c.n }}</span></div> }
          </div>
        </div>
        <div class="grid" style="grid-template-columns:1fr 1fr">
          <div class="card"><div class="chead">{{ 'volume_by_gov' | t }}</div>
            @for (g of d.govRows; track g.g) { <div class="hrow"><span style="width:90px;font-weight:600;color:var(--slate-900)">{{ g.g }}</span><div style="flex:1"><div class="bar"><div [style.width.%]="pct(g.v, maxGov(d.govRows))"></div></div></div><span class="mono">{{ g.v | number:'1.0-0' }}</span></div> }
          </div>
          <div class="card"><div class="chead">{{ 'segment_mix' | t }}</div>
            @for (s of d.segMix; track s.seg) { <div class="hrow"><span class="badge" [class]="s.seg==='A'?'b-ok':s.seg==='B'?'b-info':'b-neu'">{{ ('segment' | t) + ' ' + s.seg }}</span><div style="flex:1"><div class="bar"><div [style.width.%]="pct(s.c, segTotal(d.segMix))"></div></div></div><span class="mono">{{ s.c }}</span></div> }
          </div>
        </div>
      } @else { <div class="empty">{{ 'loading' | t : 'Loading…' }}</div> }
    }

    @if (tab() === 'performance') {
      <div class="card" style="padding:0;overflow:hidden"><div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>{{ 'representative' | t }}</th><th>{{ 'type' | t }}</th><th>{{ 'goal' | t : 'Goal' }}</th><th>{{ 'target' | t }}</th><th>{{ 'achieved' | t }}</th><th style="width:180px">{{ 'attainment' | t }}</th><th>{{ 'pace' | t : 'Pace' }}</th><th>{{ 'salary' | t : 'Salary' }}</th></tr></thead>
        <tbody>
          @for (r of perf(); track r.repId) {
            <tr>
              <td><b style="color:var(--slate-900)">{{ r.name }}</b></td>
              <td><span class="badge" [class]="r.type==='Collector'?'b-info':'b-pur'">{{ r.type }}</span></td>
              <td>{{ r.goalType }}</td><td class="mono small">{{ r.target | number:'1.0-0' }}</td><td class="mono">{{ r.achieved | number:'1.0-0' }}</td>
              <td><div style="display:flex;align-items:center;gap:8px"><div class="bar" style="flex:1"><div [style.width.%]="min100(r.pct)" [style.background]="col(r.pct)"></div></div><span class="mono small">{{ r.pct }}%</span></div></td>
              <td><span class="badge" [class]="r.onTrack?'b-ok':'b-warn'">{{ r.paceLabel }}</span></td>
              <td class="mono">EGP {{ r.salary | number:'1.0-0' }}</td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">—</td></tr> }
        </tbody>
      </table></div></div>
    }

    @if (tab() === 'labhistory') {
      <div class="toolbar" style="margin-bottom:14px">
        <select class="select" [(ngModel)]="histLab" (ngModelChange)="loadHist()" style="min-width:280px">
          @for (l of labs(); track l.id) { <option [value]="l.id">{{ l.name }} — {{ l.displayCode }}</option> }
        </select>
      </div>
      @if (hist(); as h) {
        <div class="grid" style="grid-template-columns:1.2fr 1fr">
          <div class="card"><div class="chead">{{ h.name }} <span class="badge" [class]="h.segment==='A'?'b-ok':h.segment==='B'?'b-info':'b-neu'">{{ ('segment' | t) + ' ' + h.segment }}</span> <span class="badge b-neu">{{ h.status }}</span></div>
            <div class="kpis" style="grid-template-columns:1fr 1fr;padding:14px">
              <div class="kpi kpi-teal"><div class="lbl">{{ 'avg_month' | t : 'Avg / month' }}</div><div class="val">{{ h.avgMonth | number:'1.0-0' }}</div></div>
              <div class="kpi kpi-green"><div class="lbl">{{ 'samples_mtd' | t : 'Samples MTD' }}</div><div class="val">{{ h.mtd | number:'1.0-0' }}</div></div>
              <div class="kpi kpi-red"><div class="lbl">{{ 'complaints' | t }}</div><div class="val">{{ h.complaints }}</div></div>
            </div>
          </div>
          <div class="card"><div class="chead">{{ 'samples_6mo' | t : 'Samples (6 mo)' }}</div>
            <div class="vchart">@for (p of h.months; track $index) { <div class="vcol" [class.cur]="$index === h.months.length - 1"><span class="n">{{ p.v | number:'1.0-0' }}</span><div class="stick" [style.height.px]="h2(p.v, h.months)"></div><span class="m">{{ p.m }}</span></div> }</div>
          </div>
        </div>
      } @else { <div class="empty">{{ 'loading' | t : 'Loading…' }}</div> }
    }
  `,
})
export class ReportsComponent {
  private readonly api = inject(ApiService);
  readonly tab = signal<'overview' | 'performance' | 'labhistory'>('overview');
  readonly ov = signal<Overview | null>(null);
  readonly perf = signal<Perf[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly hist = signal<LabHistory | null>(null);
  histLab = '';

  constructor() {
    this.api.get<Overview>('/reports/overview').subscribe({ next: (d) => this.ov.set(d) });
  }

  setPerf(): void { this.tab.set('performance'); if (this.perf().length === 0) this.api.get<Perf[]>('/reports/performance').subscribe({ next: (r) => this.perf.set(r) }); }
  setHist(): void {
    this.tab.set('labhistory');
    if (this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => { this.labs.set(r.items); if (r.items.length) { this.histLab = r.items[0].id; this.loadHist(); } } });
    }
  }
  loadHist(): void { if (this.histLab) this.api.get<LabHistory>(`/reports/labhistory/${this.histLab}`).subscribe({ next: (h) => this.hist.set(h) }); }

  pct(v: number, total: number): number { return Math.round((v / Math.max(1, total)) * 100); }
  min100(v: number): number { return Math.min(100, v); }
  maxCat(c: { n: number }[]): number { return Math.max(1, ...c.map((x) => x.n)); }
  maxGov(g: { v: number }[]): number { return Math.max(1, ...g.map((x) => x.v)); }
  segTotal(s: { c: number }[]): number { return s.reduce((a, x) => a + x.c, 0); }
  col(p: number): string { return p >= 70 ? 'var(--teal-500)' : p >= 40 ? '#D9A62E' : '#C4574A'; }
  h(v: number, t: ChartPoint[]): number { const mx = Math.max(1, ...t.map((x) => x.v)); return Math.max(4, Math.round((v / mx) * 96)); }
  h2(v: number, t: ChartPoint[]): number { return this.h(v, t); }
}
