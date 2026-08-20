import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { SampleTracking } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface ReportRow { area: string; date: string; count: number; stage: string; }

@Component({
  selector: 'app-sampletracking',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'sample_tracking' | t }}</div><h1>{{ 'sample_tracking' | t }}</h1></div>
    </div>

    <div class="tabs" style="margin-bottom:14px">
      <button class="tab" [class.on]="tab() === 'assignments'" (click)="tab.set('assignments')">{{ 'assignments' | t : 'Assignments' }}</button>
      <button class="tab" [class.on]="tab() === 'report'" (click)="setReport()">{{ 'report' | t : 'Report' }}</button>
    </div>

    @if (tab() === 'assignments') {
      <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:20px">
        <div class="kpi kpi-blue"><div class="lbl">{{ 'records' | t : 'Records' }}</div><div class="val">{{ filtered().length }}</div><div class="sub">{{ 'in_range' | t : 'in range' }}</div></div>
        <div class="kpi kpi-teal"><div class="lbl">{{ 'samples' | t }}</div><div class="val">{{ totalSamples() | number:'1.0-0' }}</div><div class="sub">{{ 'total' | t }}</div></div>
        <div class="kpi kpi-green"><div class="lbl">{{ 'completed' | t }}</div><div class="val">{{ completed() }}</div><div class="sub">{{ 'fully_processed' | t : 'fully processed' }}</div></div>
      </div>

      <div class="card" style="padding:20px;margin-bottom:20px">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="start"></div>
          <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="end"></div>
          <div class="field"><label>{{ 'area_2' | t }}</label>
            <select class="select" [(ngModel)]="area"><option value="All">{{ 'all_2' | t }}</option>@for (a of areas(); track a) { <option [value]="a">{{ a }}</option> }</select></div>
          <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_dates' | t }}</button></div>
        </div>
        @if (auth.has('SampleTracking')) {
          <form class="frm-grid" [formGroup]="form" (ngSubmit)="add()" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end;margin-top:12px;padding-top:12px;border-top:1px solid var(--slate-150)">
            <div class="field"><label>{{ 'new_entry' | t : 'New entry — area' }}</label><input class="input" formControlName="area"></div>
            <div class="field"><label>{{ 'count' | t : 'Count' }}</label><input class="input" type="number" min="1" formControlName="count"></div>
            <div class="field"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()" style="height:36px">{{ 'add' | t : 'Add' }}</button></div>
          </form>
        }
      </div>

      <div class="card" style="padding:0;overflow:hidden">
        @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
            <thead><tr><th>{{ 'area_2' | t }}</th><th>{{ 'date' | t }}</th><th>{{ 'count' | t : 'Count' }}</th>
              <th>{{ 'data_entry' | t : 'Data entry' }}</th><th>{{ 'review' | t : 'Review' }}</th><th>{{ 'sort' | t : 'Sort' }}</th>
              <th>{{ 'status' | t }}</th><th></th></tr></thead>
            <tbody>
              @for (s of filtered(); track s.id) {
                <tr>
                  <td><b style="color:var(--slate-900)">{{ s.area }}</b></td>
                  <td class="mono small">{{ s.date }}</td>
                  <td class="mono" style="font-weight:700">{{ s.count }}</td>
                  <td>{{ s.dataEntryBy ?? '—' }}</td>
                  <td>{{ s.reviewBy ?? '—' }}</td>
                  <td>{{ s.sortBy ?? '—' }}</td>
                  <td>@if (s.isComplete) { <span class="badge b-ok">{{ 'complete' | t : 'Complete' }}</span> } @else { <span class="badge b-warn">{{ 'in_progress' | t }}</span> }</td>
                  <td class="actions">
                    @if (auth.has('SampleTracking') && !s.isComplete) {
                      @if (!s.reviewBy) { <button class="btn btn-mini" (click)="advance(s, 'Review')" [disabled]="busy()">{{ 'review' | t : 'Review' }}</button> }
                      @if (s.reviewBy && !s.sortBy) { <button class="btn btn-mini" (click)="advance(s, 'Sort')" [disabled]="busy()">{{ 'sort' | t : 'Sort' }}</button> }
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No records.' }}</td></tr> }
            </tbody>
          </table></div>
        }
      </div>
    } @else {
      <div class="card" style="padding:20px;margin-bottom:20px">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="rStart"></div>
          <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="rEnd"></div>
          <div class="field"><button class="btn btn-p" (click)="loadReport()" style="height:36px">{{ 'apply_dates' | t }}</button></div>
        </div>
      </div>
      <div class="card" style="padding:0;overflow:hidden">
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'area_2' | t }}</th><th>{{ 'date' | t }}</th><th>{{ 'count' | t : 'Count' }}</th><th>{{ 'stage' | t : 'Stage' }}</th></tr></thead>
          <tbody>
            @for (r of report(); track $index) {
              <tr><td>{{ r.area }}</td><td class="mono small">{{ r.date }}</td><td class="mono">{{ r.count }}</td><td><span class="badge b-neu">{{ r.stage }}</span></td></tr>
            } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      </div>
    }
  `,
  styles: [`
    .tabs { display:flex; gap:6px; }
    .tab { background:var(--white); border:1px solid var(--slate-300); color:var(--slate-700); border-radius:var(--r-btn); padding:7px 16px; font:600 12.5px var(--ui); cursor:pointer; }
    .tab.on { background:var(--primary-blue); color:#fff; border-color:var(--primary-blue); }
    .actions { display:flex; gap:6px; }
  `],
})
export class SampleTrackingComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly tab = signal<'assignments' | 'report'>('assignments');
  readonly items = signal<SampleTracking[]>([]);
  readonly report = signal<ReportRow[]>([]);

  private readonly today = new Date().toISOString().slice(0, 10);
  start = this.today; end = this.today; area = 'All';
  rStart = this.today; rEnd = this.today;

  readonly form = this.fb.group({ area: this.fb.control('', Validators.required), count: this.fb.control(1, [Validators.required, Validators.min(1)]) });

  readonly filtered = computed(() => this.items().filter((s) => this.area === 'All' || s.area === this.area));
  readonly areas = computed(() => [...new Set(this.items().map((s) => s.area))].sort());
  readonly totalSamples = computed(() => this.filtered().reduce((a, s) => a + s.count, 0));
  readonly completed = computed(() => this.filtered().filter((s) => s.isComplete).length);

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<SampleTracking[]>('/sample-tracking', { start: this.start, end: this.end }).subscribe({
      next: (s) => { this.items.set(s); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  setReport(): void { this.tab.set('report'); if (this.report().length === 0) this.loadReport(); }
  loadReport(): void {
    this.api.get<ReportRow[]>('/sample-tracking/report', { from: this.rStart, to: this.rEnd }).subscribe({ next: (r) => this.report.set(r) });
  }

  add(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.api.post('/sample-tracking', { ...this.form.getRawValue(), date: this.start }).subscribe({
      next: () => { this.busy.set(false); this.form.patchValue({ area: '', count: 1 }); this.load(); }, error: () => this.busy.set(false),
    });
  }
  advance(s: SampleTracking, step: 'Review' | 'Sort'): void {
    this.busy.set(true);
    this.api.post(`/sample-tracking/${s.id}/advance`, { step }).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
}
