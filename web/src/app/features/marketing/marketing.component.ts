import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, MarketingVisit, PagedResult, RepListItem } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';
import { TranslatePipe } from '../../core/i18n';

const PURPOSES = ['Routine', 'Onboarding', 'Complaint follow-up', 'Contract', 'Retention'];

@Component({
  selector: 'app-marketing',
  standalone: true,
  imports: [DatePipe, StatusBadgePipe, TranslatePipe, ReactiveFormsModule],
  template: `
    <div class="head">
      <h1 class="display page-title">{{ 'marketing.title' | t }}</h1>
      @if (auth.has('AddMarketing')) {
        <button class="btn btn-p" (click)="toggleForm()">{{ showForm() ? 'Close' : 'Schedule visit' }}</button>
      }
    </div>

    @if (showForm()) {
      <form class="dcard formcard" [formGroup]="form" (ngSubmit)="submit()">
        <div class="cbody">
          @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
          <div class="row">
            <div class="field"><label>Laboratory <span class="req">*</span></label>
              <select formControlName="laboratoryId"><option value="">— select —</option>
                @for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }
              </select></div>
            <div class="field"><label>Representative <span class="req">*</span></label>
              <select formControlName="representativeId"><option value="">— select —</option>
                @for (r of reps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }
              </select></div>
          </div>
          <div class="row">
            <div class="field"><label>Purpose <span class="req">*</span></label>
              <select formControlName="purpose">@for (p of purposes; track p) { <option>{{ p }}</option> }</select></div>
            <div class="field"><label>Date <span class="req">*</span></label><input type="date" formControlName="scheduledDate"></div>
          </div>
        </div>
        <div class="foot">
          <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">Schedule</button>
          <button class="btn btn-s" type="button" (click)="toggleForm()">Cancel</button>
        </div>
      </form>
    }

    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">{{ 'common.loading' | t }}</div> }
      @if (result(); as r) {
        <table class="app">
          <thead><tr><th>Lab</th><th>Purpose</th><th>Date</th><th>{{ 'labs.status' | t }}</th><th>Outcome</th><th>Actions</th></tr></thead>
          <tbody>
            @for (v of r.items; track v.id) {
              <tr><td class="client-code">{{ v.labDisplayCode }}</td><td>{{ v.purpose }}</td>
                <td>{{ v.scheduledDate | date:'mediumDate' }}</td>
                <td><span class="badge" [class]="v.status | statusBadge">{{ v.status }}</span></td>
                <td>{{ v.outcome ?? '—' }}</td>
                <td class="actions">
                  @if (v.status === 'Scheduled' && auth.has('UpdateMarketing')) {
                    <button class="btn btn-mini btn-t" (click)="complete(v)" [disabled]="busy()">Complete</button>
                    <button class="btn btn-mini btn-s" (click)="cancel(v)" [disabled]="busy()">Cancel</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty">{{ 'common.empty' | t }}</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`
    .head { display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; }
    .page-title{font-size:22px;margin:0}.empty{color:var(--slate-500);text-align:center;padding:24px}
    .formcard { margin-bottom:16px; }
    .row { display:flex; gap:16px; flex-wrap:wrap; }
    .field { flex:1; min-width:220px; margin-bottom:12px; }
    .field label { display:block; font:600 12px var(--ui); color:var(--slate-600); margin-bottom:5px; }
    .field input, .field select { width:100%; border:1px solid var(--slate-300); border-radius:var(--r-input);
      padding:8px 10px; font-size:13px; background:var(--white); color:var(--slate-900); }
    .req { color: var(--danger, #dc2626); }
    .foot { display:flex; gap:10px; justify-content:flex-end; padding:14px 18px; border-top:1px solid var(--slate-150); background:var(--filter-bg); }
    .actions { display:flex; gap:6px; }
    .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
  `],
})
export class MarketingComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly result = signal<PagedResult<MarketingVisit> | null>(null);
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

  load(): void {
    this.loading.set(true);
    this.api.get<PagedResult<MarketingVisit>>('/marketing', { pageSize: 100 }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  toggleForm(): void {
    const next = !this.showForm();
    this.showForm.set(next);
    this.formError.set(null);
    if (next && this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    }
  }

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.formError.set(null);
    this.api.post('/marketing', this.form.getRawValue()).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.patchValue({ laboratoryId: '', representativeId: '' }); this.load(); },
      error: (err) => { this.busy.set(false); this.formError.set(err?.error?.detail ?? 'Schedule failed.'); },
    });
  }

  complete(v: MarketingVisit): void {
    const outcome = window.prompt('Visit outcome:');
    if (outcome === null || !outcome.trim()) return;
    this.run(this.api.post(`/marketing/${v.id}/complete`, { outcome: outcome.trim() }));
  }

  cancel(v: MarketingVisit): void {
    const reason = window.prompt('Cancellation reason (optional):') ?? '';
    this.run(this.api.post(`/marketing/${v.id}/cancel`, { reason: reason.trim() || null }));
  }

  private run(obs: { subscribe: Function }): void {
    this.busy.set(true);
    (obs as any).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
}
