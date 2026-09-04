import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, OutsourceSample, PagedResult } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportXlsx, localToday, ddmy } from '../../shared/export.util';
import { AppDatePipe } from '../../shared/app-date.pipe';

const STATUSES = ['All', 'Collected', 'Sent', 'Received'];
const NEXT: Record<string, string> = { Collected: 'Sent', Sent: 'Received' };

@Component({
  selector: 'app-outsource',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, DecimalPipe, TranslatePipe, AppDatePipe, DateInputComponent],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'outsource_samples' | t }}</div><h1>{{ 'outsource_tracking_title' | t : 'Outsource Samples Tracking' }}</h1></div>
      <button class="btn btn-s" (click)="exportExcel()" [disabled]="!filtered().length">Export Excel</button>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_outsource' | t : 'Total outsource' }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">{{ 'outsource_tests' | t : 'outsource tests' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'collected_2' | t : 'Collected' }}</div><div class="val">{{ k().collected | number:'1.0-0' }}</div><div class="sub">{{ 'awaiting_dispatch' | t : 'awaiting dispatch' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'sent' | t : 'Sent' }}</div><div class="val">{{ k().sent | number:'1.0-0' }}</div><div class="sub">{{ 'in_transit_to_destination' | t : 'in transit' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'received' | t : 'Received' }}</div><div class="val">{{ k().received | number:'1.0-0' }}</div><div class="sub">{{ 'delivered_at_destination' | t : 'delivered' }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(3,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="start"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="end"></app-date-input></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:38px">{{ 'apply_filters_2' | t : 'Apply' }}</button></div>
      </div>
      <div style="display:flex;gap:4px;margin-top:12px;padding-top:12px;border-top:1px solid var(--slate-150)">
        @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="status.set(s)">{{ s === 'All' ? ('all' | t) : (s | t : s) }}</span> }
      </div>
    </div>

    @if (auth.has('OutsourceSamples')) {
      <div class="card" style="padding:20px;margin-bottom:20px">
        <h3 style="margin:0 0 12px;font-size:14px;font-weight:600;color:var(--slate-800)">{{ 'add_outsource_sample_manually' | t : 'Add outsource sample' }}</h3>
        <form class="frm-grid" [formGroup]="form" (ngSubmit)="add()" style="grid-template-columns:repeat(3,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'date' | t : 'Date' }}</label><app-date-input formControlName="visitDate"></app-date-input></div>
          <div class="field"><label>{{ 'laboratory' | t }}</label>
            <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
          <div class="field"><label>{{ 'quantity' | t : 'Samples Count' }}</label><input class="input" type="number" min="1" formControlName="quantity"></div>
          <div class="field"><label>{{ 'destination' | t : 'Destination Lab' }}</label><input class="input" formControlName="destinationLab"></div>
          <div class="field"><label>{{ 'status_3' | t : 'Status' }}</label>
            <select class="select" formControlName="status">@for (s of statuses.slice(1); track s) { <option [value]="s">{{ s | t : s }}</option> }</select></div>
          <div class="field"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" formControlName="notes"></div>
          <div class="field"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()" style="height:38px">{{ 'add_sample_btn' | t : 'Add Sample' }}</button></div>
        </form>
      </div>
    }

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'source_lab_col' | t : 'Source Lab' }}</th>
            <th>{{ 'quantity' | t : 'Samples' }}</th><th>{{ 'status_3' | t }}</th><th>{{ 'destination_lab' | t : 'Destination Lab' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th>{{ 'actions_4' | t : 'Actions' }}</th></tr></thead>
          <tbody>
            @for (o of filtered(); track o.id) {
              <tr>
                <td class="mono small">{{ o.visitDate | appDate }}</td>
                <td><b style="color:var(--slate-900)">{{ o.labName }}</b><div class="small muted">{{ o.labDisplayCode }}</div></td>
                <td>
                  @if (auth.has('OutsourceSamples')) { <input type="number" min="1" class="input" style="width:76px;padding:4px 8px" [ngModel]="draft(o).quantity" (ngModelChange)="setD(o, 'quantity', $event)"> }
                  @else { <span class="mono" style="font-weight:700">{{ o.quantity }}</span> }
                </td>
                <td><span class="badge" [class]="badgeClass(o.status)">{{ o.status | t : o.status }}</span></td>
                <td>
                  @if (auth.has('OutsourceSamples')) { <input class="input" style="min-width:130px;padding:4px 8px" [ngModel]="draft(o).destinationLab" (ngModelChange)="setD(o, 'destinationLab', $event)"> }
                  @else { {{ o.destinationLab ?? '—' }} }
                </td>
                <td>
                  @if (auth.has('OutsourceSamples')) { <input class="input" style="min-width:130px;padding:4px 8px" [ngModel]="draft(o).notes" (ngModelChange)="setD(o, 'notes', $event)"> }
                  @else { <span class="small">{{ o.notes ?? '—' }}</span> }
                </td>
                <td class="actions">
                  @if (auth.has('OutsourceSamples')) {
                    <button class="btn btn-mini btn-p" (click)="saveRow(o)" [disabled]="busy() || !draft(o).dirty">{{ 'save' | t : 'Save' }}</button>
                    @if (next(o.status); as nx) { <button class="btn btn-mini" (click)="advance(o)" [disabled]="busy()">→ {{ nx | t : nx }}</button> }
                    <button class="btn btn-mini btn-d" (click)="remove(o)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}`],
})
export class OutsourceComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<OutsourceSample[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly status = signal('All');
  readonly statuses = STATUSES;

  private readonly today = localToday();
  start = this.today; end = this.today;

  readonly form = this.fb.group({
    visitDate: this.fb.control(localToday(), Validators.required),
    laboratoryId: this.fb.control('', Validators.required),
    destinationLab: this.fb.control('', Validators.required),
    quantity: this.fb.control(1, [Validators.required, Validators.min(1)]),
    status: this.fb.control('Collected'),
    notes: this.fb.control(''),
  });

  readonly filtered = computed(() => this.items().filter((s) => this.status() === 'All' || s.status === this.status()));
  readonly k = computed(() => {
    const f = this.filtered();
    const sum = (st: string) => f.filter((s) => s.status === st).reduce((a, s) => a + s.quantity, 0);
    return { total: f.reduce((a, s) => a + s.quantity, 0), collected: sum('Collected'), sent: sum('Sent'), received: sum('Received') };
  });

  constructor() {
    this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
    this.load();
  }

  next(status: string): string | undefined { return NEXT[status]; }
  badgeClass(s: string): string { return s === 'Received' ? 'b-ok' : s === 'Sent' ? 'b-warn' : 'b-info'; }

  // ---- Inline row drafts (reference parity: samples/destination/notes editable in the grid) ----

  private readonly drafts = new Map<string, { quantity: number; destinationLab: string; notes: string; dirty: boolean }>();
  draft(o: OutsourceSample): { quantity: number; destinationLab: string; notes: string; dirty: boolean } {
    let d = this.drafts.get(o.id);
    if (!d) { d = { quantity: o.quantity, destinationLab: o.destinationLab ?? '', notes: o.notes ?? '', dirty: false }; this.drafts.set(o.id, d); }
    return d;
  }
  setD(o: OutsourceSample, key: 'quantity' | 'destinationLab' | 'notes', value: string | number): void {
    const d = this.draft(o);
    if (key === 'quantity') d.quantity = Number(value) || 0;
    else d[key] = String(value ?? '');
    d.dirty = true;
  }
  saveRow(o: OutsourceSample): void {
    const d = this.draft(o);
    this.busy.set(true);
    this.api.put(`/outsource-samples/${o.id}`, {
      quantity: d.quantity, destinationLab: d.destinationLab.trim() || null, notes: d.notes.trim() || null,
    }).subscribe({
      next: () => { this.busy.set(false); this.drafts.delete(o.id); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.get<OutsourceSample[]>('/outsource-samples', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  add(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    // CreateOutsourceSampleCommand does not accept a status yet — new samples always start as Collected on the server, so the select stays bound but its value is not sent.
    const { status: _status, ...v } = this.form.getRawValue();
    this.api.post('/outsource-samples', { ...v, notes: v.notes || null }).subscribe({
      next: () => { this.busy.set(false); this.form.patchValue({ laboratoryId: '', destinationLab: '', quantity: 1, status: 'Collected', notes: '' }); this.load(); }, error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    exportXlsx('outsource-samples.xlsx',
      ['Date', 'Source lab', 'Code', 'Samples', 'Status', 'Destination lab', 'Notes'],
      this.filtered().map((o) => [ddmy(o.visitDate), o.labName, o.labDisplayCode, o.quantity, o.status, o.destinationLab, o.notes]));
  }
  advance(o: OutsourceSample): void {
    const status = this.next(o.status); if (!status) return;
    this.busy.set(true);
    this.api.post(`/outsource-samples/${o.id}/status`, { status }).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(o: OutsourceSample): void {
    if (!window.confirm('Delete this outsource sample?')) return;
    this.busy.set(true);
    this.api.delete(`/outsource-samples/${o.id}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
}
