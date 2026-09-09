import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AttachmentService } from '../core/attachment.service';
import { ToastService } from '../core/toast.service';
import { TranslatePipe } from '../core/i18n';
import { AttachmentRef, RepListItem } from '../core/models';

/** The visit being recorded — a normalised view so the daily board and the dashboard feed the same dialog. */
export interface RecordVisitContext {
  visitId: string;
  lab: string;
  labDisplayCode: string | null;
  scheduledTime: string;
  area: string | null;
  repName: string | null;
  /** The visit's currently assigned collector (prefills the picker). */
  collectorRepId: string | null;
  /** The lab's assigned collectors — the picker offers only these (falls back to all when empty). */
  collectorRepIds: string[];
}

/**
 * The single "Record visit" dialog (SRS FR-5), shared by the daily follow-up page and the dashboard so the two can
 * never drift: same fields (collector, samples with the suggested count, totals, outsource, notes, documents), same
 * validation, same request. The collector picker is limited to the lab's assigned collectors; a lab with none
 * assigned falls back to every collector so recording is never blocked.
 */
@Component({
  selector: 'app-record-visit-dialog',
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="overlay" (click)="closed.emit()">
      <div class="dlg" (click)="$event.stopPropagation()">
        <h3 style="margin:0 0 4px">{{ 'record_visit' | t : 'Record visit' }}</h3>
        <div class="small muted" style="margin-bottom:14px">{{ ctx().lab }}@if (ctx().labDisplayCode) { · {{ ctx().labDisplayCode }} }</div>
        <div class="small muted" style="margin-bottom:12px">{{ 'scheduled_2' | t : 'Scheduled' }} {{ ctx().scheduledTime }} · {{ ctx().area ?? '—' }} · {{ ctx().repName ?? '—' }}</div>
        <div class="field">
          <label>{{ 'collector_rep' | t : 'Collector Rep' }}</label>
          <select class="select" [(ngModel)]="rep" style="width:100%">
            <option value="">—</option>
            @for (r of collectorOptions(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }
          </select>
          @if (usingFallback()) { <div class="small muted" style="margin-top:2px">{{ 'no_assigned_collectors' | t : 'This lab has no assigned collectors — showing all.' }}</div> }
        </div>
        <div class="field" style="margin-top:10px">
          <label>{{ 'samples' | t : 'Samples collected' }} *</label>
          <input type="number" min="0" class="input" [(ngModel)]="count" style="width:100%">
          @if (suggested() !== null) { <div class="small muted" style="margin-top:4px">{{ 'suggested' | t : 'Suggested' }}: {{ suggested() }} ({{ 'last_recorded_count' | t : 'last recorded count for this lab' }})</div> }
        </div>
        <div class="grid2" style="margin-top:10px">
          <div class="field"><label>{{ 'total_required' | t : 'Total Required' }}</label><input type="number" min="0" class="input" [(ngModel)]="totalRequired"></div>
          <div class="field"><label>{{ 'no_of_requests' | t : 'No of Requests' }}</label><input type="number" min="0" class="input" [(ngModel)]="requests"></div>
        </div>
        <div class="field" style="margin-top:10px">
          <label>{{ 'no_of_outsource_samples' | t : 'No of Outsource Samples' }}</label>
          <input type="number" min="0" class="input" [(ngModel)]="outsource" style="width:100%">
          <div class="small muted" style="margin-top:2px">{{ 'outsource_hint' | t : 'A value > 0 creates an outsource-sample row automatically.' }}</div>
        </div>
        <div class="field" style="margin-top:10px">
          <label>{{ 'notes_optional' | t : 'Notes (optional)' }}</label>
          <textarea class="input" rows="2" [(ngModel)]="notes" style="width:100%"></textarea>
        </div>
        <div class="field" style="margin-top:10px">
          <label>{{ 'documents_optional' | t : 'Documents (optional)' }}</label>
          <input type="file" multiple accept=".pdf,image/png,image/jpeg" (change)="onAttach($event)">
          <div class="small muted" style="margin-top:2px">PDF, JPG or PNG · up to 10 MB each</div>
          @for (a of attachments(); track a.id) {
            <div class="att-row"><span>📎 {{ a.fileName }}</span>
              <button type="button" class="btn btn-mini btn-s" (click)="removeAtt(a.id)">✕</button></div>
          }
          @if (uploading()) { <div class="small muted">{{ 'uploading' | t : 'Uploading…' }}</div> }
        </div>
        <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:16px">
          <button class="btn btn-s" (click)="closed.emit()">{{ 'cancel' | t : 'Cancel' }}</button>
          <button class="btn btn-p" [disabled]="busy() || uploading()" (click)="confirm()">{{ 'confirm' | t : 'Confirm visit' }}</button>
        </div>
      </div>
    </div>
  `,
})
export class RecordVisitDialogComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly attach = inject(AttachmentService);
  private readonly toast = inject(ToastService);

  readonly ctx = input.required<RecordVisitContext>();
  readonly reps = input<RepListItem[]>([]);
  /** Telemetry tag only — the API ignores it. */
  readonly source = input<'daily' | 'dashboard'>('daily');

  readonly saved = output<void>();
  readonly closed = output<void>();

  rep = '';
  count: number | null = null;
  totalRequired: number | null = null;
  requests: number | null = null;
  outsource: number | null = null;
  notes = '';
  readonly attachments = signal<AttachmentRef[]>([]);
  readonly suggested = signal<number | null>(null);
  readonly busy = signal(false);
  readonly uploading = signal(false);

  /** Every collector-capable rep (Collector / Scanning), the fallback pool. */
  private readonly allCollectors = computed(() => this.reps().filter((r) => r.type === 'Collector' || r.type === 'Scanning'));
  /** The lab's assigned collectors when it has any that are loaded; otherwise the full pool. */
  readonly collectorOptions = computed(() => {
    const assignedIds = new Set(this.ctx().collectorRepIds ?? []);
    if (assignedIds.size === 0) return this.allCollectors();
    const assigned = this.allCollectors().filter((r) => assignedIds.has(r.id));
    return assigned.length ? assigned : this.allCollectors();
  });
  readonly usingFallback = computed(() => (this.ctx().collectorRepIds ?? []).length === 0 && this.allCollectors().length > 0);

  ngOnInit(): void {
    const c = this.ctx();
    this.rep = c.collectorRepId ?? '';
    this.api.get<{ suggested: number | null }>(`/daily/${c.visitId}/suggested-count`).subscribe({
      next: (r) => { this.suggested.set(r.suggested); if (this.count === null && r.suggested !== null) this.count = r.suggested; },
    });
  }

  onAttach(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    let pending = files.length;
    this.uploading.set(true);
    for (const file of files) {
      this.attach.upload(file).subscribe({
        next: (ref) => { this.attachments.update((a) => [...a, ref]); if (--pending === 0) this.uploading.set(false); },
        error: () => { if (--pending === 0) this.uploading.set(false); },
      });
    }
  }
  removeAtt(id: string): void { this.attachments.update((a) => a.filter((x) => x.id !== id)); }

  confirm(): void {
    if (this.count === null || this.count < 0) { this.toast.warning('Enter a valid sample count.'); return; }
    this.busy.set(true);
    this.api.post(`/daily/${this.ctx().visitId}/checkin?source=${this.source()}`, {
      sampleCount: this.count,
      collectorRepId: this.rep || null,
      totalRequired: this.totalRequired,
      requestCount: this.requests,
      outsourceCount: this.outsource,
      notes: this.notes.trim() || null,
      attachmentIds: this.attachments().map((a) => a.id),
    }).subscribe({
      next: () => { this.toast.success('Follow-up recorded.'); this.busy.set(false); this.saved.emit(); },
      error: () => this.busy.set(false),
    });
  }
}
