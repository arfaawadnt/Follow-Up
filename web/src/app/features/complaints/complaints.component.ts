import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ComplaintAuditRow, ComplaintDetail, ComplaintListItem, LabListItem, PagedResult, RefItem, RepListItem } from '../../core/models';
import { EsignPanelComponent } from '../../shared/esign-panel.component';
import { TranslatePipe } from '../../core/i18n';

const CATEGORIES = ['Representative Issue', 'Call Center Issue', 'Result Quality', 'Data Entry Mistake'];
const CHANNELS = ['WhatsApp', 'Phone Call', 'Email', 'In-person'];
const STATUSES = ['All', 'Open', 'InProgress', 'Resolved'];
const OUTCOME_TYPES = ['Corrective Action', 'Preventive Action', 'Training', 'Process Change', 'Compensation', 'No Action Needed'];
// Stepper: display label ↔ backend stage name, in workflow order.
const STEPS: { label: string; stage: string }[] = [
  { label: 'Logged', stage: 'Logged' },
  { label: 'Acknowledge', stage: 'Acknowledged' },
  { label: 'Validity', stage: 'ValidityChecked' },
  { label: 'Investigate', stage: 'Investigation' },
  { label: 'Outcome', stage: 'BusinessOutcome' },
  { label: 'Resolve', stage: 'Resolution' },
];

@Component({
  selector: 'app-complaints',
  standalone: true,
  imports: [DatePipe, SlicePipe, FormsModule, ReactiveFormsModule, EsignPanelComponent, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'complaint_logs' | t : 'Complaints' }}</div><h1>{{ 'complaint_logs' | t : 'Complaints' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('AddComplaints')) { <button class="btn btn-p" (click)="openLog()">{{ 'log_complaint_btn' | t : 'Log complaint' }}</button> }</div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total' | t }}</div><div class="val">{{ result()?.total ?? 0 }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">Open</div><div class="val">{{ count('Open') }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">In progress</div><div class="val">{{ count('InProgress') }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">Resolved</div><div class="val">{{ count('Resolved') }}</div></div>
    </div>

    <div class="card" style="padding:12px;margin-bottom:10px;display:flex;gap:6px;flex-wrap:wrap">
      @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="setStatus(s)">{{ s === 'All' ? ('all' | t) : s }} ({{ s === 'All' ? (result()?.total ?? 0) : count(s) }})</span> }
    </div>
    <div class="card" style="padding:12px;margin-bottom:16px;display:flex;gap:6px;flex-wrap:wrap">
      <span class="pill" [class.on]="category() === ''" (click)="setCategory('')">{{ 'all' | t : 'All' }}</span>
      @for (c of categories; track c) { <span class="pill" [class.on]="category() === c" (click)="setCategory(c)">{{ c }} ({{ catCount(c) }})</span> }
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @if (!loading() && result(); as r) {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'ref' | t : 'Ref' }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'category' | t }}</th><th>{{ 'complaint' | t : 'Complaint' }}</th>
            <th>{{ 'via' | t : 'Via' }}</th><th>{{ 'assigned_to' | t : 'Assigned to' }}</th><th>{{ 'status' | t }}</th><th>{{ 'actions_3' | t : 'Actions' }}</th></tr></thead>
          <tbody>
            @for (c of r.items; track c.id) {
              <tr>
                <td class="mono">{{ c.reference }}</td>
                <td><b style="color:var(--slate-900)">{{ c.lab }}</b><div class="small muted">{{ c.labCategory ?? c.labDisplayCode }}</div></td>
                <td><span class="badge b-neu">{{ c.category }}</span></td>
                <td style="max-width:280px"><div class="small">{{ c.description | slice:0:80 }}{{ c.description.length > 80 ? '…' : '' }}</div>
                  <div class="small muted">{{ c.ageDays === 0 ? ('today' | t : 'today') : c.ageDays + 'd' }}@if (c.resolution) { · {{ 'resolved_by' | t : 'resolved by' }} {{ c.resolution }} }</div></td>
                <td>{{ c.via }}</td>
                <td>{{ c.assignedTo ?? '—' }}</td>
                <td><span class="badge" [class]="badge(c.status)">{{ c.status }}</span><div class="small muted">{{ stageLabel(c.stage) }}</div></td>
                <td class="actions">
                  <button class="btn btn-mini btn-s" (click)="openDetail(c.id)">{{ 'details' | t : 'Details' }}</button>
                  @if (c.status === 'Open' && auth.has('UpdateComplaints')) { <button class="btn btn-mini btn-p" (click)="investigate(c)" [disabled]="busy()">{{ 'investigate' | t : 'Investigate' }}</button> }
                  @if (c.status === 'Resolved' && auth.has('UpdateComplaints')) { <button class="btn btn-mini btn-s" (click)="reopen(c.id)" [disabled]="busy()">{{ 'reopen_btn' | t : 'Reopen' }}</button> }
                </td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_complaints_match' | t : 'No complaints match.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    <!-- Log complaint popup -->
    @if (showLog()) {
      <div class="modal-backdrop" (click)="showLog.set(false)">
        <div class="modal" (click)="$event.stopPropagation()" style="max-width:620px">
          <div class="modal-head"><h2>{{ 'log_complaint_btn' | t : 'Log complaint' }}</h2><button class="btn btn-mini btn-s" (click)="showLog.set(false)">✕</button></div>
          <form [formGroup]="form" (ngSubmit)="submit()" style="padding:16px">
            @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'laboratory_lbl' | t : 'Laboratory *' }}</label>
                <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
              <div class="field"><label>{{ 'category' | t }}</label><select class="select" formControlName="category">@for (c of categories; track c) { <option>{{ c }}</option> }</select></div>
              <div class="field"><label>{{ 'representative' | t : 'Representative' }}</label>
                <select class="select" formControlName="representativeId"><option value="">—</option>@for (r of reps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
              <div class="field"><label>{{ 'complaint_datetime' | t : 'Complaint date/time' }}</label><input type="datetime-local" class="input" formControlName="receivedAt"></div>
              <div class="field"><label>{{ 'received_via' | t : 'Received via' }}</label><select class="select" formControlName="viaChannel">@for (c of channels; track c) { <option>{{ c }}</option> }</select></div>
              <div class="field"><label>{{ 'assign_to' | t : 'Assign to' }}</label>
                <select class="select" formControlName="assignedTeam">@for (t of teams(); track t.id) { <option [value]="t.nameEn">{{ t.nameEn }}</option> } @empty { <option value="">—</option> }</select></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'description_lbl' | t : 'Description *' }}</label><textarea class="input" rows="3" formControlName="details"></textarea></div>
            </div>
            <div style="display:flex;gap:8px;margin-top:14px;justify-content:flex-end">
              <button class="btn btn-s" type="button" (click)="showLog.set(false)">{{ 'cancel' | t }}</button>
              <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ 'submit' | t : 'Submit' }}</button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- Details popup -->
    @if (detail(); as d) {
      <div class="modal-backdrop" (click)="closeDetail()">
        <div class="modal" (click)="$event.stopPropagation()" style="max-width:760px;max-height:90vh;overflow-y:auto">
          <div class="modal-head">
            <h2>{{ d.reference }} — {{ d.lab }} <span class="badge" [class]="badge(d.status)">{{ d.status }}</span>
              @if (d.stage === 'RejectedInvalid') { <span class="badge b-bad">{{ 'rejected_invalid' | t : 'Rejected — invalid' }}</span> }</h2>
            <button class="btn btn-mini btn-s" (click)="closeDetail()">✕</button>
          </div>

          <!-- Stage stepper -->
          <div class="stepper">
            @for (s of steps; track s.stage; let i = $index) {
              <div class="step" [class.done]="i < stepIndex(d)" [class.cur]="i === stepIndex(d) && d.stage !== 'RejectedInvalid'">
                <div class="dot">{{ i + 1 }}</div><div class="slbl">{{ s.label }}</div>
              </div>
              @if (i < steps.length - 1) { <div class="sline" [class.done]="i < stepIndex(d)"></div> }
            }
          </div>

          <div class="toolbar" style="padding:0 16px;display:flex;gap:6px">
            <span class="pill" [class.on]="detailTab() === 'meta'" (click)="detailTab.set('meta')">{{ 'details_meta' | t : 'Details & Metadata' }}</span>
            <span class="pill" [class.on]="detailTab() === 'audit'" (click)="loadAudit(d.id)">{{ 'audit_log' | t : 'Audit Log' }}</span>
          </div>

          @if (detailTab() === 'meta') {
            <div style="padding:14px 16px">
              <dl class="proffields">
                <dt>{{ 'category' | t }}</dt><dd>{{ d.category }}</dd>
                <dt>{{ 'received_via' | t : 'Received via' }}</dt><dd>{{ d.viaChannel }}</dd>
                <dt>{{ 'representative' | t : 'Representative' }}</dt><dd>{{ d.representativeName ?? '—' }}</dd>
                <dt>{{ 'complaint_datetime' | t : 'Received at' }}</dt><dd>{{ d.receivedAt ? (d.receivedAt | date:'medium') : '—' }}</dd>
                <dt>{{ 'assigned_to' | t : 'Assigned to' }}</dt><dd>{{ d.assignedTeam ?? '—' }}</dd>
                <dt>{{ 'logged_at' | t : 'Logged at' }}</dt><dd>{{ d.createdAt | date:'medium' }}</dd>
                <dt>{{ 'description_lbl' | t : 'Description' }}</dt><dd>{{ d.details }}</dd>
                @if (d.isValid !== null) { <dt>{{ 'validity' | t : 'Validity' }}</dt><dd><span class="badge" [class]="d.isValid ? 'b-ok' : 'b-bad'">{{ d.isValid ? ('valid' | t : 'Valid') : ('invalid' | t : 'Invalid') }}</span> {{ d.validityNotes ?? '' }}</dd> }
                @if (d.investigationNotes) { <dt>{{ 'investigation' | t : 'Investigation' }}</dt><dd>{{ d.investigationNotes }}</dd> }
                @if (d.outcomeType) { <dt>{{ 'outcome' | t : 'Outcome' }}</dt><dd><b>{{ d.outcomeType }}</b>@if (d.outcomeSummary) { — {{ d.outcomeSummary }} }</dd> }
                @if (d.resolutionSummary) { <dt>{{ 'resolution' | t : 'Resolution' }}</dt><dd>{{ d.resolutionSummary }}</dd> }
                @if (d.resolvedAt) { <dt>{{ 'resolved_at_lbl' | t : 'Resolved at' }}</dt><dd>{{ d.resolvedAt | date:'medium' }} · {{ d.resolvedBy }}</dd> }
              </dl>

              <!-- Stage action forms -->
              @if (auth.has('UpdateComplaints') && d.status !== 'Resolved' && d.stage !== 'RejectedInvalid') {
                <div class="stagebox">
                  @if (actionError()) { <div class="inline-banner inline-banner-error">{{ actionError() }}</div> }
                  @switch (d.stage) {
                    @case ('Logged') {
                      <b>{{ 'acknowledge' | t : 'Acknowledge' }}</b>
                      <div class="small muted" style="margin:4px 0 8px">{{ 'ack_hint' | t : 'Confirm the complaint was received and is being handled.' }}</div>
                      <button class="btn btn-p" [disabled]="busy()" (click)="acknowledge(d)">{{ 'acknowledge' | t : 'Acknowledge' }}</button>
                    }
                    @case ('Acknowledged') {
                      <b>{{ 'validity_check' | t : 'Validity check' }}</b>
                      <div style="display:flex;gap:14px;margin:8px 0">
                        <label class="small"><input type="radio" name="valid" [value]="true" [(ngModel)]="stageValid"> {{ 'valid' | t : 'Valid' }}</label>
                        <label class="small"><input type="radio" name="valid" [value]="false" [(ngModel)]="stageValid"> {{ 'invalid' | t : 'Invalid — reject' }}</label>
                      </div>
                      <textarea class="input" rows="2" [(ngModel)]="stageNotes" placeholder="Validity notes (optional)"></textarea>
                      <button class="btn btn-p" style="margin-top:8px" [disabled]="stageValid === null || busy()" (click)="checkValidity(d)">{{ 'save_stage' | t : 'Save validity' }}</button>
                    }
                    @case ('ValidityChecked') {
                      <b>{{ 'investigation' | t : 'Investigation' }}</b>
                      <textarea class="input" rows="3" style="margin-top:8px" [(ngModel)]="stageNotes" placeholder="Investigation notes / root cause *"></textarea>
                      <button class="btn btn-p" style="margin-top:8px" [disabled]="!stageNotes.trim() || busy()" (click)="recordInvestigation(d)">{{ 'record_investigation' | t : 'Record investigation' }}</button>
                    }
                    @case ('Investigation') {
                      <b>{{ 'business_outcome' | t : 'Business outcome' }}</b>
                      <div class="frm-grid" style="grid-template-columns:220px 1fr;gap:10px;margin-top:8px;align-items:start">
                        <select class="select" [(ngModel)]="stageOutcomeType">@for (o of outcomeTypes; track o) { <option>{{ o }}</option> }</select>
                        <textarea class="input" rows="2" [(ngModel)]="stageNotes" placeholder="Outcome summary (optional)"></textarea>
                      </div>
                      <button class="btn btn-p" style="margin-top:8px" [disabled]="busy()" (click)="recordOutcome(d)">{{ 'save_outcome' | t : 'Save outcome' }}</button>
                    }
                    @case ('BusinessOutcome') {
                      <b>{{ 'resolve' | t : 'Resolve' }}</b>
                      <textarea class="input" rows="2" style="margin-top:8px" [(ngModel)]="stageNotes" placeholder="Resolution summary (optional)"></textarea>
                      <button class="btn btn-t" style="margin-top:8px" [disabled]="busy()" (click)="resolve(d)">{{ 'resolve_complaint_btn' | t : 'Resolve complaint' }}</button>
                    }
                  }
                </div>
              }
              @if (auth.has('UpdateComplaints') && (d.status === 'Resolved' || d.stage === 'RejectedInvalid')) {
                <div class="stagebox"><button class="btn btn-s" [disabled]="busy()" (click)="reopen(d.id)">{{ 'reopen_btn' | t : 'Reopen' }}</button></div>
              }

              <div style="margin-top:14px"><b>{{ 'signatures' | t : 'Signatures' }}</b><app-esign-panel module="complaint" [recordId]="d.id" /></div>
            </div>
          }
          @if (detailTab() === 'audit') {
            <div style="padding:14px 16px">
              @if (audit(); as rows) {
                <table class="grid-table"><thead><tr><th>{{ 'when' | t : 'When' }}</th><th>{{ 'actor' | t : 'Actor' }}</th><th>{{ 'action' | t : 'Action' }}</th></tr></thead>
                  <tbody>@for (a of rows; track $index) { <tr><td class="mono small">{{ a.occurredAt | date:'short' }}</td><td>{{ a.actor }}</td><td>{{ a.action }}</td></tr> } @empty { <tr><td colspan="3" class="muted small">—</td></tr> }</tbody>
                </table>
              } @else { <span class="muted small">{{ 'loading' | t : 'Loading…' }}</span> }
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    .actions{display:flex;gap:6px;align-items:center;flex-wrap:wrap}
    .modal-backdrop{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:60}
    .modal{background:var(--white);border-radius:12px;width:94%;box-shadow:0 20px 60px rgba(0,0,0,.25)}
    .modal-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-200)}
    .modal-head h2{font-size:15px;margin:0;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
    .stepper{display:flex;align-items:center;padding:16px;gap:4px}
    .step{display:flex;flex-direction:column;align-items:center;gap:4px;min-width:64px}
    .step .dot{width:26px;height:26px;border-radius:50%;background:var(--slate-200);color:var(--slate-500);display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:700}
    .step.done .dot{background:var(--teal-500);color:#fff}
    .step.cur .dot{background:var(--slate-900);color:#fff}
    .step .slbl{font-size:11px;color:var(--slate-500)}
    .step.cur .slbl{color:var(--slate-900);font-weight:700}
    .sline{flex:1;height:2px;background:var(--slate-200);margin-bottom:16px}
    .sline.done{background:var(--teal-500)}
    .proffields{display:grid;grid-template-columns:150px 1fr;gap:6px 10px;margin:0}
    .proffields dt{font-size:12px;color:var(--slate-500);font-weight:600}
    .proffields dd{margin:0;font-size:13px;color:var(--slate-900)}
    .stagebox{margin-top:14px;padding:12px;border:1px solid var(--slate-200);border-radius:8px;background:var(--filter-bg)}
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
  readonly reps = signal<RepListItem[]>([]);
  readonly teams = signal<RefItem[]>([]);
  readonly showLog = signal(false);
  readonly detail = signal<ComplaintDetail | null>(null);
  readonly detailTab = signal<'meta' | 'audit'>('meta');
  readonly audit = signal<ComplaintAuditRow[] | null>(null);
  readonly formError = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly status = signal('All');
  readonly category = signal('');
  readonly categories = CATEGORIES; readonly channels = CHANNELS; readonly statuses = STATUSES;
  readonly outcomeTypes = OUTCOME_TYPES; readonly steps = STEPS;
  stageValid: boolean | null = null;
  stageNotes = '';
  stageOutcomeType = OUTCOME_TYPES[0];

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    category: this.fb.control(CATEGORIES[0], Validators.required),
    representativeId: this.fb.control(''),
    receivedAt: this.fb.control(''),
    viaChannel: this.fb.control(CHANNELS[0], Validators.required),
    assignedTeam: this.fb.control(''),
    details: this.fb.control('', Validators.required),
  });

  constructor() { this.load(); }

  count(s: string): number { return (this.result()?.items ?? []).filter((c) => c.status === s).length; }
  catCount(c: string): number { return (this.result()?.items ?? []).filter((x) => x.category === c).length; }
  badge(s: string): string { return s === 'Resolved' ? 'b-ok' : s === 'InProgress' ? 'b-warn' : 'b-bad'; }
  stageLabel(stage: string): string { return STEPS.find((s) => s.stage === stage)?.label ?? stage; }
  stepIndex(d: ComplaintDetail): number {
    if (d.stage === 'RejectedInvalid') return 2;
    const i = STEPS.findIndex((s) => s.stage === d.stage);
    return i < 0 ? 0 : i;
  }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 100 };
    if (this.status() !== 'All') params['status'] = this.status();
    if (this.category()) params['category'] = this.category();
    this.api.get<PagedResult<ComplaintListItem>>('/complaints', params).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  setStatus(s: string): void { this.status.set(s); this.load(); }
  setCategory(c: string): void { this.category.set(c); this.load(); }

  // ---- Log popup ----
  openLog(): void {
    this.showLog.set(true); this.formError.set(null);
    if (this.labs().length === 0) {
      this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
      this.api.get<RefItem[]>('/setup/refs', { type: 'Team' }).subscribe({ next: (t) => {
        this.teams.set(t);
        // The reference defaults the assignment to the first team (no empty option).
        if (t.length && !this.form.controls.assignedTeam.value) this.form.controls.assignedTeam.setValue(t[0].nameEn);
      } });
    }
  }
  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true); this.formError.set(null);
    const v = this.form.getRawValue();
    this.api.post('/complaints', {
      laboratoryId: v.laboratoryId, category: v.category, viaChannel: v.viaChannel,
      assignedTeam: v.assignedTeam || null, details: v.details,
      representativeId: v.representativeId || null,
      receivedAt: v.receivedAt ? new Date(v.receivedAt).toISOString() : null,
    }).subscribe({
      next: () => { this.busy.set(false); this.showLog.set(false); this.form.reset({ category: CATEGORIES[0], viaChannel: CHANNELS[0], assignedTeam: this.teams()[0]?.nameEn ?? '' }); this.load(); },
      error: (e) => { this.busy.set(false); this.formError.set(e?.error?.detail ?? 'Submit failed.'); },
    });
  }

  // ---- Details popup ----
  openDetail(id: string): void {
    this.detailTab.set('meta'); this.audit.set(null); this.actionError.set(null);
    this.resetStageInputs();
    this.api.get<ComplaintDetail>(`/complaints/${id}`).subscribe({ next: (d) => this.detail.set(d) });
  }
  closeDetail(): void { this.detail.set(null); }
  refreshDetail(id: string): void {
    this.resetStageInputs();
    this.api.get<ComplaintDetail>(`/complaints/${id}`).subscribe({ next: (d) => this.detail.set(d) });
    this.load();
  }
  private resetStageInputs(): void { this.stageValid = null; this.stageNotes = ''; this.stageOutcomeType = OUTCOME_TYPES[0]; }
  loadAudit(id: string): void {
    this.detailTab.set('audit');
    if (!this.audit()) this.api.get<ComplaintAuditRow[]>(`/complaints/${id}/audit`).subscribe({ next: (rows) => this.audit.set(rows) });
  }

  // ---- Workflow actions ----
  private act(id: string, obs: { subscribe: Function }): void {
    this.busy.set(true); this.actionError.set(null);
    (obs as { subscribe: Function }).subscribe({
      next: () => { this.busy.set(false); this.refreshDetail(id); },
      error: (e: { error?: { detail?: string } }) => { this.busy.set(false); this.actionError.set(e?.error?.detail ?? 'Action failed.'); },
    });
  }
  investigate(c: ComplaintListItem): void {
    // Start handling (Open -> InProgress), then open the detail popup on the workflow.
    this.busy.set(true);
    this.api.post(`/complaints/${c.id}/start`).subscribe({
      next: () => { this.busy.set(false); this.load(); this.openDetail(c.id); },
      error: () => { this.busy.set(false); this.openDetail(c.id); },
    });
  }
  acknowledge(d: ComplaintDetail): void {
    const moveStage = () => this.act(d.id, this.api.post(`/complaints/${d.id}/stage`, { stage: 'Acknowledged' }));
    if (d.status === 'Open') this.api.post(`/complaints/${d.id}/start`).subscribe({ next: moveStage, error: moveStage });
    else moveStage();
  }
  checkValidity(d: ComplaintDetail): void {
    if (this.stageValid === null) return;
    this.act(d.id, this.api.post(`/complaints/${d.id}/advance`, { stage: 'ValidityChecked', isValid: this.stageValid, notes: this.stageNotes.trim() || null }));
  }
  recordInvestigation(d: ComplaintDetail): void {
    if (!this.stageNotes.trim()) return;
    this.act(d.id, this.api.post(`/complaints/${d.id}/advance`, { stage: 'Investigation', notes: this.stageNotes.trim() }));
  }
  recordOutcome(d: ComplaintDetail): void {
    this.act(d.id, this.api.post(`/complaints/${d.id}/advance`, { stage: 'BusinessOutcome', outcomeType: this.stageOutcomeType, summary: this.stageNotes.trim() || null }));
  }
  resolve(d: ComplaintDetail): void {
    this.act(d.id, this.api.post(`/complaints/${d.id}/resolve`, { resolutionSummary: this.stageNotes.trim() || null }));
  }
  reopen(id: string): void {
    this.busy.set(true); this.actionError.set(null);
    this.api.post(`/complaints/${id}/reopen`).subscribe({
      next: () => { this.busy.set(false); if (this.detail()) this.refreshDetail(id); else this.load(); },
      error: (e) => { this.busy.set(false); this.actionError.set(e?.error?.detail ?? 'Reopen failed.'); },
    });
  }
}
