import { Component, computed, inject, signal, WritableSignal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { AttachmentRef, BoardItem, LabListItem, PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportXlsx, printTable, localToday, localTime, localDateTime, ddmy } from '../../shared/export.util';
import { AppDatePipe } from '../../shared/app-date.pipe';
import { ToastService } from '../../core/toast.service';
import { AttachmentService } from '../../core/attachment.service';
import { RecordVisitContext, RecordVisitDialogComponent } from '../../shared/record-visit-dialog.component';

const STATUSES = ['All', 'Pending', 'Visited', 'Missed'];

@Component({
  selector: 'app-daily',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, AppDatePipe, DateInputComponent, FilterSelectComponent, RecordVisitDialogComponent],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'daily' | t }}</div><h1>{{ 'daily_followup_board' | t : 'Daily Follow-up Board' }}</h1></div>
      <div style="display:flex;gap:8px">
        @if (auth.has('AddDailyFollowup')) {
          <button class="btn btn-p" (click)="openManual()">{{ 'record_manual_visit' | t : 'Record manual visit' }}</button>
        }
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
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="start"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="end"></app-date-input></div>
        <div class="field"><label>{{ 'branch_2' | t }}</label>
          <app-filter-select [options]="opts('branch')" [(ngModel)]="branch" [allValue]="'All'" [placeholder]="'all_2' | t"></app-filter-select></div>
        <div class="field"><label>{{ 'governorate_2' | t }}</label>
          <app-filter-select [multiple]="true" [options]="opts('governorate')" [(ngModel)]="gov" [placeholder]="'all_2' | t"></app-filter-select></div>
      </div>
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
        <div class="field"><label>{{ 'city_2' | t }}</label>
          <app-filter-select [multiple]="true" [options]="opts('city')" [(ngModel)]="city" [placeholder]="'all_2' | t"></app-filter-select></div>
        <div class="field"><label>{{ 'area_2' | t }}</label>
          <app-filter-select [multiple]="true" [options]="opts('area')" [(ngModel)]="area" [placeholder]="'all_2' | t"></app-filter-select></div>
        <div class="field"><label>{{ 'collector_rep' | t }}</label>
          <app-filter-select [options]="repOptions()" [(ngModel)]="rep" (ngModelChange)="load()" [allValue]="'All'" [placeholder]="'all_2' | t"></app-filter-select></div>
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
            <th>{{ 'status' | t }}</th><th>{{ 'samples' | t }}</th><th>{{ 'marked_at' | t : 'Marked At' }}</th><th>Verified</th><th></th></tr>
          @for (v of paged(); track v.visitId) {
            <tr>
              <td class="mono">{{ v.visitDate | appDate }}<div class="small muted">{{ v.scheduledTime }}</div></td>
              <td><b style="color:var(--slate-900)">{{ v.lab }}</b><div class="small muted">{{ sub(v) }}</div>
                @if (v.attachments?.length) {
                  <div class="docs">@for (a of v.attachments; track a.id) {
                    <a class="doc-link" (click)="viewDoc(a.id)" [title]="a.fileName">📎 {{ a.fileName }}</a>
                  }</div>
                }
              </td>
              <td>{{ v.rep ?? '—' }}</td>
              <td><span class="badge" [class]="badgeClass(v.status)">{{ statusLabel(v.status) }}</span>@if (v.transferDone) { <span class="badge b-info">{{ 'transferred' | t : 'Transferred' }}</span> }</td>
              <td class="mono">{{ v.samples ?? '—' }}</td>
              <td class="mono small">{{ marked(v) }}</td>
              <td>{{ v.adminChecked ? '✓' : '—' }}</td>
              <td class="actions">
                @if (v.archived) {
                  <span class="badge b-neu" title="{{ 'archived_readonly' | t : 'Archived — read-only history' }}">{{ 'history' | t : 'History' }}</span>
                } @else {
                  @if (v.status === 'Pending') {
                    <button class="btn btn-mini btn-p" (click)="openRecord(v)" [disabled]="busy()">{{ 'record_visit' | t : 'Record visit' }}</button>
                    <button class="btn btn-mini btn-s" (click)="miss(v)" [disabled]="busy()">{{ 'miss' | t : 'missed' }}</button>
                  }
                  @if ((v.status === 'Visited' || v.status === 'Received') && !v.adminChecked && auth.has('VerifyDailyFollowup')) {
                    <button class="btn btn-mini" (click)="verify(v)" [disabled]="busy()">{{ 'verify' | t : 'Verify' }}</button>
                  }
                }
              </td>
            </tr>
          } @empty { <tr><td colspan="8" class="empty">{{ 'no_visits_today' | t }}</td></tr> }
        </table>
        @if (filtered().length) {
          <div class="fu-pager">
            <button class="btn-ghost" [disabled]="curPage() <= 1" (click)="page.set(curPage() - 1)">‹ {{ 'prev' | t : 'Prev' }}</button>
            <span>{{ 'page' | t : 'Page' }} {{ curPage() }} / {{ pageCount() }} · {{ filtered().length }} {{ 'visits_lc' | t : 'visits' }}</span>
            <button class="btn-ghost" [disabled]="curPage() >= pageCount()" (click)="page.set(curPage() + 1)">{{ 'next' | t : 'Next' }} ›</button>
            <select class="select" [ngModel]="pageSize()" (ngModelChange)="pageSize.set(+$event); page.set(1)" style="max-width:90px;margin-inline-start:auto">
              <option [ngValue]="25">25</option><option [ngValue]="50">50</option><option [ngValue]="100">100</option>
            </select>
          </div>
        }
      }
    </div>

    <!-- Record-visit popup (SRS FR-5): the shared dialog — identical fields on the daily board and the dashboard. -->
    @if (recording(); as v) {
      <app-record-visit-dialog [ctx]="recordCtx(v)" [reps]="reps()" source="daily" (saved)="onRecorded()" (closed)="closeRecord()" />
    }

    <!-- Manual record: pick any lab (any status) and record a Collected visit for today. -->
    @if (manualOpen()) {
      <div class="overlay" (click)="manualOpen.set(false)">
        <div class="dlg" (click)="$event.stopPropagation()">
          <h3 style="margin:0 0 12px">{{ 'record_manual_visit' | t : 'Record manual visit' }}</h3>
          <div class="field">
            <label>{{ 'laboratory' | t }} *</label>
            @if (manualLab) {
              <div class="att-row"><span><b>{{ manualLab.name }}</b> · {{ manualLab.displayCode }} <span class="badge b-neu">{{ manualLab.status }}</span></span>
                <button type="button" class="btn btn-mini btn-s" (click)="manualLab = null">{{ 'change' | t : 'Change' }}</button></div>
            } @else {
              <div style="display:flex;gap:6px">
                <input class="input" [(ngModel)]="manualLabSearch" (keydown.enter)="searchLabs(); $event.preventDefault()" [placeholder]="'search_lab_name_or_code' | t" style="flex:1">
                <button class="btn btn-s" (click)="searchLabs()">{{ 'search' | t : 'Search' }}</button>
              </div>
              @if (manualLabResults().length) {
                <div class="lab-results">
                  @for (l of manualLabResults(); track l.id) {
                    <div class="lab-opt" (click)="pickManualLab(l)"><b>{{ l.name }}</b> · {{ l.displayCode }} <span class="badge b-neu">{{ l.status }}</span></div>
                  }
                </div>
              } @else if (manualSearched()) { <div class="small muted" style="margin-top:4px">{{ 'no_labs_found' | t : 'No labs found.' }}</div> }
            }
          </div>
          <div class="field" style="margin-top:10px">
            <label>{{ 'collector_rep' | t : 'Collector Rep' }}</label>
            <select class="select" [(ngModel)]="manualRep" style="width:100%">
              <option value="">—</option>
              @for (r of manualCollectorReps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }
            </select>
          </div>
          <div class="field" style="margin-top:10px">
            <label>{{ 'samples' | t : 'Samples collected' }} *</label>
            <input type="number" min="0" class="input" [(ngModel)]="manualCount" style="width:100%">
          </div>
          <div class="grid2" style="margin-top:10px">
            <div class="field"><label>Total Required</label><input type="number" min="0" class="input" [(ngModel)]="manualTotalRequired"></div>
            <div class="field"><label>No of Requests</label><input type="number" min="0" class="input" [(ngModel)]="manualRequests"></div>
          </div>
          <div class="field" style="margin-top:10px"><label>No of Outsource Samples</label><input type="number" min="0" class="input" [(ngModel)]="manualOutsource" style="width:100%"></div>
          <div class="field" style="margin-top:10px"><label>Notes (optional)</label><textarea class="input" rows="2" [(ngModel)]="manualNotes" style="width:100%"></textarea></div>
          <div class="field" style="margin-top:10px">
            <label>{{ 'documents_optional' | t : 'Documents (optional)' }}</label>
            <input type="file" multiple accept=".pdf,image/png,image/jpeg" (change)="onAttach($event, manualAtt)">
            <div class="small muted" style="margin-top:2px">PDF, JPG or PNG · up to 10 MB each</div>
            @for (a of manualAtt(); track a.id) {
              <div class="att-row"><span>📎 {{ a.fileName }}</span>
                <button type="button" class="btn btn-mini btn-s" (click)="removeAtt(a.id, manualAtt)">✕</button></div>
            }
            @if (uploading()) { <div class="small muted">{{ 'uploading' | t : 'Uploading…' }}</div> }
          </div>
          <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:16px">
            <button class="btn btn-s" (click)="manualOpen.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy() || uploading()" (click)="confirmManual()">{{ 'confirm' | t : 'Confirm visit' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .actions{display:flex;gap:6px;align-items:center}.num{width:66px}
    .docs{margin-top:3px;display:flex;flex-direction:column;gap:2px}
    .doc-link{font-size:11px;color:var(--brand,#0078d4);cursor:pointer;text-decoration:none;max-width:220px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
    .doc-link:hover{text-decoration:underline}
    .att-row{display:flex;justify-content:space-between;align-items:center;gap:8px;font-size:12px;margin-top:6px}
    .lab-results{border:1px solid var(--slate-150,#edebe9);border-radius:8px;margin-top:6px;max-height:180px;overflow:auto}
    .lab-opt{padding:8px 10px;cursor:pointer;font-size:12.5px;border-bottom:1px solid var(--slate-100,#f3f2f1)}
    .lab-opt:hover{background:var(--slate-50,#faf9f8)}
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .dlg{background:var(--white);border-radius:12px;padding:22px;width:min(92vw,420px);box-shadow:0 16px 48px rgba(0,0,0,.25);max-height:90vh;overflow-y:auto}
    .grid2{display:grid;grid-template-columns:1fr 1fr;gap:10px}
    .field label{display:block;font:600 11px var(--ui);color:var(--slate-600);margin-bottom:4px}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150,#edebe9);font-size:12.5px;color:var(--slate-700,#605e5c)}
  `],
})
export class DailyComponent {
  private readonly api = inject(ApiService);
  private readonly icons = inject(IconsService);
  private readonly toast = inject(ToastService);
  private readonly attach = inject(AttachmentService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<BoardItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  readonly status = signal('All');
  readonly statuses = STATUSES;
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly recording = signal<BoardItem | null>(null);
  readonly uploading = signal(false);

  // Manual-record dialog state
  readonly manualOpen = signal(false);
  readonly manualSearched = signal(false);
  readonly manualLabResults = signal<LabListItem[]>([]);
  manualLab: LabListItem | null = null;
  manualLabSearch = '';
  manualCount: number | null = null;
  manualRep = '';
  manualTotalRequired: number | null = null;
  manualRequests: number | null = null;
  manualOutsource: number | null = null;
  manualNotes = '';
  readonly manualAtt = signal<AttachmentRef[]>([]);

  readonly collectorReps = computed(() => this.reps().filter((r) => r.type === 'Collector' || r.type === 'Scanning'));
  /** Manual-visit collector picker: only the picked lab's assigned collectors; a lab with none falls back to all. */
  manualCollectorReps(): RepListItem[] {
    const all = this.collectorReps();
    const ids = new Set(this.manualLab?.collectorRepIds ?? []);
    if (ids.size === 0) return all;
    const assigned = all.filter((r) => ids.has(r.id));
    return assigned.length ? assigned : all;
  }

  private readonly today = localToday();
  start = this.today; end = this.today;
  branch = 'All'; gov: string[] = []; city: string[] = []; area: string[] = []; rep = 'All'; query = '';

  readonly filtered = computed(() => {
    const q = this.query.trim().toLowerCase();
    return this.items().filter((i) =>
      (this.branch === 'All' || i.branch === this.branch) &&
      (!this.gov.length || this.gov.includes(i.governorate ?? '')) &&
      (!this.city.length || this.city.includes(i.city ?? '')) &&
      (!this.area.length || this.area.includes(i.area ?? '')) &&
      (!q || i.lab?.toLowerCase().includes(q) || i.labDisplayCode?.toLowerCase().includes(q)));
  });
  readonly repOptions = computed(() => this.reps().map((r) => ({ value: r.id, label: r.fullName })));

  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize())));
  readonly curPage = computed(() => Math.min(this.page(), this.pageCount()));
  readonly paged = computed(() => {
    const start = (this.curPage() - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
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
    this.loading.set(true); this.page.set(1);
    const params: Record<string, string> = { start: this.start, end: this.end, status: this.status() };
    if (this.rep !== 'All') params['rep'] = this.rep;
    this.api.get<BoardItem[]>('/daily', params).subscribe({
      next: (b) => { this.items.set(b); this.loading.set(false); this.icons.render(); },
      error: () => this.loading.set(false),
    });
  }
  setStatus(s: string): void { this.status.set(s); this.load(); }
  reset(): void { this.start = this.today; this.end = this.today; this.branch = this.rep = 'All'; this.gov = []; this.city = []; this.area = []; this.query = ''; this.status.set('All'); this.load(); }

  // ---- Record-visit popup (SRS FR-5) — rendered by the shared RecordVisitDialogComponent ----

  openRecord(v: BoardItem): void { this.recording.set(v); }
  closeRecord(): void { this.recording.set(null); }
  onRecorded(): void { this.recording.set(null); this.load(); }
  recordCtx(v: BoardItem): RecordVisitContext {
    return {
      visitId: v.visitId, lab: v.lab, labDisplayCode: v.labDisplayCode, scheduledTime: v.scheduledTime,
      area: v.area, repName: v.rep, collectorRepId: v.collectorRepId, collectorRepIds: v.collectorRepIds ?? [],
    };
  }

  // ---- Attachments (shared by the record + manual dialogs) ----
  viewDoc(id: string): void { this.attach.view(id); }

  onAttach(ev: Event, target: WritableSignal<AttachmentRef[]>): void {
    const input = ev.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    let pending = files.length;
    this.uploading.set(true);
    for (const file of files) {
      this.attach.upload(file).subscribe({
        next: (ref) => { target.update((a) => [...a, ref]); if (--pending === 0) this.uploading.set(false); },
        error: () => { if (--pending === 0) this.uploading.set(false); },
      });
    }
  }
  removeAtt(id: string, target: WritableSignal<AttachmentRef[]>): void {
    target.update((a) => a.filter((x) => x.id !== id));
  }

  // ---- Manual record ----
  openManual(): void {
    this.manualLab = null; this.manualLabSearch = ''; this.manualLabResults.set([]); this.manualSearched.set(false);
    this.manualCount = null; this.manualRep = ''; this.manualTotalRequired = null; this.manualRequests = null;
    this.manualOutsource = null; this.manualNotes = ''; this.manualAtt.set([]);
    this.manualOpen.set(true);
  }
  searchLabs(): void {
    const term = this.manualLabSearch.trim();
    this.api.get<PagedResult<LabListItem>>('/labs', { search: term, pageSize: 25 }).subscribe({
      next: (r) => { this.manualLabResults.set(r.items); this.manualSearched.set(true); },
    });
  }
  pickManualLab(l: LabListItem): void { this.manualLab = l; this.manualLabResults.set([]); this.manualRep = ''; /* the collector list is per-lab */ }
  confirmManual(): void {
    if (!this.manualLab) { this.toast.warning('Select a laboratory.'); return; }
    if (this.manualCount === null || this.manualCount < 0) { this.toast.warning('Enter a valid sample count.'); return; }
    this.busy.set(true);
    this.api.post('/daily/manual', {
      laboratoryId: this.manualLab.id,
      sampleCount: this.manualCount,
      collectorRepId: this.manualRep || null,
      totalRequired: this.manualTotalRequired,
      requestCount: this.manualRequests,
      outsourceCount: this.manualOutsource,
      notes: this.manualNotes.trim() || null,
      attachmentIds: this.manualAtt().map((a) => a.id),
    }).subscribe({
      next: () => { this.toast.success('Visit recorded.'); this.busy.set(false); this.manualOpen.set(false); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  miss(v: BoardItem): void {
    if (!window.confirm(`Mark the ${v.scheduledTime} visit to ${v.lab} as missed?`)) return;
    this.run(this.api.post(`/daily/${v.visitId}/miss?source=daily`), 'Visit marked as missed.');
  }
  verify(v: BoardItem): void { this.run(this.api.post(`/daily/${v.visitId}/verify?source=daily`, { verified: true }), 'Visit verified.'); }

  private run(obs: { subscribe: Function }, msg?: string): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({ next: () => { if (msg) this.toast.success(msg); this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }

  sub(v: BoardItem): string { return [v.labDisplayCode, v.branch, v.area, v.governorate].filter(Boolean).join(' · '); }
  marked(v: BoardItem): string { return localTime(v.markedAt); }
  statusLabel(s: string): string { return s === 'Visited' ? 'Collected' : s; }

  private exportRows(): (string | number | null)[][] {
    return this.filtered().map((v) => [ddmy(v.visitDate), v.scheduledTime, v.lab, v.labDisplayCode, v.branch, v.area, v.governorate,
      v.rep, this.statusLabel(v.status), v.samples, localDateTime(v.markedAt), v.adminChecked ? 'Yes' : 'No']);
  }
  exportExcel(): void {
    exportXlsx('daily-followup.xlsx',
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
