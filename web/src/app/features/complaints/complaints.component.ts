import { Component, ElementRef, HostListener, inject, signal, ViewChild } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ComplaintAuditRow, ComplaintCounts, ComplaintDetail, ComplaintListItem, LabListItem, PagedResult, RefItem, RepListItem } from '../../core/models';
import { EsignPanelComponent } from '../../shared/esign-panel.component';
import { TranslatePipe } from '../../core/i18n';

const CATEGORIES = ['Representative Issue', 'Call Center Issue', 'Result Quality', 'Data Entry Mistake'];
const CHANNELS = ['WhatsApp', 'Phone Call', 'Email', 'In-person'];
const STATUSES = ['All', 'Open', 'InProgress', 'Resolved'];
// Business outcome types as configured on the reference platform.
const OUTCOME_TYPES = ['Repeat Test / Service', 'Refund / Credit Note', 'Staff Training / Warning',
  'Customer Notified & Satisfied', 'No Action Required', 'Other Action'];
// Stepper: display label ↔ backend stage name, in workflow order.
const STEPS: { key: string; label: string; stage: string }[] = [
  { key: 'step_logged', label: 'Logged', stage: 'Logged' },
  { key: 'step_acknowledge', label: 'Acknowledge', stage: 'Acknowledged' },
  { key: 'step_validity', label: 'Validity', stage: 'ValidityChecked' },
  { key: 'step_investigate', label: 'Investigate', stage: 'Investigation' },
  { key: 'step_outcome', label: 'Outcome', stage: 'BusinessOutcome' },
  { key: 'step_resolve', label: 'Resolve', stage: 'Resolution' },
];
type StageForm = 'ack' | 'validity' | 'investigation' | 'outcome' | 'resolve';

@Component({
  selector: 'app-complaints',
  standalone: true,
  imports: [DatePipe, SlicePipe, FormsModule, ReactiveFormsModule, EsignPanelComponent, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'complaint_logs' | t : 'Complaints' }}</div><h1>{{ 'complaint_logs' | t : 'Complaints' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('AddComplaints')) { <button class="btn btn-p" (click)="openLog()">{{ 'log_complaint_btn' | t : 'Log complaint' }}</button> }</div>
    </div>

    <!-- Reference has no KPI cards here — one filter row: status pills + category dropdown -->
    <div class="card" style="padding:12px;margin-bottom:16px;display:flex;gap:6px;align-items:center;flex-wrap:wrap">
      @for (s of statuses; track s) { <button type="button" class="pill" [class.on]="status() === s" [attr.aria-pressed]="status() === s" (click)="setStatus(s)">{{ statusLabel(s) }} · {{ statusCount(s) }}</button> }
      <select class="select" style="width:auto;min-width:180px;margin-inline-start:10px" [ngModel]="category()" (ngModelChange)="setCategory($event)">
        <option value="">{{ 'all' | t : 'All' }}</option>
        @for (c of categories; track c) { <option [value]="c">{{ c }}</option> }
      </select>
    </div>

    <!-- CMP-16: the list is capped at 100; tell the user when there are more instead of silently truncating -->
    @if (result()?.truncated) {
      <div role="status" style="margin-bottom:16px;padding:8px 14px;border-radius:8px;background:#fef3c7;color:#92400e;font-size:13px">
        {{ 'complaints_truncated' | t : 'Showing the first' }} {{ result()?.pageSize }} {{ 'complaints_of' | t : 'of' }} {{ result()?.total }} — {{ 'complaints_narrow' | t : 'use the status or category filters to narrow the list.' }}
      </div>
    }

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @if (!loading() && result(); as r) {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'ref' | t : 'Ref' }}</th><th>{{ 'laboratory' | t }}</th><th>{{ 'category' | t }}</th><th>{{ 'complaint' | t : 'Complaint' }}</th>
            <th>{{ 'via' | t : 'Via' }}</th><th>{{ 'assigned_to' | t : 'Assigned to' }}</th><th>{{ 'status' | t }}</th><th style="width:190px"></th></tr></thead>
          <tbody>
            @for (c of r.items; track c.id) {
              <tr>
                <td class="mono">{{ c.reference }}</td>
                <td><b style="color:var(--slate-900)">{{ c.lab }}</b><div class="small muted">{{ c.labCategory ?? c.labDisplayCode }}</div></td>
                <td><span class="badge b-neu">{{ c.category }}</span></td>
                <td style="max-width:280px"><div class="small">{{ c.description | slice:0:80 }}{{ c.description.length > 80 ? '…' : '' }}</div>
                  <div class="small muted">
                    @if (c.status === 'Resolved') { {{ 'resolved_sub' | t : 'resolved' }} {{ c.resolvedAt | date:'dd/MM/yyyy' }}@if (c.resolutionSummary) { — {{ c.resolutionSummary }} } }
                    @else { {{ 'logged_sub' | t : 'Logged' }} {{ c.ageDays === 0 ? ('today' | t : 'today') : c.ageDays + ' ' + ('days_ago' | t : 'days ago') }} }
                  </div></td>
                <td>{{ c.via }}</td>
                <td>{{ c.assignedTo ?? '—' }}</td>
                <td><span class="badge" [class]="badge(c.status)">{{ c.status }}</span></td>
                <td class="actions">
                  @if (c.status === 'Resolved') {
                    <button class="btn-mini on" (click)="openDetail(c.id)">{{ 'details' | t : 'Details' }}</button>
                    @if (auth.has('UpdateComplaints')) { <button class="btn-mini red" (click)="reopen(c.id)" [disabled]="busy()">{{ 'reopen_btn' | t : 'Reopen' }}</button> }
                  } @else {
                    @if (auth.has('UpdateComplaints')) { <button class="btn-mini on" (click)="investigate(c)" [disabled]="busy()">{{ 'investigate' | t : 'Investigate' }}</button> }
                    @else { <button class="btn-mini on" (click)="openDetail(c.id)">{{ 'details' | t : 'Details' }}</button> }
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_complaints_match' | t : 'No complaints match.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    <!-- Log complaint popup -->
    @if (showLog()) {
      <div class="overlay" (click)="showLog.set(false)">
        <div #dlg class="dlg" role="dialog" aria-modal="true" aria-labelledby="logComplaintTitle" tabindex="-1"
             (click)="$event.stopPropagation()" style="width:min(94vw,620px)">
          <div class="dlg-head">
            <div><h2 id="logComplaintTitle">{{ 'log_complaint_title' | t : 'Log Complaint' }}</h2>
              <div class="small muted">{{ 'cmp_ref_auto_hint' | t : 'A sequential reference (CMP-nnn) is assigned automatically' }}</div></div>
            <button class="btn btn-mini btn-s" (click)="showLog.set(false)">✕</button>
          </div>
          <form [formGroup]="form" (ngSubmit)="submit()" style="padding:16px">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'laboratory_lbl' | t : 'Laboratory *' }}</label>
                <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
              <div class="field"><label>{{ 'category' | t }}</label><select class="select" formControlName="category">@for (c of categories; track c) { <option [value]="c">{{ c }}</option> }</select></div>
              <div class="field"><label>{{ 'representative' | t : 'Representative' }}</label>
                <select class="select" formControlName="representativeId"><option value="">—</option>@for (r of reps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
              <div class="field"><label>{{ 'complaint_datetime_2' | t : 'Complaint Date/Time' }}</label><input type="datetime-local" class="input" formControlName="receivedAt"></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'description_lbl' | t : 'Description *' }}</label><textarea class="input" rows="3" formControlName="details"></textarea></div>
              <div class="field"><label>{{ 'received_via' | t : 'Received via' }}</label><select class="select" formControlName="viaChannel">@for (c of channels; track c) { <option [value]="c">{{ c }}</option> }</select></div>
              <div class="field"><label>{{ 'assign_to' | t : 'Assign to' }}</label>
                <select class="select" formControlName="assignedTeam">@for (t of teams(); track t.id) { <option [value]="t.nameEn">{{ t.nameEn }}</option> } @empty { <option value="">—</option> }</select></div>
            </div>
            <div style="display:flex;gap:8px;margin-top:14px;justify-content:flex-end">
              <button class="btn btn-s" type="button" (click)="showLog.set(false)">{{ 'cancel' | t }}</button>
              <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ 'log_complaint_btn' | t : '+ Log complaint' }}</button>
            </div>
          </form>
        </div>
      </div>
    }

    <!-- Details popup (reference: "View Details" with stepper, metadata cards and staged forms) -->
    @if (detail(); as d) {
      <div class="overlay" (click)="closeDetail()">
        <div #dlg class="dlg" role="dialog" aria-modal="true" aria-labelledby="complaintDetailTitle" tabindex="-1"
             (click)="$event.stopPropagation()" style="width:min(94vw,800px);max-height:88vh;overflow-y:auto">
          <div class="dlg-head">
            <h2 id="complaintDetailTitle">{{ 'view_details' | t : 'View Details' }} <span class="badge" [class]="badge(d.status)">{{ d.status }}</span>
              @if (d.stage === 'RejectedInvalid') { <span class="badge b-bad">{{ 'invalid' | t : 'Invalid' }}</span> }</h2>
            <button class="btn btn-mini btn-s" (click)="closeDetail()">✕</button>
          </div>

          <!-- Stage stepper -->
          <div class="stepper">
            @for (s of steps; track s.stage; let i = $index) {
              <div class="step" [class.done]="i < stepIndex(d)" [class.cur]="i === stepIndex(d)">
                <div class="dot">{{ i < stepIndex(d) ? '✓' : i + 1 }}</div><div class="slbl">{{ s.key | t : s.label }}</div>
              </div>
              @if (i < steps.length - 1) { <div class="sarrow" [class.done]="i < stepIndex(d)">→</div> }
            }
          </div>

          <div class="toolbar" role="tablist" style="padding:0 16px;display:flex;gap:6px">
            <button type="button" class="pill" role="tab" [attr.aria-selected]="detailTab() === 'meta'" [class.on]="detailTab() === 'meta'" (click)="detailTab.set('meta')">{{ 'details_meta' | t : 'Details & Metadata' }}</button>
            <button type="button" class="pill" role="tab" [attr.aria-selected]="detailTab() === 'audit'" [class.on]="detailTab() === 'audit'" (click)="loadAudit(d.id)">{{ 'audit_log' | t : 'Audit Log' }}</button>
          </div>

          @if (detailTab() === 'meta') {
            <div style="padding:14px 16px">
              <div class="metacards">
                <div class="mcard"><div class="mlbl">{{ 'complaint_reference' | t : 'Complaint reference' }}</div><div class="mval mono" style="color:var(--teal-600, #0d7490)">{{ d.reference }}</div></div>
                <div class="mcard"><div class="mlbl">{{ 'category_validity' | t : 'Category & validity' }}</div><div class="mval">{{ d.category }} · {{ d.isValid === null ? '—' : (d.isValid ? 'VALID' : 'INVALID') }}</div></div>
                <div class="mcard"><div class="mlbl">{{ 'requester_channel' | t : 'Requester & channel' }}</div><div class="mval">{{ d.lab }} <span class="muted">({{ d.viaChannel }})</span></div></div>
                <div class="mcard"><div class="mlbl">{{ 'received_owner' | t : 'Received date & owner' }}</div><div class="mval">{{ (d.receivedAt ?? d.createdAt) | date:'dd/MM/yyyy' }} · {{ d.assignedTeam ?? '—' }}</div></div>
              </div>

              <div class="msec"><div class="mlbl">{{ 'complaint_description' | t : 'Complaint description' }}</div><div>{{ d.details }}</div></div>
              @if (d.isValid !== null) {
                <div class="msec"><div class="mlbl">{{ 'validity_notes_sec' | t : 'Validity check notes' }}</div>
                  <div><span class="badge" [class]="d.isValid ? 'b-ok' : 'b-bad'">Status: {{ d.isValid ? 'Valid' : 'Invalid' }}</span> {{ d.validityNotes ?? '' }}</div></div>
              }
              @if (d.investigationNotes) { <div class="msec"><div class="mlbl">{{ 'investigation_sec' | t : 'Investigation notes & root cause analysis' }}</div><div>{{ d.investigationNotes }}</div></div> }
              @if (d.outcomeType) {
                <div class="msec"><div class="mlbl">{{ 'outcome_sec' | t : 'Outcome details' }}</div>
                  <div><b>{{ d.outcomeType }}</b>@if (d.outcomeSummary) { <span> — {{ d.outcomeSummary }}</span> }</div></div>
              }
              @if (d.resolutionSummary) { <div class="msec"><div class="mlbl">{{ 'resolution_sec' | t : 'Resolution summary' }}</div><div>{{ d.resolutionSummary }}</div></div> }

              <!-- Stage action forms -->
              @if (auth.has('UpdateComplaints') && d.status !== 'Resolved') {
                <div class="stagebox">
                  @switch (stageForm(d)) {
                    @case ('ack') {
                      <b>{{ 'ack_title' | t : 'Acknowledge Complaint' }}</b>
                      <div class="small muted" style="margin:4px 0 8px">{{ 'ack_hint' | t : 'Click the button below to acknowledge receipt of the complaint and begin the investigation.' }}</div>
                      <button class="btn btn-p" [disabled]="busy()" (click)="acknowledge(d)">{{ 'acknowledge' | t : 'Acknowledge' }}</button>
                    }
                    @case ('validity') {
                      <b>{{ 'validity_title' | t : 'Complaint Validity Check' }}</b>
                      <div class="field" style="margin-top:8px"><label>{{ 'validity_status' | t : 'Validity Status' }}</label>
                        <select class="select" [(ngModel)]="stageValid"><option [ngValue]="true">{{ 'valid' | t : 'Valid' }}</option><option [ngValue]="false">{{ 'invalid' | t : 'Invalid' }}</option></select></div>
                      <div class="field" style="margin-top:8px"><label>{{ 'validity_notes' | t : 'Validity Notes' }}</label>
                        <textarea class="input" rows="2" [(ngModel)]="stageNotes" placeholder="Enter validity check notes..."></textarea></div>
                      <button class="btn btn-p" style="margin-top:8px" [disabled]="busy()" (click)="checkValidity(d)">{{ 'save_proceed' | t : 'Save & Proceed' }}</button>
                    }
                    @case ('investigation') {
                      <b>{{ 'investigation_title' | t : 'Investigation & Root Cause Analysis' }}</b>
                      <div class="field" style="margin-top:8px"><label>{{ 'investigation_lbl' | t : 'Investigation Notes & Root Cause' }}</label>
                        <textarea class="input" rows="3" [(ngModel)]="stageNotes" placeholder="Enter investigation details and root cause analysis..."></textarea></div>
                      <button class="btn btn-p" style="margin-top:8px" [disabled]="!stageNotes.trim() || busy()" (click)="recordInvestigation(d)">{{ 'save_proceed' | t : 'Save & Proceed' }}</button>
                    }
                    @case ('outcome') {
                      <b>{{ 'outcome_title' | t : 'Business Outcome' }}</b>
                      <div class="field" style="margin-top:8px"><label>{{ 'outcome_type_lbl' | t : 'Outcome Type' }}</label>
                        <select class="select" [(ngModel)]="stageOutcomeType">@for (o of outcomeTypes; track o) { <option>{{ o }}</option> }</select></div>
                      <div class="field" style="margin-top:8px"><label>{{ 'outcome_details_lbl' | t : 'Outcome Details' }}</label>
                        <textarea class="input" rows="2" [(ngModel)]="stageNotes" placeholder="Enter summary of business action taken..."></textarea></div>
                      <button class="btn btn-p" style="margin-top:8px" [disabled]="busy()" (click)="recordOutcome(d)">{{ 'save_proceed' | t : 'Save & Proceed' }}</button>
                    }
                    @case ('resolve') {
                      <b>{{ 'resolve_title' | t : 'Resolution & Closure' }}</b>
                      <div class="field" style="margin-top:8px"><label>{{ 'resolution_notes_lbl' | t : 'Resolution Notes' }}</label>
                        <textarea class="input" rows="2" [(ngModel)]="stageNotes" placeholder="Enter final resolution details and preventive actions..."></textarea></div>
                      <button class="btn btn-t" style="margin-top:8px" [disabled]="busy()" (click)="resolve(d)">{{ 'resolve_close_btn' | t : 'Resolve & Close' }}</button>
                    }
                  }
                </div>
              }
              @if (d.status === 'Resolved') {
                <div class="stagebox resolvedbox">
                  <b>{{ 'resolved_banner' | t : 'Complaint Resolved & Closed' }}</b>
                  <div class="small muted" style="margin:4px 0 8px">{{ 'resolved_banner_sub' | t : 'All investigation and outcome stages have been completed.' }}</div>
                  @if (auth.has('UpdateComplaints')) { <button class="btn btn-s" [disabled]="busy()" (click)="reopen(d.id)">{{ 'reopen_btn' | t : 'Reopen' }}</button> }
                </div>
              }

              <div style="margin-top:14px"><b>{{ 'signatures' | t : 'Signatures' }}</b><app-esign-panel module="complaint" [recordId]="d.id" /></div>
            </div>
          }
          @if (detailTab() === 'audit') {
            <div style="padding:14px 16px">
              @if (audit(); as rows) {
                <table class="grid-table"><thead><tr><th>{{ 'time' | t : 'Time' }}</th><th>{{ 'user' | t : 'User' }}</th><th>{{ 'event' | t : 'Event' }}</th><th>{{ 'details' | t : 'Details' }}</th></tr></thead>
                  <tbody>@for (a of rows; track $index) { <tr><td class="mono small">{{ a.occurredAt | date:'dd/MM/yyyy HH:mm' }}</td><td>{{ a.actor }}</td><td>{{ a.action }}</td><td class="small muted" style="white-space:pre-line;max-width:320px">{{ auditDetails(a) }}</td></tr> } @empty { <tr><td colspan="4" class="muted small">—</td></tr> }</tbody>
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
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .dlg{background:var(--white);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25)}
    .dlg-head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-200)}
    .dlg-head h2{font-size:15px;margin:0;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
    .stepper{display:flex;align-items:center;padding:16px;gap:4px;background:var(--filter-bg);border-radius:10px;margin:14px 16px 0}
    .step{display:flex;flex-direction:column;align-items:center;gap:4px;min-width:64px}
    .step .dot{width:26px;height:26px;border-radius:50%;background:var(--white);border:2px solid var(--slate-200);color:var(--slate-500);display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:700}
    .step.done .dot{background:#e8f6ee;border-color:var(--teal-500);color:var(--teal-600, #0d7490)}
    .step.cur .dot{background:var(--teal-500);border-color:var(--teal-500);color:#fff}
    .step .slbl{font-size:11px;color:var(--slate-500)}
    .step.done .slbl{color:var(--teal-600, #0d7490)}
    .step.cur .slbl{color:var(--slate-900);font-weight:700}
    .sarrow{flex:1;text-align:center;color:var(--slate-300, #cbd5e1);margin-bottom:16px}
    .sarrow.done{color:var(--teal-500)}
    .metacards{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px;margin-bottom:12px}
    .mcard{border:1px solid var(--slate-200);border-radius:10px;padding:10px 12px;background:var(--white)}
    .mlbl{font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:var(--slate-500);font-weight:700;margin-bottom:4px}
    .mval{font-size:14px;color:var(--slate-900);font-weight:700}
    .msec{border:1px solid var(--slate-200);border-radius:10px;padding:10px 12px;margin-bottom:10px;background:var(--white)}
    .msec > div:last-child{font-size:13px;color:var(--slate-900)}
    .stagebox{margin-top:14px;padding:12px;border:1px solid var(--slate-200);border-radius:8px;background:var(--filter-bg)}
    .resolvedbox{border-color:var(--teal-500);background:#e8f6ee}
  `],
})
export class ComplaintsComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly result = signal<PagedResult<ComplaintListItem> | null>(null);
  readonly counts = signal<ComplaintCounts | null>(null); // CMP-16: server-side pill counts
  readonly labs = signal<LabListItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  readonly teams = signal<RefItem[]>([]);
  readonly showLog = signal(false);
  readonly detail = signal<ComplaintDetail | null>(null);
  readonly detailTab = signal<'meta' | 'audit'>('meta');
  // CMP-17: move focus into whichever dialog opens, and let Escape close it (keyboard/screen-reader operable).
  @ViewChild('dlg') set dlg(el: ElementRef<HTMLElement> | undefined) { el?.nativeElement.focus(); }
  @HostListener('document:keydown.escape') onEscape(): void {
    if (this.detail()) this.closeDetail();
    else if (this.showLog()) this.showLog.set(false);
  }
  readonly audit = signal<ComplaintAuditRow[] | null>(null);
  readonly status = signal('All');
  readonly category = signal('');
  readonly categories = CATEGORIES; readonly channels = CHANNELS; readonly statuses = STATUSES;
  readonly outcomeTypes = OUTCOME_TYPES; readonly steps = STEPS;
  stageValid = true;
  stageNotes = '';
  stageOutcomeType = OUTCOME_TYPES[0];

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    category: this.fb.control(CATEGORIES[0], Validators.required),
    representativeId: this.fb.control(''),
    receivedAt: this.fb.control(''),
    viaChannel: this.fb.control('Phone Call', Validators.required), // reference default
    assignedTeam: this.fb.control(''),
    details: this.fb.control('', Validators.required),
  });

  constructor() { this.load(); }

  // CMP-16: counts come from the backend (whole in-scope set), so they are right past 100 rows and under filters.
  statusCount(s: string): number {
    const c = this.counts();
    if (!c) return 0;
    return s === 'Open' ? c.open : s === 'InProgress' ? c.inProgress : s === 'Resolved' ? c.resolved : c.total;
  }
  statusLabel(s: string): string { return s === 'All' ? 'All' : s === 'InProgress' ? 'In Progress' : s; }
  badge(s: string): string { return s === 'Resolved' ? 'b-ok' : s === 'InProgress' ? 'b-warn' : 'b-bad'; }
  stageLabel(stage: string): string { return STEPS.find((s) => s.stage === stage)?.label ?? stage; }
  stepIndex(d: ComplaintDetail): number {
    if (d.status === 'Resolved') return STEPS.length; // all steps checked, like the reference
    if (d.stage === 'RejectedInvalid') return 2;
    const i = STEPS.findIndex((s) => s.stage === d.stage);
    return i < 0 ? 0 : i;
  }
  /** Which stage-payload form is due next (RejectedInvalid still resolves & closes, per the reference). */
  stageForm(d: ComplaintDetail): StageForm {
    switch (d.stage) {
      case 'Logged': return 'ack';
      case 'Acknowledged': return 'validity';
      case 'ValidityChecked': return 'investigation';
      case 'Investigation': return 'outcome';
      default: return 'resolve'; // BusinessOutcome, RejectedInvalid, Resolution
    }
  }
  auditDetails(a: ComplaintAuditRow): string {
    if (!a.after) return '';
    try {
      const obj = JSON.parse(a.after) as Record<string, unknown>;
      return Object.entries(obj)
        .filter(([, v]) => v !== null && v !== undefined && typeof v !== 'object' && String(v) !== '')
        .map(([k, v]) => `${k}: ${v}`).join('\n');
    } catch { return a.after; }
  }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 100 };
    if (this.status() !== 'All') params['status'] = this.status();
    if (this.category()) params['category'] = this.category();
    this.api.get<PagedResult<ComplaintListItem>>('/complaints', params).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
    // CMP-16: pill counts are computed server-side over the whole in-scope set (status-independent).
    const countParams: Record<string, string | number> = {};
    if (this.category()) countParams['category'] = this.category();
    this.api.get<ComplaintCounts>('/complaints/counts', countParams).subscribe({ next: (c) => this.counts.set(c) });
  }
  setStatus(s: string): void { this.status.set(s); this.load(); }
  setCategory(c: string): void { this.category.set(c); this.load(); }

  // ---- Log popup ----
  private static nowLocal(): string {
    const d = new Date();
    const p = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
  }

  openLog(): void {
    this.showLog.set(true);
    // The reference prefills the complaint date/time with "now".
    this.form.controls.receivedAt.setValue(ComplaintsComponent.nowLocal());
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
    this.busy.set(true);
    const v = this.form.getRawValue();
    this.api.post('/complaints', {
      laboratoryId: v.laboratoryId, category: v.category, viaChannel: v.viaChannel,
      assignedTeam: v.assignedTeam || null, details: v.details,
      representativeId: v.representativeId || null,
      receivedAt: v.receivedAt ? new Date(v.receivedAt).toISOString() : null,
    }).subscribe({
      next: () => { this.busy.set(false); this.showLog.set(false); this.form.reset({ category: CATEGORIES[0], viaChannel: 'Phone Call', assignedTeam: this.teams()[0]?.nameEn ?? '' }); this.load(); },
      error: () => { this.busy.set(false); },
    });
  }

  // ---- Details popup ----
  openDetail(id: string): void {
    this.detailTab.set('meta'); this.audit.set(null);
    this.resetStageInputs();
    this.api.get<ComplaintDetail>(`/complaints/${id}`).subscribe({ next: (d) => this.detail.set(d) });
  }
  closeDetail(): void { this.detail.set(null); }
  refreshDetail(id: string): void {
    this.resetStageInputs();
    this.audit.set(null); // stale after a stage change; reloads on next tab open
    this.api.get<ComplaintDetail>(`/complaints/${id}`).subscribe({ next: (d) => this.detail.set(d) });
    this.load();
  }
  private resetStageInputs(): void { this.stageValid = true; this.stageNotes = ''; this.stageOutcomeType = OUTCOME_TYPES[0]; }
  loadAudit(id: string): void {
    this.detailTab.set('audit');
    if (!this.audit()) this.api.get<ComplaintAuditRow[]>(`/complaints/${id}/audit`).subscribe({ next: (rows) => this.audit.set(rows) });
  }

  // ---- Workflow actions ----
  private act(id: string, obs: { subscribe: Function }): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({
      next: () => { this.busy.set(false); this.refreshDetail(id); },
      error: () => { this.busy.set(false); },
    });
  }
  investigate(c: ComplaintListItem): void {
    // Open rows start handling (Open -> InProgress) first; in-progress rows go straight to the workflow popup.
    if (c.status !== 'Open') { this.openDetail(c.id); return; }
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
    const notes = this.stageNotes.trim();
    // Invalid complaints close with an "Invalid Complaint" resolution, mirroring the reference.
    const summary = d.stage === 'RejectedInvalid' ? `Invalid Complaint${notes ? ': ' + notes : ''}` : (notes || null);
    this.act(d.id, this.api.post(`/complaints/${d.id}/resolve`, { resolutionSummary: summary }));
  }
  reopen(id: string): void {
    this.busy.set(true);
    this.api.post(`/complaints/${id}/reopen`).subscribe({
      next: () => { this.busy.set(false); if (this.detail()) this.refreshDetail(id); else this.load(); },
      error: () => { this.busy.set(false); },
    });
  }
}
