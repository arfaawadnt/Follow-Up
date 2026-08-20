import { Component, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ComplaintAuditRow, ComplaintListItem, LabListItem, PagedResult } from '../../core/models';
import { EsignPanelComponent } from '../../shared/esign-panel.component';
import { TranslatePipe } from '../../core/i18n';

const STAGES = ['Acknowledged', 'ValidityChecked', 'Investigation', 'BusinessOutcome', 'Resolution', 'RejectedInvalid'];
const CATEGORIES = ['Sample Quality', 'Turnaround Time', 'Result Accuracy', 'Service', 'Billing', 'Other'];
const CHANNELS = ['Phone', 'Email', 'WhatsApp', 'In Person'];
const STATUSES = ['All', 'Open', 'InProgress', 'Resolved'];

@Component({
  selector: 'app-complaints',
  standalone: true,
  imports: [DatePipe, SlicePipe, ReactiveFormsModule, EsignPanelComponent, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'complaint_logs' | t : 'Complaints' }}</div><h1>{{ 'complaint_logs' | t : 'Complaints' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('AddComplaints')) { <button class="btn btn-p" (click)="toggleForm()">{{ showForm() ? 'Close' : ('log_complaint_btn' | t : 'Log complaint') }}</button> }</div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total' | t }}</div><div class="val">{{ result()?.total ?? 0 }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">Open</div><div class="val">{{ count('Open') }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">In progress</div><div class="val">{{ count('InProgress') }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">Resolved</div><div class="val">{{ count('Resolved') }}</div></div>
    </div>

    @if (showForm()) {
      <form class="card" style="padding:20px;margin-bottom:20px" [formGroup]="form" (ngSubmit)="submit()">
        @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
        <div class="frm-grid" style="grid-template-columns:repeat(2,1fr);gap:12px">
          <div class="field"><label>{{ 'laboratory_lbl' | t : 'Laboratory' }}</label>
            <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
          <div class="field"><label>{{ 'category' | t }}</label><select class="select" formControlName="category">@for (c of categories; track c) { <option>{{ c }}</option> }</select></div>
          <div class="field"><label>{{ 'received_via' | t : 'Received via' }}</label><select class="select" formControlName="viaChannel">@for (c of channels; track c) { <option>{{ c }}</option> }</select></div>
          <div class="field"><label>{{ 'assign_to' | t : 'Assign to' }}</label><input class="input" formControlName="assignedTeam" placeholder="optional"></div>
          <div class="field" style="grid-column:1/-1"><label>{{ 'description_lbl' | t : 'Description' }}</label><textarea class="input" rows="3" formControlName="details"></textarea></div>
        </div>
        <div style="display:flex;gap:8px;margin-top:12px"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">Submit</button><button class="btn btn-s" type="button" (click)="toggleForm()">{{ 'cancel' | t }}</button></div>
      </form>
    }

    <div class="card" style="padding:12px;margin-bottom:16px;display:flex;gap:6px">
      @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="setStatus(s)">{{ s === 'All' ? ('all' | t) : s }}</span> }
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @if (!loading() && result(); as r) {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'ref' | t : 'Ref' }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'category' | t }}</th><th>{{ 'via' | t : 'Via' }}</th>
            <th>{{ 'status' | t }}</th><th>{{ 'stage' | t : 'Stage' }}</th><th>{{ 'age' | t : 'Age' }}</th><th>{{ 'actions_3' | t : 'Actions' }}</th></tr></thead>
          <tbody>
            @for (c of r.items; track c.id) {
              <tr>
                <td class="mono">{{ c.reference }}</td>
                <td><b style="color:var(--slate-900)">{{ c.lab }}</b><div class="small muted">{{ c.description | slice:0:60 }}…</div></td>
                <td>{{ c.category }}</td><td>{{ c.via }}</td>
                <td><span class="badge" [class]="badge(c.status)">{{ c.status }}</span></td>
                <td>{{ c.stage }}</td>
                <td class="small muted">{{ c.ageDays === 0 ? ('today' | t) : c.ageDays + 'd' }}</td>
                <td class="actions">
                  @if (c.status === 'Open' && auth.has('UpdateComplaints')) { <button class="btn btn-mini btn-p" (click)="act(c, 'start')" [disabled]="busy()">{{ 'investigate' | t : 'Start' }}</button> }
                  @if (c.status === 'InProgress' && auth.has('UpdateComplaints')) { <button class="btn btn-mini btn-t" (click)="act(c, 'resolve')" [disabled]="busy()">{{ 'resolved' | t : 'Resolve' }}</button> }
                  @if (c.status === 'Resolved' && auth.has('UpdateComplaints')) { <button class="btn btn-mini btn-s" (click)="act(c, 'reopen')" [disabled]="busy()">{{ 'reopen_btn' | t : 'Reopen' }}</button> }
                  @if (c.status !== 'Resolved' && auth.has('UpdateComplaints')) {
                    <select class="select" style="padding:3px 6px;font-size:11px" (change)="advance(c, $event)"><option value="">Stage →</option>@for (s of stages; track s) { <option [value]="s">{{ s }}</option> }</select>
                  }
                  <button class="btn btn-mini btn-s" (click)="toggle(c.id)">{{ expanded() === c.id ? 'Hide' : ('details' | t : 'Details') }}</button>
                </td>
              </tr>
              @if (expanded() === c.id) {
                <tr class="detailrow"><td colspan="8">
                  <div style="padding:8px 4px"><b>Signatures</b><app-esign-panel module="complaint" [recordId]="c.id" /></div>
                  <div style="padding:8px 4px"><b>{{ 'complaint_logs' | t : 'Audit log' }}</b>
                    @if (audit()[c.id]; as rows) {
                      <table class="grid-table" style="margin-top:6px"><thead><tr><th>When</th><th>Actor</th><th>Action</th></tr></thead>
                        <tbody>@for (a of rows; track $index) { <tr><td class="mono small">{{ a.occurredAt | date:'short' }}</td><td>{{ a.actor }}</td><td>{{ a.action }}</td></tr> } @empty { <tr><td colspan="3" class="muted small">—</td></tr> }</tbody>
                      </table>
                    } @else { <span class="muted small"> loading…</span> }
                  </div>
                </td></tr>
              }
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_complaints_match' | t : 'No complaints match.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px;align-items:center;flex-wrap:wrap}.detailrow td{background:var(--filter-bg)}`],
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
  readonly status = signal('All');
  readonly audit = signal<Record<string, ComplaintAuditRow[]>>({});
  readonly stages = STAGES; readonly categories = CATEGORIES; readonly channels = CHANNELS; readonly statuses = STATUSES;

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    category: this.fb.control(CATEGORIES[0], Validators.required),
    viaChannel: this.fb.control(CHANNELS[0], Validators.required),
    assignedTeam: this.fb.control(''),
    details: this.fb.control('', Validators.required),
  });

  constructor() { this.load(); }

  count(s: string): number { return (this.result()?.items ?? []).filter((c) => c.status === s).length; }
  badge(s: string): string { return s === 'Resolved' ? 'b-ok' : s === 'InProgress' ? 'b-warn' : 'b-bad'; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 100 };
    if (this.status() !== 'All') params['status'] = this.status();
    this.api.get<PagedResult<ComplaintListItem>>('/complaints', params).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  setStatus(s: string): void { this.status.set(s); this.load(); }
  toggle(id: string): void {
    const cur = this.expanded() === id ? null : id;
    this.expanded.set(cur);
    if (cur && !this.audit()[cur]) this.api.get<ComplaintAuditRow[]>(`/complaints/${cur}/audit`).subscribe({ next: (rows) => this.audit.update((a) => ({ ...a, [cur]: rows })) });
  }
  toggleForm(): void {
    const n = !this.showForm(); this.showForm.set(n); this.formError.set(null);
    if (n && this.labs().length === 0) this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
  }
  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true); this.formError.set(null);
    const v = this.form.getRawValue();
    this.api.post('/complaints', { ...v, assignedTeam: v.assignedTeam || null }).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.reset({ category: CATEGORIES[0], viaChannel: CHANNELS[0] }); this.load(); },
      error: (e) => { this.busy.set(false); this.formError.set(e?.error?.detail ?? 'Submit failed.'); },
    });
  }
  act(c: ComplaintListItem, action: 'start' | 'resolve' | 'reopen'): void {
    this.busy.set(true);
    this.api.post(`/complaints/${c.id}/${action}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
  advance(c: ComplaintListItem, event: Event): void {
    const stage = (event.target as HTMLSelectElement).value; if (!stage) return;
    this.busy.set(true);
    this.api.post(`/complaints/${c.id}/stage`, { stage }).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => { this.busy.set(false); this.load(); } });
  }
}
