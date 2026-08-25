import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { BoardItem, PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportCsv, printTable, localToday } from '../../shared/export.util';

const STATUSES = ['All', 'Pending', 'Visited', 'Missed'];

@Component({
  selector: 'app-daily',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'daily' | t }}</div><h1>{{ 'daily' | t }}</h1></div>
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
            <th>{{ 'status' | t }}</th><th>{{ 'samples' | t }}</th><th>Marked at</th><th>Verified</th><th></th></tr>
          @for (v of filtered(); track v.visitId) {
            <tr>
              <td class="mono">{{ v.visitDate }}<div class="small muted">{{ v.scheduledTime }}</div></td>
              <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ sub(v) }}</div></td>
              <td>{{ v.rep ?? '—' }}</td>
              <td><span class="badge" [class]="badgeClass(v.status)">{{ v.status | t }}</span>@if (v.transferDone) { <span class="badge b-info">{{ 'transferred' | t : 'Transferred' }}</span> }</td>
              <td class="mono">{{ v.samples ?? '—' }}</td>
              <td class="mono small">{{ v.markedAt ?? '—' }}</td>
              <td>{{ v.adminChecked ? '✓' : '—' }}</td>
              <td class="actions">
                @if (v.status === 'Pending') {
                  <input class="input num" type="number" min="0" [(ngModel)]="counts[v.visitId]" placeholder="#">
                  <button class="btn btn-mini btn-p" (click)="checkin(v)" [disabled]="busy()">{{ 'record_visit' | t : 'Record' }}</button>
                  <button class="btn btn-mini btn-s" (click)="act(v, 'miss')" [disabled]="busy()">{{ 'miss' | t }}</button>
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
  `,
  styles: [`.actions{display:flex;gap:6px;align-items:center}.num{width:66px}`],
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
  counts: Record<string, number> = {};

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
      (!q || i.lab?.toLowerCase().includes(q) || i.labCode?.toLowerCase().includes(q)));
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

  checkin(v: BoardItem): void { this.run(this.api.post(`/daily/${v.visitId}/checkin?source=daily`, { sampleCount: this.counts[v.visitId] ?? 0 })); }
  act(v: BoardItem, action: 'miss' | 'undo'): void { this.run(this.api.post(`/daily/${v.visitId}/${action}?source=daily`)); }
  verify(v: BoardItem): void { this.run(this.api.post(`/daily/${v.visitId}/verify?source=daily`, { verified: true })); }

  private run(obs: { subscribe: Function }): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }

  sub(v: BoardItem): string { return [v.labCode, v.branch, v.area, v.governorate].filter(Boolean).join(' · '); }

  private exportRows(): (string | number | null)[][] {
    return this.filtered().map((v) => [v.visitDate, v.scheduledTime, v.lab, v.labCode, v.branch, v.area, v.governorate,
      v.rep, v.status, v.samples, v.markedAt, v.adminChecked ? 'Yes' : 'No']);
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
