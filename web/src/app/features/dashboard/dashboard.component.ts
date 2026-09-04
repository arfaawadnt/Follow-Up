import { Component, inject, signal } from '@angular/core';
import { DecimalPipe, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface Kpis {
  activeLabs: number; totalLabs: number; done: number; totalVisits: number; pending: number; missed: number;
  samplesToday: number; openComplaints: number; inProgress: number; resolved: number;
  mtd: number; target: number; monthName: string;
}
interface DashSchedule { id: string; time: string; lab: string; area: string | null; rep: string; status: string; samples: number | null; transferDone: boolean; }
interface DashComplaint { id: string; lab: string; description: string; category: string; age: number; }
interface DashRepProg { name: string; detail: string; pct: number; }
interface DashTopLab { name: string; area: string | null; gov: string | null; v: number; }
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
  imports: [DecimalPipe, SlicePipe, FormsModule, TranslatePipe],
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

      @if (d.bday) { <div class="inline-banner">🎂 {{ 'birthday_reminder' | t : 'Birthday reminder' }} — {{ d.bday.text }}</div> }

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
            <table><tr><th>{{ 'time' | t }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'collector' | t }}</th><th>{{ 'status' | t }}</th><th></th></tr>
              @for (v of d.schedule; track v.id) {
                <tr><td class="mono">{{ v.time }}</td>
                  <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ v.area ?? '—' }}</div></td>
                  <td>{{ v.rep }}</td>
                  <td><span class="badge" [class]="badgeClass(v.status)">{{ v.status | t }}</span>@if (v.samples != null) { <span class="mono small"> {{ v.samples }}</span> }</td>
                  <td>@if (v.status === 'Pending' && auth.has('AddDailyFollowup')) {
                    <button class="btn btn-mini btn-p" (click)="openRecord(v)" [disabled]="busy()">{{ 'record_visit' | t : 'Record visit' }}</button>
                  }</td></tr>
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
              <div style="flex:1"><b style="color:var(--slate-900)">{{ l.name }}</b><div class="small muted">{{ l.area ?? '—' }} · {{ l.gov ?? '—' }}</div></div>
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

    <!-- Record-visit popup, same flow as the daily board (the reference records straight from the dashboard). -->
    @if (recording(); as v) {
      <div class="overlay" (click)="closeRecord()">
        <div class="dlg" (click)="$event.stopPropagation()">
          <h3 style="margin:0 0 4px">{{ 'record_visit' | t : 'Record visit' }}</h3>
          <div class="small muted" style="margin-bottom:12px">{{ v.lab }} · {{ 'scheduled_2' | t : 'Scheduled' }} {{ v.time }} · {{ v.area ?? '—' }}</div>
          <div class="field">
            <label>{{ 'collector_rep' | t : 'Collector Rep' }}</label>
            <select class="select" [(ngModel)]="recordRep" style="width:100%">
              <option value="">—</option>
              @for (r of collectorReps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }
            </select>
          </div>
          <div class="field" style="margin-top:10px">
            <label>{{ 'samples' | t : 'Samples collected' }} *</label>
            <input type="number" min="0" class="input" [(ngModel)]="recordCount" style="width:100%">
            @if (suggested() !== null) { <div class="small muted" style="margin-top:4px">Suggested: {{ suggested() }} (last recorded count for this lab)</div> }
          </div>
          <div class="grid2" style="margin-top:10px">
            <div class="field"><label>{{ 'total_required' | t : 'Total Required' }}</label><input type="number" min="0" class="input" [(ngModel)]="recordTotalRequired"></div>
            <div class="field"><label>{{ 'no_of_requests' | t : 'No of Requests' }}</label><input type="number" min="0" class="input" [(ngModel)]="recordRequests"></div>
          </div>
          <div class="field" style="margin-top:10px">
            <label>{{ 'no_of_outsource_samples' | t : 'No of Outsource Samples' }}</label>
            <input type="number" min="0" class="input" [(ngModel)]="recordOutsource" style="width:100%">
          </div>
          <div class="field" style="margin-top:10px">
            <label>{{ 'notes_optional' | t : 'Notes (optional)' }}</label>
            <textarea class="input" rows="2" [(ngModel)]="recordNotes" style="width:100%"></textarea>
          </div>
          <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:16px">
            <button class="btn btn-s" (click)="closeRecord()">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="recordCount === null || recordCount < 0 || busy()" (click)="confirmRecord()">{{ 'confirm' | t : 'Confirm visit' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .dlg{background:var(--white);border-radius:12px;padding:22px;width:min(92vw,420px);box-shadow:0 16px 48px rgba(0,0,0,.25);max-height:90vh;overflow-y:auto}
    .grid2{display:grid;grid-template-columns:1fr 1fr;gap:10px}
  `],
})
export class DashboardComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly icons = inject(IconsService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly d = signal<DashboardData | null>(null);
  readonly reps = signal<RepListItem[]>([]);
  readonly recording = signal<DashSchedule | null>(null);
  readonly suggested = signal<number | null>(null);
  recordRep = ''; recordCount: number | null = null;
  recordTotalRequired: number | null = null; recordRequests: number | null = null;
  recordOutsource: number | null = null; recordNotes = '';

  constructor() {
    this.load();
  }

  load(): void {
    this.api.get<DashboardData>('/dashboard').subscribe({
      next: (data) => {
        this.d.set(data); this.loading.set(false); this.icons.render();
      },
      error: () => this.loading.set(false),
    });
  }

  collectorReps(): RepListItem[] { return this.reps().filter((r) => r.type === 'Collector' || r.type === 'Scanning'); }

  openRecord(v: DashSchedule): void {
    if (this.reps().length === 0)
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    this.recording.set(v);
    this.recordRep = ''; this.recordCount = null;
    this.recordTotalRequired = null; this.recordRequests = null; this.recordOutsource = null; this.recordNotes = '';
    this.suggested.set(null);
    this.api.get<{ suggested: number | null }>(`/daily/${v.id}/suggested-count`).subscribe({
      next: (r) => { this.suggested.set(r.suggested); if (this.recordCount === null && r.suggested !== null) this.recordCount = r.suggested; },
    });
  }
  closeRecord(): void { this.recording.set(null); }
  confirmRecord(): void {
    const v = this.recording();
    if (!v || this.recordCount === null || this.recordCount < 0) return;
    this.busy.set(true);
    this.api.post(`/daily/${v.id}/checkin?source=dashboard`, {
      sampleCount: this.recordCount,
      collectorRepId: this.recordRep || null,
      totalRequired: this.recordTotalRequired,
      requestCount: this.recordRequests,
      outsourceCount: this.recordOutsource,
      notes: this.recordNotes.trim() || null,
    }).subscribe({
      next: () => { this.busy.set(false); this.recording.set(null); this.load(); },
      error: () => this.busy.set(false),
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
