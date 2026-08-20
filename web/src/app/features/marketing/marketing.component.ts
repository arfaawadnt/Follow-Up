import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, MarketingVisit, PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const PURPOSES = ['Routine', 'Onboarding', 'Complaint follow-up', 'Contract', 'Retention'];
const STATUSES = ['All', 'Scheduled', 'Completed', 'Cancelled'];

@Component({
  selector: 'app-marketing',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'marketing_visit_followup' | t : 'Marketing' }}</div><h1>{{ 'marketing_visit_followup' | t : 'Marketing Visits' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('AddMarketing')) { <button class="btn btn-p" (click)="toggle()">{{ showForm() ? ('cancel' | t) : ('schedule_btn' | t : 'Schedule visit') }}</button> }</div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total' | t }}</div><div class="val">{{ items().length }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">Scheduled</div><div class="val">{{ count('Scheduled') }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">Completed</div><div class="val">{{ count('Completed') }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">Cancelled</div><div class="val">{{ count('Cancelled') }}</div></div>
    </div>

    @if (showForm()) {
      <form class="card" style="padding:20px;margin-bottom:20px" [formGroup]="form" (ngSubmit)="submit()">
        @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'laboratory_lbl' | t : 'Laboratory' }}</label>
            <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
          <div class="field"><label>{{ 'marketing_rep_lbl' | t : 'Representative' }}</label>
            <select class="select" formControlName="representativeId"><option value="">—</option>@for (r of reps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
          <div class="field"><label>{{ 'purpose' | t }}</label><select class="select" formControlName="purpose">@for (p of purposes; track p) { <option>{{ p }}</option> }</select></div>
          <div class="field"><label>{{ 'date_lbl' | t : 'Date' }}</label><input type="date" class="input" formControlName="scheduledDate"></div>
        </div>
        <div style="margin-top:12px"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ 'schedule_btn' | t : 'Schedule' }}</button></div>
      </form>
    }

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'laboratory' | t }}</th><th>{{ 'rep' | t }}</th><th>{{ 'purpose' | t }}</th><th>{{ 'date' | t }}</th><th>{{ 'status' | t }}</th><th>{{ 'outcome' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (v of items(); track v.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ v.labDisplayCode }}@if (v.area) { · {{ v.area }} }</div></td>
                <td>{{ v.rep ?? '—' }}</td><td>{{ v.purpose }}</td><td class="mono small">{{ v.scheduledDate | date:'mediumDate' }}</td>
                <td><span class="badge" [class]="badge(v.status)">{{ v.status }}</span></td>
                <td>{{ v.outcome ?? '—' }}</td>
                <td class="actions">
                  @if (v.status === 'Scheduled' && auth.has('UpdateMarketing')) {
                    <button class="btn btn-mini btn-t" (click)="complete(v)" [disabled]="busy()">{{ 'complete_btn' | t : 'Complete' }}</button>
                    <button class="btn btn-mini btn-s" (click)="cancel(v)" [disabled]="busy()">{{ 'cancel_btn' | t : 'Cancel' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:24px">{{ 'no_mkt_visits' | t : 'No marketing visits.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}`],
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
  readonly showForm = signal(false);
  readonly formError = signal<string | null>(null);
  readonly purposes = PURPOSES;

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    representativeId: this.fb.control('', Validators.required),
    purpose: this.fb.control(PURPOSES[0], Validators.required),
    scheduledDate: this.fb.control(new Date().toISOString().slice(0, 10), Validators.required),
  });

  constructor() { this.load(); }

  count(status: string): number { return this.items().filter((v) => v.status === status).length; }
  badge(s: string): string { return s === 'Completed' ? 'b-ok' : s === 'Cancelled' ? 'b-bad' : 'b-info'; }

  load(): void {
    this.loading.set(true);
    this.api.get<PagedResult<MarketingVisit>>('/marketing', { pageSize: 200 }).subscribe({
      next: (r) => { this.items.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  toggle(): void {
    const n = !this.showForm(); this.showForm.set(n); this.formError.set(null);
    if (n && this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    }
  }
  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true); this.formError.set(null);
    this.api.post('/marketing', this.form.getRawValue()).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.patchValue({ laboratoryId: '', representativeId: '' }); this.load(); },
      error: (e) => { this.busy.set(false); this.formError.set(e?.error?.detail ?? 'Schedule failed.'); },
    });
  }
  complete(v: MarketingVisit): void {
    const outcome = window.prompt('Visit outcome:'); if (outcome === null || !outcome.trim()) return;
    this.run(this.api.post(`/marketing/${v.id}/complete`, { outcome: outcome.trim() }));
  }
  cancel(v: MarketingVisit): void {
    const reason = window.prompt('Cancellation reason (optional):') ?? '';
    this.run(this.api.post(`/marketing/${v.id}/cancel`, { reason: reason.trim() || null }));
  }
  private run(obs: { subscribe: Function }): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
}
