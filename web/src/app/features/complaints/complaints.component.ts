import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ComplaintListItem, LabListItem, PagedResult } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';
import { EsignPanelComponent } from '../../shared/esign-panel.component';

const STAGES = ['Acknowledged', 'ValidityChecked', 'Investigation', 'BusinessOutcome', 'Resolution', 'RejectedInvalid'];
const CATEGORIES = ['Sample Quality', 'Turnaround Time', 'Result Accuracy', 'Service', 'Billing', 'Other'];
const CHANNELS = ['Phone', 'Email', 'WhatsApp', 'In Person'];

@Component({
  selector: 'app-complaints',
  standalone: true,
  imports: [StatusBadgePipe, DatePipe, ReactiveFormsModule, EsignPanelComponent],
  template: `
    <div class="head">
      <h1 class="display page-title">Complaints</h1>
      @if (auth.has('AddComplaints')) {
        <button class="btn btn-p" (click)="toggleForm()">{{ showForm() ? 'Close' : 'Log complaint' }}</button>
      }
    </div>

    @if (showForm()) {
      <form class="dcard formcard" [formGroup]="form" (ngSubmit)="submit()">
        <div class="cbody">
          @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
          <div class="row">
            <div class="field"><label>Laboratory <span class="req">*</span></label>
              <select formControlName="laboratoryId">
                <option value="">— select —</option>
                @for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }
              </select></div>
            <div class="field"><label>Category <span class="req">*</span></label>
              <select formControlName="category">@for (c of categories; track c) { <option>{{ c }}</option> }</select></div>
          </div>
          <div class="row">
            <div class="field"><label>Channel <span class="req">*</span></label>
              <select formControlName="viaChannel">@for (c of channels; track c) { <option>{{ c }}</option> }</select></div>
            <div class="field"><label>Assigned team</label><input formControlName="assignedTeam" placeholder="optional"></div>
          </div>
          <div class="field wide"><label>Details <span class="req">*</span></label>
            <textarea formControlName="details" rows="3"></textarea></div>
        </div>
        <div class="foot">
          <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">Submit</button>
          <button class="btn btn-s" type="button" (click)="toggleForm()">Cancel</button>
        </div>
      </form>
    }

    <div class="dcard">
      <div class="cbody" style="padding:0">
        @if (loading()) { <div class="cbody">Loading…</div> }
        @if (result(); as r) {
          <table class="app">
            <thead><tr><th>Ref</th><th>Lab</th><th>Category</th><th>Stage</th><th>Status</th><th>Logged</th><th>Actions</th></tr></thead>
            <tbody>
              @for (c of r.items; track c.id) {
                <tr>
                  <td class="mono">{{ c.reference }}</td>
                  <td class="client-code">{{ c.labDisplayCode }}</td>
                  <td>{{ c.category }}</td>
                  <td>{{ c.stage }}</td>
                  <td><span class="badge" [class]="c.status | statusBadge">{{ c.status }}</span></td>
                  <td>{{ c.createdAt | date:'short' }}</td>
                  <td class="actions">
                    @if (c.status === 'Open' && auth.has('UpdateComplaints')) {
                      <button class="btn btn-mini btn-p" (click)="act(c, 'start')" [disabled]="busy()">Start</button>
                    }
                    @if (c.status === 'InProgress' && auth.has('UpdateComplaints')) {
                      <button class="btn btn-mini btn-t" (click)="act(c, 'resolve')" [disabled]="busy()">Resolve</button>
                    }
                    @if (c.status === 'Resolved' && auth.has('UpdateComplaints')) {
                      <button class="btn btn-mini btn-s" (click)="act(c, 'reopen')" [disabled]="busy()">Reopen</button>
                    }
                    @if (c.status !== 'Resolved' && auth.has('UpdateComplaints')) {
                      <select class="stage-sel" [disabled]="busy()" (change)="advance(c, $event)">
                        <option value="">Stage →</option>
                        @for (s of stages; track s) { <option [value]="s">{{ s }}</option> }
                      </select>
                    }
                    <button class="btn btn-mini btn-s" (click)="toggleSign(c.id)">{{ expanded() === c.id ? 'Hide sign' : 'Signatures' }}</button>
                  </td>
                </tr>
                @if (expanded() === c.id) {
                  <tr class="signrow"><td colspan="7">
                    <app-esign-panel module="complaint" [recordId]="c.id" />
                  </td></tr>
                }
              } @empty { <tr><td colspan="7" class="empty">No complaints.</td></tr> }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`
    .head { display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; }
    .page-title { font-size: 22px; margin: 0; }
    .empty { color: var(--slate-500); text-align: center; padding: 24px; }
    .formcard { margin-bottom: 16px; }
    .row { display:flex; gap:16px; flex-wrap:wrap; }
    .field { flex:1; min-width:220px; margin-bottom:12px; } .field.wide { min-width:100%; }
    .field label { display:block; font:600 12px var(--ui); color:var(--slate-600); margin-bottom:5px; }
    .field input, .field select, .field textarea { width:100%; border:1px solid var(--slate-300); border-radius:var(--r-input);
      padding:8px 10px; font-size:13px; background:var(--white); color:var(--slate-900); }
    .req { color: var(--danger, #dc2626); }
    .foot { display:flex; gap:10px; justify-content:flex-end; padding:14px 18px; border-top:1px solid var(--slate-150); background:var(--filter-bg); }
    .actions { display:flex; gap:6px; align-items:center; }
    .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
    .stage-sel { padding:4px 6px; font-size:11.5px; border:1px solid var(--slate-300); border-radius:var(--r-btn); background:var(--white); color:var(--slate-900); }
    .signrow td { background: var(--filter-bg); padding:12px 16px; }
  `],
})
export class ComplaintsComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly result = signal<PagedResult<ComplaintListItem> | null>(null);
  readonly labs = signal<LabListItem[]>([]);
  readonly showForm = signal(false);
  readonly expanded = signal<string | null>(null);
  readonly formError = signal<string | null>(null);
  readonly stages = STAGES;
  readonly categories = CATEGORIES;
  readonly channels = CHANNELS;

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    category: this.fb.control(CATEGORIES[0], Validators.required),
    viaChannel: this.fb.control(CHANNELS[0], Validators.required),
    assignedTeam: this.fb.control(''),
    details: this.fb.control('', Validators.required),
  });

  constructor() { this.load(); }

  toggleSign(id: string): void { this.expanded.update((cur) => (cur === id ? null : id)); }

  load(): void {
    this.loading.set(true);
    this.api.get<PagedResult<ComplaintListItem>>('/complaints', { pageSize: 50 }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  toggleForm(): void {
    const next = !this.showForm();
    this.showForm.set(next);
    this.formError.set(null);
    if (next && this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
    }
  }

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.formError.set(null);
    const v = this.form.getRawValue();
    this.api.post('/complaints', { ...v, assignedTeam: v.assignedTeam || null }).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.reset({ category: CATEGORIES[0], viaChannel: CHANNELS[0] }); this.load(); },
      error: (err) => { this.busy.set(false); this.formError.set(err?.error?.detail ?? 'Submit failed.'); },
    });
  }

  act(c: ComplaintListItem, action: 'start' | 'resolve' | 'reopen'): void {
    this.busy.set(true);
    this.api.post(`/complaints/${c.id}/${action}`).subscribe({
      next: () => { this.busy.set(false); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  advance(c: ComplaintListItem, event: Event): void {
    const stage = (event.target as HTMLSelectElement).value;
    if (!stage) return;
    this.busy.set(true);
    this.api.post(`/complaints/${c.id}/stage`, { stage }).subscribe({
      next: () => { this.busy.set(false); this.load(); },
      error: () => { this.busy.set(false); this.load(); },
    });
  }
}
