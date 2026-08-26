import { exportCsv, localToday, printTable } from '../../shared/export.util';
import { Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { LabListItem, PagedResult, RepPerformanceRow } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface ChartPoint { m: string; v: number; }
interface Overview {
  samplesMtd: number; completionPct: number; completionDetail: string; avgPerLab: number; activeLabs: number;
  resolutionPct: number; resolutionDetail: string; newLabsYtd: number;
  trend: ChartPoint[]; cats: { c: string; n: number }[]; govRows: { g: string; v: number }[]; segMix: { seg: string; c: number }[];
}
interface HistVisit { date: string; time: string; collector: string | null; status: string; samples: number | null; }
interface HistComplaint { reference: string; description: string; date: string; status: string; }
interface LabHistory {
  labDisplayCode: string; encAlias: string; name: string; segment: string; status: string;
  branch: string | null; payer: string | null; contractType: string | null; licenseNo: string | null; licenseDate: string | null;
  preferredChannel: string | null; visitTimes: string[]; workDays: string[]; collectors: string[];
  marketing: string | null; joined: string; address: string | null; contacts: string[];
  avgMonth: number; mtd: number; completion14Pct: number; missed14: number; complaints: number;
  months: ChartPoint[]; visits: HistVisit[]; complaintRows: HistComplaint[];
}

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [FormsModule, DecimalPipe, DatePipe, RouterLink, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'stats_reports' | t : 'Reports' }}</div><h1>{{ 'stats_reports' | t : 'Reports' }}</h1></div>
      <div class="pagehead-actions">
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="toolbar" style="margin-bottom:14px;display:flex;gap:6px">
      <span class="pill" [class.on]="tab() === 'overview'" (click)="tab.set('overview')">{{ 'network_overview' | t : 'Network overview' }}</span>
      <span class="pill" [class.on]="tab() === 'performance'" (click)="setPerf()">{{ 'rep_attainment' | t : 'Rep Attainment' }}</span>
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
              <td>{{ r.goalType }}</td>
              <td class="mono small">{{ r.target | number:'1.0-0' }} {{ unit(r) }} · {{ r.goalDuration }}</td>
              <td class="mono">{{ r.achieved | number:'1.0-0' }} {{ unit(r) }}</td>
              <td><div style="display:flex;align-items:center;gap:8px"><div class="bar" style="flex:1"><div [style.width.%]="min100(r.pct)" [style.background]="col(r.pct)"></div></div><span class="mono small">{{ r.pct }}%</span></div></td>
              <td><span class="badge" [class]="r.onTrack?'b-ok':'b-warn'">{{ r.paceLabel }}</span></td>
              <td class="mono">EGP {{ r.salary | number:'1.0-0' }}</td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">—</td></tr> }
        </tbody>
      </table></div></div>
    }

    @if (tab() === 'labhistory') {
      <div class="toolbar" style="margin-bottom:14px;display:flex;gap:10px;align-items:center">
        <select class="select" [(ngModel)]="histLab" (ngModelChange)="loadHist()" style="min-width:280px">
          @for (l of labs(); track l.id) { <option [value]="l.id">{{ l.name }} — {{ l.displayCode }}</option> }
        </select>
        @if (histLab) { <a class="btn btn-s" [routerLink]="['/labs', histLab]">{{ 'edit_this_lab' | t : 'Edit this laboratory' }}</a> }
      </div>
      @if (hist(); as x) {
        <div class="grid" style="grid-template-columns:1.2fr 1fr;margin-bottom:14px">
          <div class="card">
            <div class="chead">{{ x.name }} <span class="mono small muted">{{ x.labDisplayCode }} · {{ x.encAlias }}</span>
              <span class="badge" [class]="x.segment==='A'?'b-ok':x.segment==='B'?'b-info':'b-neu'">{{ ('segment' | t) + ' ' + x.segment }}</span>
              <span class="badge b-neu">{{ x.status }}</span></div>
            <dl class="proffields" style="padding:12px 16px">
              <dt>{{ 'branch' | t : 'Branch' }}</dt><dd>{{ x.branch ?? '—' }}</dd>
              <dt>{{ 'payer' | t : 'Payer' }}</dt><dd>{{ x.payer ?? '—' }}</dd>
              <dt>{{ 'contract' | t : 'Contract' }}</dt><dd>{{ x.contractType ?? '—' }}</dd>
              <dt>{{ 'license' | t : 'License' }}</dt><dd>{{ x.licenseNo ?? '—' }}@if (x.licenseDate) { <span class="muted small"> · {{ x.licenseDate | date:'mediumDate' }}</span> }</dd>
              <dt>{{ 'preferred_channel' | t : 'Channel' }}</dt><dd>{{ x.preferredChannel ?? '—' }}</dd>
              <dt>{{ 'visit_times' | t : 'Visit times' }}</dt><dd class="mono">{{ x.visitTimes.length ? x.visitTimes.join(' · ') : '—' }}</dd>
              <dt>{{ 'work_days' | t : 'Work days' }}</dt><dd>{{ x.workDays.length ? x.workDays.join(', ') : '—' }}</dd>
              <dt>{{ 'collectors' | t : 'Collectors' }}</dt><dd>{{ x.collectors.length ? x.collectors.join(', ') : '—' }}</dd>
              <dt>{{ 'marketing_rep' | t : 'Marketing' }}</dt><dd>{{ x.marketing ?? '—' }}</dd>
              <dt>{{ 'joined' | t : 'Joined' }}</dt><dd>{{ x.joined | date:'mediumDate' }}</dd>
              <dt>{{ 'address' | t : 'Address' }}</dt><dd>{{ x.address ?? '—' }}</dd>
              <dt>{{ 'contacts' | t : 'Contacts' }}</dt><dd>{{ x.contacts.length ? x.contacts.join(' / ') : '—' }}</dd>
            </dl>
          </div>
          <div>
            <div class="kpis" style="grid-template-columns:1fr 1fr;margin-bottom:14px">
              <div class="kpi kpi-teal"><div class="lbl">{{ 'avg_month' | t : 'Avg / month' }}</div><div class="val">{{ x.avgMonth | number:'1.0-0' }}</div></div>
              <div class="kpi kpi-green"><div class="lbl">{{ 'samples_mtd' | t : 'Samples MTD' }}</div><div class="val">{{ x.mtd | number:'1.0-0' }}</div></div>
              <div class="kpi kpi-blue"><div class="lbl">{{ 'completion_14d' | t : '14-day completion' }}</div><div class="val">{{ x.completion14Pct }}%</div><div class="sub">{{ x.missed14 }} {{ 'missed' | t : 'missed' }}</div></div>
              <div class="kpi kpi-red"><div class="lbl">{{ 'complaints' | t }}</div><div class="val">{{ x.complaints }}</div></div>
            </div>
            <div class="card"><div class="chead">{{ 'samples_6mo' | t : 'Samples (6 mo)' }}</div>
              <div class="vchart">@for (p of x.months; track $index) { <div class="vcol" [class.cur]="$index === x.months.length - 1"><span class="n">{{ p.v | number:'1.0-0' }}</span><div class="stick" [style.height.px]="h(p.v, x.months)"></div><span class="m">{{ p.m }}</span></div> }</div>
            </div>
          </div>
        </div>
        <div class="grid" style="grid-template-columns:1.2fr 1fr">
          <div class="card" style="padding:0;overflow:hidden"><div class="chead" style="padding:12px 16px">{{ 'visit_history' | t : 'Visit history' }}</div>
            <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
              <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'time' | t : 'Time' }}</th><th>{{ 'collector' | t }}</th><th>{{ 'status' | t }}</th><th class="r">{{ 'samples_2' | t : 'Samples' }}</th></tr></thead>
              <tbody>
                @for (v of x.visits; track $index) {
                  <tr><td class="mono small">{{ v.date }}</td><td class="mono small">{{ v.time }}</td><td>{{ v.collector ?? '—' }}</td>
                    <td><span class="badge" [class]="vbadge(v.status)">{{ v.status }}</span></td><td class="r mono">{{ v.samples ?? '—' }}</td></tr>
                } @empty { <tr><td colspan="5" class="empty" style="text-align:center;padding:18px">—</td></tr> }
              </tbody>
            </table></div>
          </div>
          <div class="card" style="padding:0;overflow:hidden"><div class="chead" style="padding:12px 16px">{{ 'complaints_from_lab' | t : 'Complaints from this lab' }}</div>
            <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
              <thead><tr><th>{{ 'ref' | t : 'Ref' }}</th><th>{{ 'description_lbl' | t : 'Description' }}</th><th>{{ 'date' | t }}</th><th>{{ 'status' | t }}</th></tr></thead>
              <tbody>
                @for (c of x.complaintRows; track c.reference) {
                  <tr><td class="mono">{{ c.reference }}</td><td class="small">{{ c.description }}</td><td class="mono small">{{ c.date }}</td>
                    <td><span class="badge" [class]="c.status === 'Resolved' ? 'b-ok' : c.status === 'InProgress' ? 'b-warn' : 'b-bad'">{{ c.status }}</span></td></tr>
                } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:18px">{{ 'no_complaints' | t : 'No complaints.' }}</td></tr> }
              </tbody>
            </table></div>
          </div>
        </div>
      } @else { <div class="empty">{{ 'loading' | t : 'Loading…' }}</div> }
    }
  `,
  styles: [`
    th.r,td.r{text-align:right}
    .proffields{display:grid;grid-template-columns:130px 1fr;gap:6px 10px;margin:0}
    .proffields dt{font-size:12px;color:var(--slate-500);font-weight:600}
    .proffields dd{margin:0;font-size:13px;color:var(--slate-900)}
  `],
})
export class ReportsComponent {
  private readonly api = inject(ApiService);
  readonly tab = signal<'overview' | 'performance' | 'labhistory'>('overview');
  readonly ov = signal<Overview | null>(null);
  readonly perf = signal<RepPerformanceRow[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly hist = signal<LabHistory | null>(null);
  histLab = '';
  private readonly today = localToday();

  constructor() {
    this.api.get<Overview>('/reports/overview').subscribe({ next: (d) => this.ov.set(d) });
  }

  setPerf(): void { this.tab.set('performance'); if (this.perf().length === 0) this.api.get<RepPerformanceRow[]>('/reports/performance').subscribe({ next: (r) => this.perf.set(r) }); }
  setHist(): void {
    this.tab.set('labhistory');
    if (this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => { this.labs.set(r.items); if (r.items.length) { this.histLab = r.items[0].id; this.loadHist(); } } });
    }
  }
  loadHist(): void { if (this.histLab) { this.hist.set(null); this.api.get<LabHistory>(`/reports/labhistory/${this.histLab}`).subscribe({ next: (h) => this.hist.set(h) }); } }

  unit(r: RepPerformanceRow): string { return (r.metric ?? (r.type === 'Collector' ? 'samples' : '')).toLowerCase(); }
  pct(v: number, total: number): number { return Math.round((v / Math.max(1, total)) * 100); }
  min100(v: number): number { return Math.min(100, v); }
  maxCat(c: { n: number }[]): number { return Math.max(1, ...c.map((x) => x.n)); }
  maxGov(g: { v: number }[]): number { return Math.max(1, ...g.map((x) => x.v)); }
  segTotal(s: { c: number }[]): number { return s.reduce((a, x) => a + x.c, 0); }
  col(p: number): string { return p >= 70 ? 'var(--teal-500)' : p >= 40 ? '#D9A62E' : '#C4574A'; }
  h(v: number, t: ChartPoint[]): number { const mx = Math.max(1, ...t.map((x) => x.v)); return Math.max(4, Math.round((v / mx) * 96)); }
  vbadge(s: string): string { return s === 'Visited' || s === 'Received' ? 'b-ok' : s === 'Missed' ? 'b-bad' : s === 'Pending' ? 'b-info' : 'b-neu'; }

  private exportData(): { title: string; header: string[]; rows: (string | number)[][] } {
    const t = this.tab();
    if (t === 'performance') {
      return {
        title: 'Rep attainment', header: ['Representative', 'Type', 'Goal', 'Target', 'Achieved', 'Attainment %', 'Pace', 'Salary'],
        rows: this.perf().map((r) => [r.name, r.type, r.goalType, `${r.target} ${this.unit(r)} · ${r.goalDuration}`, `${r.achieved} ${this.unit(r)}`, r.pct, r.paceLabel, r.salary]),
      };
    }
    if (t === 'labhistory') {
      const x = this.hist();
      return {
        title: `Lab history — ${x?.name ?? ''}`, header: ['Date', 'Time', 'Collector', 'Status', 'Samples'],
        rows: (x?.visits ?? []).map((v) => [v.date, v.time, v.collector ?? '—', v.status, v.samples ?? '—']),
      };
    }
    const d = this.ov();
    return {
      title: 'Network overview', header: ['Metric', 'Value'],
      rows: d ? [
        ['Samples MTD', d.samplesMtd], ['Visit completion %', d.completionPct], ['Completion detail', d.completionDetail],
        ['Avg / active lab', d.avgPerLab], ['Active labs', d.activeLabs],
        ['Complaint resolution %', d.resolutionPct], ['Resolution detail', d.resolutionDetail], ['New labs YTD', d.newLabsYtd],
        ...d.govRows.map((g) => [`Volume — ${g.g}`, g.v] as (string | number)[]),
      ] : [],
    };
  }
  exportExcel(): void { const { title, header, rows } = this.exportData(); exportCsv(`${title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}-${this.today}.csv`, header, rows); }
  exportPdf(): void { const { title, header, rows } = this.exportData(); printTable(title, header, rows); }
}
