import { localToday } from '../../shared/export.util';
import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, MarketingVisit, PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const PURPOSES: { value: string; label: string }[] = [
  { value: 'Pitch', label: 'New Contract Pitch' },
  { value: 'Renewal', label: 'Contract Renewal' },
  { value: 'ComplaintResolution', label: 'Complaint Resolution' },
  { value: 'Promotion', label: 'Promotion Campaign' },
  { value: 'Onboarding', label: 'Onboarding Training' },
  { value: 'Reactivation', label: 'Reactivation Meeting' },
  { value: 'Routine', label: 'Routine Relationship' },
];
const STATUSES = ['All', 'Scheduled', 'Completed', 'Cancelled'];

@Component({
  selector: 'app-marketing',
  standalone: true,
  imports: [DatePipe, FormsModule, ReactiveFormsModule, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'marketing_visit_followup' | t : 'Marketing' }}</div><h1>{{ 'marketing_visit_followup' | t : 'Marketing Visits' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('AddMarketing')) { <button class="btn btn-p" (click)="openForm()">{{ 'schedule_btn' | t : 'Schedule visit' }}</button> }</div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total' | t }}</div><div class="val">{{ items().length }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">Scheduled</div><div class="val">{{ count('Scheduled') }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">Completed</div><div class="val">{{ count('Completed') }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">Cancelled</div><div class="val">{{ count('Cancelled') }}</div></div>
    </div>

    <div class="card" style="padding:12px;margin-bottom:16px;display:flex;gap:6px">
      @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="status.set(s)">{{ s === 'All' ? ('all' | t) : s }} ({{ s === 'All' ? items().length : count(s) }})</span> }
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'ref' | t : 'Ref' }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'rep' | t }}</th><th>{{ 'purpose' | t }}</th><th>{{ 'date' | t }}</th><th>{{ 'time' | t : 'Time' }}</th><th>{{ 'status' | t }}</th><th>{{ 'outcome_plan' | t : 'Outcome / Plan' }}</th><th></th></tr></thead>
          <tbody>
            @for (v of shown(); track v.id) {
              <tr>
                <td class="mono">{{ v.reference }}</td>
                <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ v.labDisplayCode }}@if (v.area) { · {{ v.area }} }</div></td>
                <td>{{ v.rep ?? '—' }}</td><td>{{ purposeLabel(v.purpose) }}</td>
                <td class="mono small">{{ v.scheduledDate | date:'mediumDate' }}</td>
                <td class="mono small">{{ v.scheduledTime ?? '—' }}</td>
                <td><span class="badge" [class]="badge(v.status)">{{ v.status }}</span></td>
                <td class="small">
                  @if (v.status === 'Scheduled') { {{ v.plan ? 'Plan: ' + v.plan : '—' }} }
                  @else { {{ v.outcome ?? '—' }} }
                </td>
                <td class="actions">
                  @if (v.status === 'Scheduled' && auth.has('UpdateMarketing')) {
                    <button class="btn btn-mini btn-t" (click)="openComplete(v)" [disabled]="busy()">{{ 'complete_btn' | t : 'Complete' }}</button>
                    <button class="btn btn-mini btn-s" (click)="cancel(v)" [disabled]="busy()">{{ 'cancel_btn' | t : 'Cancel' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="9" class="empty" style="text-align:center;padding:24px">{{ 'no_mkt_visits' | t : 'No marketing visits.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    @if (showForm()) {
      <div class="overlay" (click)="showForm.set(false)">
        <div class="dlg" (click)="$event.stopPropagation()" style="width:min(94vw,560px)">
          <div class="dlg-head"><h2>{{ 'schedule_mkt_visit' | t : 'Schedule marketing visit' }}</h2><button class="btn btn-mini btn-s" (click)="showForm.set(false)">✕</button></div>
          <form [formGroup]="form" (ngSubmit)="submit()" style="padding:16px">
            @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'laboratory_lbl' | t : 'Laboratory *' }}</label>
                <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
              <div class="field"><label>{{ 'marketing_rep_lbl' | t : 'Marketing rep *' }}</label>
                <select class="select" formControlName="representativeId"><option value="">—</option>@for (r of marketingReps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
              <div class="field"><label>{{ 'date_lbl' | t : 'Date *' }}</label><input type="date" class="input" formControlName="scheduledDate"></div>
              <div class="field"><label>{{ 'time' | t : 'Time' }}</label><input type="time" class="input" formControlName="scheduledTime"></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'purpose' | t }}</label>
                <select class="select" formControlName="purpose">@for (p of purposes; track p.value) { <option [value]="p.value">{{ p.label }}</option> }</select></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'visit_plan' | t : 'Visit plan / notes' }}</label><textarea class="input" rows="3" formControlName="plan"></textarea></div>
            </div>
            <div style="display:flex;gap:8px;margin-top:14px;justify-content:flex-end">
              <button class="btn btn-s" type="button" (click)="showForm.set(false)">{{ 'cancel' | t }}</button>
              <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ 'schedule_btn' | t : 'Schedule' }}</button>
            </div>
          </form>
        </div>
      </div>
    }

    @if (completing(); as v) {
      <div class="overlay" (click)="completing.set(null)">
        <div class="dlg" (click)="$event.stopPropagation()" style="width:min(94vw,480px)">
          <div class="dlg-head"><h2>{{ 'complete_visit' | t : 'Complete visit' }} — {{ v.reference }}</h2><button class="btn btn-mini btn-s" (click)="completing.set(null)">✕</button></div>
          <div style="padding:16px">
            <div class="small muted" style="margin-bottom:8px">{{ v.lab }} · {{ purposeLabel(v.purpose) }} · {{ v.scheduledDate | date:'mediumDate' }}</div>
            <div class="field"><label>{{ 'outcome' | t : 'Outcome' }} *</label><textarea class="input" rows="4" [(ngModel)]="outcomeText"></textarea></div>
            <div style="display:flex;gap:8px;margin-top:14px;justify-content:flex-end">
              <button class="btn btn-s" (click)="completing.set(null)">{{ 'cancel' | t }}</button>
              <button class="btn btn-p" [disabled]="!outcomeText.trim() || busy()" (click)="complete()">{{ 'complete_btn' | t : 'Complete' }}</button>
            </div>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .actions{display:flex;gap:6px}
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .dlg{background:var(--white);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);max-height:88vh;overflow-y:auto}
    .dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-200)}
    .dlg-head h2{font-size:15px;margin:0}
  `],
})
export class MarketingComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<MarketingVisit[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  // The reference only offers marketing-type reps in the schedule popup.
  readonly marketingReps = computed(() => this.reps().filter((r) => r.type === 'Marketing'));
  readonly showForm = signal(false);
  readonly completing = signal<MarketingVisit | null>(null);
  readonly formError = signal<string | null>(null);
  readonly status = signal('All');
  readonly purposes = PURPOSES;
  readonly statuses = STATUSES;
  outcomeText = '';

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    representativeId: this.fb.control('', Validators.required),
    purpose: this.fb.control('Routine', Validators.required), // reference default: Routine Relationship
    scheduledDate: this.fb.control(localToday(), Validators.required),
    scheduledTime: this.fb.control('10:00'), // reference default

    plan: this.fb.control(''),
  });

  readonly shown = computed(() => this.status() === 'All' ? this.items() : this.items().filter((v) => v.status === this.status()));

  constructor() { this.load(); }

  count(status: string): number { return this.items().filter((v) => v.status === status).length; }
  badge(s: string): string { return s === 'Completed' ? 'b-ok' : s === 'Cancelled' ? 'b-bad' : 'b-info'; }
  purposeLabel(p: string): string { return PURPOSES.find((x) => x.value === p)?.label ?? p; }

  load(): void {
    this.loading.set(true);
    this.api.get<PagedResult<MarketingVisit>>('/marketing', { pageSize: 200 }).subscribe({
      next: (r) => { this.items.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  openForm(): void {
    this.showForm.set(true); this.formError.set(null);
    if (this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    }
  }
  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true); this.formError.set(null);
    const v = this.form.getRawValue();
    // TimeOnly binds "HH:mm:ss" only — pad the time input's "HH:mm".
    const time = v.scheduledTime ? (v.scheduledTime.length === 5 ? v.scheduledTime + ':00' : v.scheduledTime) : null;
    this.api.post('/marketing', { ...v, scheduledTime: time, plan: v.plan.trim() || null }).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.patchValue({ laboratoryId: '', representativeId: '', purpose: 'Routine', scheduledTime: '10:00', plan: '' }); this.load(); },
      error: (e) => { this.busy.set(false); this.formError.set(e?.error?.detail ?? 'Schedule failed.'); },
    });
  }
  openComplete(v: MarketingVisit): void { this.outcomeText = ''; this.completing.set(v); }
  complete(): void {
    const v = this.completing(); if (!v || !this.outcomeText.trim()) return;
    this.busy.set(true);
    this.api.post(`/marketing/${v.id}/complete`, { outcome: this.outcomeText.trim() }).subscribe({
      next: () => { this.busy.set(false); this.completing.set(null); this.load(); },
      error: () => this.busy.set(false),
    });
  }
  cancel(v: MarketingVisit): void {
    const reason = window.prompt('Cancellation reason (optional):') ?? '';
    this.busy.set(true);
    this.api.post(`/marketing/${v.id}/cancel`, { reason: reason.trim() || null }).subscribe({
      next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false),
    });
  }
}
