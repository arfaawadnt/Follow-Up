import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { BoardItem, PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportCsv, printTable, localToday, localTime, localDateTime } from '../../shared/export.util';

const STATUSES = ['All', 'Pending', 'Visited', 'Missed'];

@Component({
  selector: 'app-daily',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'daily' | t }}</div><h1>{{ 'daily_followup_board' | t : 'Daily Follow-up Board' }}</h1></div>
      <div style="display:flex;gap:8px">
        <button class="btn btn-s" (click)="exportExcel()" [disabled]="!filtered().length">Export Excel</button>
        <button class="btn btn-s" (click)="exportPdf()" [disabled]="!filtered().length">Export PDF</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(5,1fr);margin-bottom:14px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_visits' | t }}</div><div class="val">{{ k().total }}</div><div class="sub">{{ 'scheduled_2' | t }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'completed' | t }}</div><div class="val">{{ k().done }}</div><div class="sub">{{ 'visited_2' | t }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'pending_2' | t }}</div><div class="val">{{ k().pending }}</div><div class="sub">{{ 'awaiting_check_in' | t }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">{{ 'missed' | t }}</div><div class="val">{{ k().missed }}</div><div class="sub">{{ 'not_collected' | t }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'samples_today' | t }}</div><div class="val">{{ k().samples | number:'1.0-0' }}</div><div class="sub">{{ 'total_verified_samples' | t }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="start"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="end"></div>
        <div class="field"><label>{{ 'branch_2' | t }}</label>
          <select class="select" [(ngModel)]="branch"><option value="All">{{ 'all_2' | t }}</option>
            @for (b of opts('branch'); track b) { <option [value]="b">{{ b }}</option> }</select></div>
        <div class="field"><label>{{ 'governorate_2' | t }}</label>
          <select class="select" [(ngModel)]="gov"><option value="All">{{ 'all_2' | t }}</option>
            @for (g of opts('governorate'); track g) { <option [value]="g">{{ g }}</option> }</select></div>
      </div>
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
        <div class="field"><label>{{ 'city_2' | t }}</label>
          <select class="select" [(ngModel)]="city"><option value="All">{{ 'all_2' | t }}</option>
            @for (c of opts('city'); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        <div class="field"><label>{{ 'area_2' | t }}</label>
          <select class="select" [(ngModel)]="area"><option value="All">{{ 'all_2' | t }}</option>
            @for (a of opts('area'); track a) { <option [value]="a">{{ a }}</option> }</select></div>
        <div class="field"><label>{{ 'collector_rep' | t }}</label>
          <select class="select" [(ngModel)]="rep" (ngModelChange)="load()"><option value="All">{{ 'all_2' | t }}</option>
            @for (r of reps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
        <div class="field"><label>{{ 'search_name_code' | t }}</label>
          <input type="text" class="input" [(ngModel)]="query" [placeholder]="'search_lab_name_or_code' | t"></div>
      </div>
      <div style="display:flex;justify-content:space-between;align-items:center;margin-top:12px;padding-top:12px;border-top:1px solid var(--slate-150)">
        <div style="display:flex;gap:4px">
          @for (s of statuses; track s) {
            <span class="pill" [class.on]="status() === s" (click)="setStatus(s)">{{ s === 'All' ? ('all' | t) : (s.toLowerCase() | t : s) }}</span>
          }
        </div>
        <div style="display:flex;gap:8px">
          <button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_dates' | t }}</button>
          <button class="btn btn-s" (click)="reset()" style="height:36px">{{ 'reset_filters' | t }}</button>
        </div>
      </div>
    </div>

    <div class="card">
      @if (loading()) { <div class="empty">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table id="daily-table">
          <tr><th>{{ 'date' | t }} &amp; {{ 'time' | t }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'collector' | t }}</th>
            <th>{{ 'status' | t }}</th><th>{{ 'samples' | t }}</th><th>{{ 'marked_at' | t : 'Marked At' }}</th><th>Verified</th><th></th></tr>
          @for (v of filtered(); track v.visitId) {
            <tr>
              <td class="mono">{{ v.visitDate }}<div class="small muted">{{ v.scheduledTime }}</div></td>
              <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ sub(v) }}</div></td>
              <td>{{ v.rep ?? '—' }}</td>
              <td><span class="badge" [class]="badgeClass(v.status)">{{ statusLabel(v.status) }}</span>@if (v.transferDone) { <span class="badge b-info">{{ 'transferred' | t : 'Transferred' }}</span> }</td>
              <td class="mono">{{ v.samples ?? '—' }}</td>
              <td class="mono small">{{ marked(v) }}</td>
              <td>{{ v.adminChecked ? '✓' : '—' }}</td>
              <td class="actions">
                @if (v.status === 'Pending') {
                  <button class="btn btn-mini btn-p" (click)="openRecord(v)" [disabled]="busy()">{{ 'record_visit' | t : 'Record visit' }}</button>
                  <button class="btn btn-mini btn-s" (click)="miss(v)" [disabled]="busy()">{{ 'miss' | t : 'missed' }}</button>
                }
                @if ((v.status === 'Visited' || v.status === 'Received') && !v.adminChecked && auth.has('VerifyDailyFollowup')) {
                  <button class="btn btn-mini" (click)="verify(v)" [disabled]="busy()">{{ 'verify' | t : 'Verify' }}</button>
                }
              </td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty">{{ 'no_visits_today' | t }}</td></tr> }
        </table>
      }
    </div>

    <!-- Record-visit popup (SRS FR-5): visit context + sample count prefilled with the suggested value. -->
    @if (recording(); as v) {
      <div class="overlay" (click)="closeRecord()">
        <div class="dlg" (click)="$event.stopPropagation()">
          <h3 style="margin:0 0 4px">{{ 'record_visit' | t : 'Record visit' }}</h3>
          <div class="small muted" style="margin-bottom:14px">{{ v.lab }} · {{ v.labDisplayCode }}</div>
          <div class="small muted" style="margin-bottom:12px">Scheduled {{ v.scheduledTime }} · {{ v.area ?? '—' }} · {{ v.rep ?? '—' }}</div>
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
            <div class="field"><label>Total Required</label><input type="number" min="0" class="input" [(ngModel)]="recordTotalRequired"></div>
            <div class="field"><label>No of Requests</label><input type="number" min="0" class="input" [(ngModel)]="recordRequests"></div>
          </div>
          <div class="field" style="margin-top:10px">
            <label>No of Outsource Samples</label>
            <input type="number" min="0" class="input" [(ngModel)]="recordOutsource" style="width:100%">
            <div class="small muted" style="margin-top:2px">A value &gt; 0 creates an outsource-sample row automatically.</div>
          </div>
          <div class="field" style="margin-top:10px">
            <label>Notes (optional)</label>
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
    .actions{display:flex;gap:6px;align-items:center}.num{width:66px}
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .dlg{background:var(--white);border-radius:12px;padding:22px;width:min(92vw,420px);box-shadow:0 16px 48px rgba(0,0,0,.25)}
    .grid2{display:grid;grid-template-columns:1fr 1fr;gap:10px}
    .field label{display:block;font:600 11px var(--ui);color:var(--slate-600);margin-bottom:4px}
  `],
})
export class DailyComponent {
  private readonly api = inject(ApiService);
  private readonly icons = inject(IconsService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<BoardItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  readonly status = signal('All');
  readonly statuses = STATUSES;
  readonly recording = signal<BoardItem | null>(null);
  readonly suggested = signal<number | null>(null);
  recordCount: number | null = null;
  recordRep = '';
  recordTotalRequired: number | null = null;
  recordRequests: number | null = null;
  recordOutsource: number | null = null;
  recordNotes = '';

  readonly collectorReps = computed(() => this.reps().filter((r) => r.type === 'Collector' || r.type === 'Scanning'));

  private readonly today = localToday();
  start = this.today; end = this.today;
  branch = 'All'; gov = 'All'; city = 'All'; area = 'All'; rep = 'All'; query = '';

  readonly filtered = computed(() => {
    const q = this.query.trim().toLowerCase();
    return this.items().filter((i) =>
      (this.branch === 'All' || i.branch === this.branch) &&
      (this.gov === 'All' || i.governorate === this.gov) &&
      (this.city === 'All' || i.city === this.city) &&
      (this.area === 'All' || i.area === this.area) &&
      (!q || i.lab?.toLowerCase().includes(q) || i.labDisplayCode?.toLowerCase().includes(q)));
  });

  readonly k = computed(() => {
    const f = this.filtered();
    const done = f.filter((r) => r.status === 'Visited' || r.status === 'Received');
    return { total: f.length, done: done.length, pending: f.filter((r) => r.status === 'Pending').length,
      missed: f.filter((r) => r.status === 'Missed').length, samples: done.reduce((a, r) => a + (r.samples ?? 0), 0) };
  });

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    this.load();
  }

  opts(field: 'branch' | 'governorate' | 'city' | 'area'): string[] {
    return [...new Set(this.items().map((i) => i[field]).filter((x): x is string => !!x))].sort();
  }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { start: this.start, end: this.end, status: this.status() };
    if (this.rep !== 'All') params['rep'] = this.rep;
    this.api.get<BoardItem[]>('/daily', params).subscribe({
      next: (b) => { this.items.set(b); this.loading.set(false); this.icons.render(); },
      error: () => this.loading.set(false),
    });
  }
  setStatus(s: string): void { this.status.set(s); this.load(); }
  reset(): void { this.start = this.today; this.end = this.today; this.branch = this.gov = this.city = this.area = this.rep = 'All'; this.query = ''; this.status.set('All'); this.load(); }

  // ---- Record-visit popup (SRS FR-5) ----

  openRecord(v: BoardItem): void {
    this.recording.set(v);
    this.recordCount = null;
    this.recordRep = v.collectorRepId ?? '';
    this.recordTotalRequired = null;
    this.recordRequests = null;
    this.recordOutsource = null;
    this.recordNotes = '';
    this.suggested.set(null);
    this.api.get<{ suggested: number | null }>(`/daily/${v.visitId}/suggested-count`).subscribe({
      next: (r) => { this.suggested.set(r.suggested); if (this.recordCount === null && r.suggested !== null) this.recordCount = r.suggested; },
    });
  }
  closeRecord(): void { this.recording.set(null); }
  confirmRecord(): void {
    const v = this.recording();
    if (!v || this.recordCount === null || this.recordCount < 0) return;
    this.busy.set(true);
    this.api.post(`/daily/${v.visitId}/checkin?source=daily`, {
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

  miss(v: BoardItem): void {
    if (!window.confirm(`Mark the ${v.scheduledTime} visit to ${v.lab} as missed?`)) return;
    this.run(this.api.post(`/daily/${v.visitId}/miss?source=daily`));
  }
  verify(v: BoardItem): void { this.run(this.api.post(`/daily/${v.visitId}/verify?source=daily`, { verified: true })); }

  private run(obs: { subscribe: Function }): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }

  sub(v: BoardItem): string { return [v.labDisplayCode, v.branch, v.area, v.governorate].filter(Boolean).join(' · '); }
  marked(v: BoardItem): string { return localTime(v.markedAt); }
  statusLabel(s: string): string { return s === 'Visited' ? 'Collected' : s; }

  private exportRows(): (string | number | null)[][] {
    return this.filtered().map((v) => [v.visitDate, v.scheduledTime, v.lab, v.labDisplayCode, v.branch, v.area, v.governorate,
      v.rep, this.statusLabel(v.status), v.samples, localDateTime(v.markedAt), v.adminChecked ? 'Yes' : 'No']);
  }
  exportExcel(): void {
    exportCsv('daily-followup.csv',
      ['Date', 'Time', 'Laboratory', 'Code', 'Branch', 'Area', 'Governorate', 'Collector', 'Status', 'Samples', 'Marked at', 'Verified'],
      this.exportRows());
  }
  exportPdf(): void {
    printTable('Daily Follow-up Board',
      ['Date', 'Time', 'Laboratory', 'Code', 'Branch', 'Area', 'Governorate', 'Collector', 'Status', 'Samples', 'Marked at', 'Verified'],
      this.exportRows());
  }

  badgeClass(status: string): string {
    const s = status.toLowerCase();
    if (s === 'visited' || s === 'received') return 'b-ok';
    if (s === 'pending') return 'b-warn';
    if (s === 'missed') return 'b-bad';
    return 'b-neu';
  }
}
