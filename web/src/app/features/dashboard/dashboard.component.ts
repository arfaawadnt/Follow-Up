import { Component, inject, signal } from '@angular/core';
import { DecimalPipe, SlicePipe } from '@angular/common';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { TranslatePipe } from '../../core/i18n';

interface Kpis {
  activeLabs: number; totalLabs: number; done: number; totalVisits: number; pending: number; missed: number;
  samplesToday: number; openComplaints: number; inProgress: number; resolved: number;
  mtd: number; target: number; monthName: string;
}
interface DashSchedule { id: string; time: string; lab: string; area: string | null; rep: string; status: string; samples: number | null; transferDone: boolean; }
interface DashComplaint { id: string; lab: string; description: string; category: string; age: number; }
interface DashRepProg { name: string; detail: string; pct: number; }
interface DashTopLab { name: string; area: string | null; v: number; }
interface DashSegMix { seg: string; c: number; }
interface DashGovRow { g: string; v: number; }
interface DashboardData {
  kpis: Kpis; bday: { text: string } | null;
  schedule: DashSchedule[]; complaints: DashComplaint[]; repProg: DashRepProg[];
  topLabs: DashTopLab[]; trend: number[]; segMix: DashSegMix[]; govRows: DashGovRow[];
}

const MO = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [DecimalPipe, SlicePipe, TranslatePipe],
  template: `
    @if (d(); as d) {
      <div class="pagehead">
        <div>
          <div class="breadcrumbs">Home / {{ 'dashboard' | t }}</div>
          <h1>{{ 'dashboard' | t }}</h1>
        </div>
        <div class="pagehead-actions">
          @if (auth.has('AddLabs')) { <button class="btn btn-s" (click)="go('/labs/new')">{{ 'new_lab_btn' | t }}</button> }
          @if (auth.has('AddComplaints')) { <button class="btn btn-s" (click)="go('/complaints')">{{ 'log_complaint_btn' | t }}</button> }
        </div>
      </div>

      @if (d.bday) { <div class="inline-banner inline-banner-info">🎂 {{ d.bday.text }}</div> }

      <div class="kpis" style="margin-bottom:14px">
        <div class="kpi kpi-teal"><div class="lbl">{{ 'active_labs' | t }}</div><div class="val">{{ d.kpis.activeLabs }}</div><div class="sub">{{ 'of' | t }} {{ d.kpis.totalLabs }} {{ 'registered' | t }}</div></div>
        <div class="kpi kpi-blue"><div class="lbl">{{ 'todays_visits' | t }}</div><div class="val">{{ d.kpis.done }}<span style="font-size:13px;opacity:.6"> / {{ d.kpis.totalVisits }}</span></div><div class="sub">{{ d.kpis.pending }} {{ 'pending' | t }} · {{ d.kpis.missed }} {{ 'missed' | t }}</div></div>
        <div class="kpi kpi-green"><div class="lbl">{{ 'samples_today' | t }}</div><div class="val">{{ d.kpis.samplesToday | number:'1.0-0' }}</div><div class="sub">{{ 'from_completed' | t }}</div></div>
        <div class="kpi kpi-red"><div class="lbl">{{ 'open_complaints' | t }}</div><div class="val">{{ d.kpis.openComplaints }}</div><div class="sub">{{ d.kpis.inProgress }} {{ 'pending' | t }} · {{ d.kpis.resolved }} {{ 'resolved' | t }}</div></div>
        <div class="kpi kpi-amber"><div class="lbl">{{ d.kpis.monthName }} {{ 'samples' | t }}</div><div class="val">{{ d.kpis.mtd | number:'1.0-0' }}</div><div class="sub">{{ pctTarget(d.kpis) }}% {{ 'of' | t }} {{ d.kpis.target | number:'1.0-0' }} {{ 'target' | t }}</div><div class="bar"><div [style.width.%]="pctTarget(d.kpis)" style="background:#2E3440"></div></div></div>
      </div>

      <div class="grid" style="grid-template-columns:1.5fr 1fr;margin-bottom:14px">
        <div class="card"><div class="chead">{{ 'todays_schedule' | t }} <span class="small muted" style="font-weight:400">{{ 'first_9_by_time' | t }}</span></div>
          @if (d.schedule.length) {
            <table><tr><th>{{ 'time' | t }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'collector' | t }}</th><th>{{ 'status' | t }}</th></tr>
              @for (v of d.schedule; track v.id) {
                <tr><td class="mono">{{ v.time }}</td>
                  <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ v.area ?? '—' }}</div></td>
                  <td>{{ v.rep }}</td>
                  <td><span class="badge" [class]="badgeClass(v.status)">{{ v.status | t }}</span>@if (v.samples != null) { <span class="mono small"> {{ v.samples }}</span> }</td></tr>
              }
            </table>
          } @else { <div class="empty">{{ 'no_visits_today' | t }}</div> }
        </div>
        <div class="card"><div class="chead">{{ 'unresolved_complaints' | t }}</div>
          @if (d.complaints.length) {
            @for (c of d.complaints; track c.id) {
              <div class="hrow"><span class="mono" style="color:var(--teal-700)">{{ c.id }}</span>
                <div style="flex:1"><b style="color:var(--slate-900)">{{ c.lab }}</b><div class="small muted">{{ c.description | slice:0:70 }}…</div></div>
                <span class="badge b-neu">{{ c.category }}</span><span class="small muted">{{ c.age === 0 ? ('today' | t) : c.age + ('d' | t) }}</span></div>
            }
          } @else { <div class="empty">{{ 'no_unresolved_complaints' | t }}</div> }
        </div>
      </div>

      <div class="grid" style="grid-template-columns:1fr 1fr;margin-bottom:14px">
        <div class="card"><div class="chead">{{ 'collector_progress' | t }} <span class="small muted" style="font-weight:400">{{ 'attainment' | t }}</span></div>
          @for (r of d.repProg; track r.name) {
            <div class="prog-row"><span class="nm">{{ r.name }}</span><span class="dt">{{ r.detail }}</span>
              <div class="pb"><div class="bar"><div [style.width.%]="min100(r.pct)" [style.background]="progColor(r.pct)"></div></div></div>
              <span class="pc">{{ r.pct }}%</span></div>
          } @empty { <div class="empty">—</div> }
        </div>
        <div class="card"><div class="chead">{{ 'top_labs' | t }} — {{ d.kpis.monthName }}</div>
          @for (l of d.topLabs; track $index) {
            <div class="hrow"><span class="mono muted">{{ $index + 1 }}</span>
              <div style="flex:1"><b style="color:var(--slate-900)">{{ l.name }}</b><div class="small muted">{{ l.area ?? '—' }}</div></div>
              <span class="mono">{{ l.v | number:'1.0-0' }}</span></div>
          } @empty { <div class="empty">—</div> }
        </div>
      </div>

      <div class="grid" style="grid-template-columns:1.4fr .8fr 1fr">
        <div class="card"><div class="chead">{{ 'network_samples_6mo' | t }}</div>
          <div class="vchart">
            @for (val of d.trend; track $index) {
              <div class="vcol" [class.cur]="$index === d.trend.length - 1">
                <span class="n">{{ val | number:'1.0-0' }}</span>
                <div class="stick" [style.height.px]="stickHeight(val, d.trend)"></div>
                <span class="m">{{ monthLabel($index, d.trend.length) }}</span>
              </div>
            }
          </div>
          <div class="small muted" style="padding:0 14px 12px">{{ 'current_month_to_date' | t }}</div>
        </div>
        <div class="card"><div class="chead">{{ 'segment_mix' | t }}</div>
          @for (s of d.segMix; track s.seg) {
            <div class="hrow"><span class="badge" [class]="segClass(s.seg)">{{ ('segment' | t) + ' ' + s.seg }}</span>
              <div style="flex:1"><div class="bar"><div [style.width.%]="pct(s.c, d.kpis.totalLabs)"></div></div></div><span class="mono">{{ s.c }}</span></div>
          }
        </div>
        <div class="card"><div class="chead">{{ 'volume_by_gov' | t }}</div>
          @for (g of d.govRows; track g.g) {
            <div class="hrow"><span style="width:90px;font-weight:600;color:var(--slate-900)">{{ g.g }}</span>
              <div style="flex:1"><div class="bar"><div [style.width.%]="pct(g.v, maxGov(d.govRows))"></div></div></div><span class="mono">{{ g.v | number:'1.0-0' }}</span></div>
          }
        </div>
      </div>
    } @else if (loading()) { <div class="empty">{{ 'loading' | t : 'Loading…' }}</div> }
  `,
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly icons = inject(IconsService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly d = signal<DashboardData | null>(null);

  constructor() {
    this.api.get<DashboardData>('/dashboard').subscribe({
      next: (data) => { this.d.set(data); this.loading.set(false); this.icons.render(); },
      error: () => this.loading.set(false),
    });
  }

  go(path: string): void { void this.router.navigateByUrl(path); }
  pctTarget(k: Kpis): number { return k.target ? Math.min(100, Math.round((k.mtd / k.target) * 100)) : 0; }
  pct(v: number, total: number): number { return Math.round((v / Math.max(1, total)) * 100); }
  min100(v: number): number { return Math.min(100, v); }
  maxGov(rows: DashGovRow[]): number { return Math.max(1, ...rows.map((r) => r.v)); }
  progColor(p: number): string { return p >= 70 ? 'var(--teal-500)' : p >= 40 ? '#D9A62E' : '#C4574A'; }
  stickHeight(v: number, trend: number[]): number { const mx = Math.max(1, ...trend); return Math.max(4, Math.round((v / mx) * 96)); }
  monthLabel(i: number, len: number): string { const now = new Date(); const back = len - 1 - i; return MO[(now.getMonth() - back + 12) % 12]; }

  badgeClass(status: string): string {
    const s = status.toLowerCase();
    if (s === 'visited' || s === 'received' || s === 'transferred' || s === 'collected') return 'b-ok';
    if (s === 'pending') return 'b-warn';
    if (s === 'missed') return 'b-bad';
    return 'b-neu';
  }
  segClass(seg: string): string { return seg === 'A' ? 'b-ok' : seg === 'B' ? 'b-info' : 'b-neu'; }
}
