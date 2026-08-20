import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, PagedResult } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface OutsourceSample {
  id: string; laboratoryId: string; labDisplayCode: string; labName: string;
  visitDate: string; destinationLab: string; quantity: number; status: string;
}
const STATUSES = ['All', 'Collected', 'Sent', 'Received'];
const NEXT: Record<string, string> = { Collected: 'Sent', Sent: 'Received' };

@Component({
  selector: 'app-outsource',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'outsource_samples' | t }}</div><h1>{{ 'outsource_samples' | t }}</h1></div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_outsource' | t : 'Total outsource' }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">{{ 'outsource_tests' | t : 'outsource tests' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'collected_2' | t : 'Collected' }}</div><div class="val">{{ k().collected | number:'1.0-0' }}</div><div class="sub">{{ 'awaiting_dispatch' | t : 'awaiting dispatch' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'sent' | t : 'Sent' }}</div><div class="val">{{ k().sent | number:'1.0-0' }}</div><div class="sub">{{ 'in_transit_to_destination' | t : 'in transit' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'received' | t : 'Received' }}</div><div class="val">{{ k().received | number:'1.0-0' }}</div><div class="sub">{{ 'delivered_at_destination' | t : 'delivered' }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(3,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="start"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="end"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:38px">{{ 'apply_filters_2' | t : 'Apply' }}</button></div>
      </div>
      <div style="display:flex;gap:4px;margin-top:12px;padding-top:12px;border-top:1px solid var(--slate-150)">
        @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="status.set(s)">{{ s === 'All' ? ('all' | t) : (s | t : s) }}</span> }
      </div>
    </div>

    @if (auth.has('OutsourceSamples')) {
      <div class="card" style="padding:20px;margin-bottom:20px">
        <h3 style="margin:0 0 12px;font-size:14px;font-weight:600;color:var(--slate-800)">{{ 'add_outsource_sample_manually' | t : 'Add outsource sample' }}</h3>
        <form class="frm-grid" [formGroup]="form" (ngSubmit)="add()" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'laboratory' | t }}</label>
            <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
          <div class="field"><label>{{ 'destination' | t : 'Destination' }}</label><input class="input" formControlName="destinationLab"></div>
          <div class="field"><label>{{ 'quantity' | t : 'Quantity' }}</label><input class="input" type="number" min="1" formControlName="quantity"></div>
          <div class="field"><button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()" style="height:38px">{{ 'add' | t : 'Add' }}</button></div>
        </form>
      </div>
    }

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'laboratory_2' | t }}</th><th>{{ 'destination' | t : 'Destination' }}</th><th>{{ 'date' | t }}</th>
            <th>{{ 'quantity' | t : 'Quantity' }}</th><th>{{ 'status_3' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (o of filtered(); track o.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ o.labName }}</b><div class="small muted">{{ o.labDisplayCode }}</div></td>
                <td>{{ o.destinationLab }}</td><td class="mono small">{{ o.visitDate }}</td>
                <td class="mono" style="font-weight:700">{{ o.quantity }}</td>
                <td><span class="badge" [class]="badgeClass(o.status)">{{ o.status | t : o.status }}</span></td>
                <td class="actions">
                  @if (auth.has('OutsourceSamples')) {
                    @if (next(o.status); as nx) { <button class="btn btn-mini btn-p" (click)="advance(o)" [disabled]="busy()">→ {{ nx | t : nx }}</button> }
                    <button class="btn btn-mini btn-d" (click)="remove(o)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No records.' }}</td></tr> }
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

  private readonly today = new Date().toISOString().slice(0, 10);
  start = this.today; end = this.today;

  readonly form = this.fb.group({
    laboratoryId: this.fb.control('', Validators.required),
    destinationLab: this.fb.control('', Validators.required),
    quantity: this.fb.control(1, [Validators.required, Validators.min(1)]),
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

  load(): void {
    this.loading.set(true);
    this.api.get<OutsourceSample[]>('/outsource-samples', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  add(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.api.post('/outsource-samples', { ...this.form.getRawValue(), visitDate: this.start }).subscribe({
      next: () => { this.busy.set(false); this.form.patchValue({ laboratoryId: '', destinationLab: '', quantity: 1 }); this.load(); }, error: () => this.busy.set(false),
    });
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
