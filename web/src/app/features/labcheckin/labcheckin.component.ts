import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ReceivingItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-labcheckin',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'labcheckin' | t }}</div><h1>{{ 'labcheckin' | t }}</h1></div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:24px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'transferred_samples' | t }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">{{ 'in_transit_or_received' | t : 'In transit or received' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'received_samples' | t : 'Received samples' }}</div><div class="val">{{ k().received | number:'1.0-0' }}</div><div class="sub">{{ 'confirmed_at_lab' | t : 'Confirmed at lab' }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'pending_receipt' | t : 'Pending receipt' }}</div><div class="val">{{ k().pending | number:'1.0-0' }}</div><div class="sub">{{ 'awaiting_confirmation' | t }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'receipt_rate' | t : 'Receipt rate' }}</div><div class="val">{{ k().rate }}%</div><div class="sub">{{ 'overall_completion_rate' | t }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="start"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="end"></div>
        <div class="field"><label>{{ 'governorate_2' | t }}</label>
          <select class="select" [(ngModel)]="gov"><option value="All">{{ 'all_2' | t }}</option>@for (g of opts('governorate'); track g) { <option [value]="g">{{ g }}</option> }</select></div>
        <div class="field"><label>{{ 'area_2' | t }}</label>
          <select class="select" [(ngModel)]="area"><option value="All">{{ 'all_2' | t }}</option>@for (a of opts('area'); track a) { <option [value]="a">{{ a }}</option> }</select></div>
      </div>
      <div style="display:flex;gap:8px;margin-top:12px">
        <button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_dates' | t }}</button>
        <button class="btn btn-s" (click)="reset()" style="height:36px">{{ 'reset_filters' | t }}</button>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'laboratory_2' | t }}</th><th>{{ 'collection_date_and_time' | t }}</th><th>{{ 'transfer_rep' | t }}</th>
            <th>{{ 'samples' | t }}</th><th>{{ 'status_3' | t }}</th><th style="text-align:center">{{ 'confirm_receipt' | t : 'Confirm receipt' }}</th>
          </tr></thead>
          <tbody>
            @for (r of filtered(); track r.visitId) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ r.labName }}</b><div class="small muted">{{ r.labCode }}@if (r.area) { · {{ r.area }} }</div></td>
                <td class="mono small">{{ r.visitDate }} · {{ r.visitTime }}</td>
                <td>{{ r.transferRepName ?? '—' }}</td>
                <td class="mono" style="font-weight:700">{{ r.samples ?? 0 }}</td>
                <td><span class="badge" [class]="r.status === 'Received' ? 'b-ok' : 'b-warn'">{{ (r.status === 'Received' ? 'received' : 'transferred') | t : r.status }}</span></td>
                <td style="text-align:center">
                  @if (r.status !== 'Received' && auth.has('ConfirmTransfers')) {
                    <button class="btn btn-mini btn-p" [disabled]="busy()" (click)="confirm(r)">{{ 'confirm_receipt' | t : 'Confirm receipt' }}</button>
                  } @else if (r.status === 'Received') { <span style="color:var(--ok-ink)" [title]="r.receivedTime">✓</span> }
                </td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">{{ 'nothing_awaiting_receipt' | t : 'Nothing awaiting receipt.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
})
export class LabCheckInComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<ReceivingItem[]>([]);

  private readonly today = new Date().toISOString().slice(0, 10);
  start = this.today; end = this.today; gov = 'All'; area = 'All';

  readonly filtered = computed(() => this.items().filter((i) =>
    (this.gov === 'All' || i.governorate === this.gov) && (this.area === 'All' || i.area === this.area)));

  readonly k = computed(() => {
    const f = this.filtered();
    const total = f.reduce((a, r) => a + (r.samples ?? 0), 0);
    const received = f.filter((r) => r.status === 'Received').reduce((a, r) => a + (r.samples ?? 0), 0);
    return { total, received, pending: total - received, rate: total ? Math.round((received / total) * 100) : 0 };
  });

  constructor() { this.load(); }

  opts(field: 'governorate' | 'area'): string[] {
    return [...new Set(this.items().map((i) => i[field]).filter((x): x is string => !!x))].sort();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<ReceivingItem[]>('/labcheckin', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  reset(): void { this.start = this.today; this.end = this.today; this.gov = this.area = 'All'; this.load(); }

  confirm(r: ReceivingItem): void {
    this.busy.set(true);
    this.api.post('/labcheckin/confirm', { visitId: r.visitId }).subscribe({
      next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false),
    });
  }
}
