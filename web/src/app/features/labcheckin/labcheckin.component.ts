import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ReceivingItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportCsv, printTable, localToday, localDateTime } from '../../shared/export.util';

@Component({
  selector: 'app-labcheckin',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px">
      <div><div class="breadcrumbs">Home / {{ 'labcheckin' | t }}</div><h1>{{ 'labcheckin' | t }}</h1></div>
      <div style="display:flex;gap:8px">
        <button class="btn btn-s" (click)="exportExcel()" [disabled]="!filtered().length">Export Excel</button>
        <button class="btn btn-s" (click)="exportPdf()" [disabled]="!filtered().length">Export PDF</button>
        @if (auth.has('ConfirmTransfers')) {
          <button class="btn btn-p" (click)="confirmSelected()" [disabled]="busy() || selected.size === 0">{{ 'confirm_selected_receipts' | t : 'Confirm Selected Receipts' }}</button>
        }
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:24px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_transferred_samples' | t : 'Total Transferred Samples' }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">{{ 'in_transit_or_received' | t : 'In transit or received' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'received_samples' | t : 'Received Samples' }}</div><div class="val">{{ k().received | number:'1.0-0' }}</div><div class="sub">{{ 'confirmed_at_lab' | t : 'Confirmed at lab' }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'awaiting_receipt' | t : 'Awaiting Receipt' }}</div><div class="val">{{ k().pending | number:'1.0-0' }}</div><div class="sub">{{ 'awaiting_confirmation' | t }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'receipt_completion_rate' | t : 'Receipt Completion Rate' }}</div><div class="val">{{ k().rate }}%</div><div class="sub">{{ 'overall_completion_rate' | t }}</div></div>
    </div>

    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
        <div class="field"><label>{{ 'start_date' | t }}</label><input type="date" class="input" [(ngModel)]="start"></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><input type="date" class="input" [(ngModel)]="end"></div>
        <div class="field"><label>{{ 'branch_2' | t }}</label>
          <select class="select" [(ngModel)]="branch"><option value="All">{{ 'all_2' | t }}</option>@for (b of opts('branch'); track b) { <option [value]="b">{{ b }}</option> }</select></div>
        <div class="field"><label>{{ 'governorate_2' | t }}</label>
          <select class="select" [(ngModel)]="gov"><option value="All">{{ 'all_2' | t }}</option>@for (g of opts('governorate'); track g) { <option [value]="g">{{ g }}</option> }</select></div>
      </div>
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px">
        <div class="field"><label>{{ 'city_2' | t }}</label>
          <select class="select" [(ngModel)]="city"><option value="All">{{ 'all_2' | t }}</option>@for (c of opts('city'); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        <div class="field"><label>{{ 'area_2' | t }}</label>
          <select class="select" [(ngModel)]="area"><option value="All">{{ 'all_2' | t }}</option>@for (a of opts('area'); track a) { <option [value]="a">{{ a }}</option> }</select></div>
        <div class="field"><label>{{ 'transfer_rep' | t : 'Transfer rep' }}</label>
          <select class="select" [(ngModel)]="repFilter"><option value="All">{{ 'all_2' | t }}</option>@for (n of repNames(); track n) { <option [value]="n">{{ n }}</option> }</select></div>
      </div>
      <div style="display:flex;gap:8px;margin-top:12px">
        <button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th style="width:28px"><input type="checkbox" [checked]="allSelected()" (change)="toggleAll()"></th>
            <th>{{ 'laboratory_2' | t }}</th><th>{{ 'collection_date_and_time' | t }}</th><th>{{ 'collector_rep' | t : 'Collector rep' }}</th>
            <th>{{ 'samples' | t }}</th><th>{{ 'transfer_time' | t : 'Transfer Time' }}</th><th>{{ 'status_3' | t }}</th><th style="text-align:center">{{ 'confirm_receipt' | t : 'Confirm receipt' }}</th>
          </tr></thead>
          <tbody>
            @for (r of filtered(); track r.visitId) {
              <tr>
                <td>@if (r.status !== 'Received') { <input type="checkbox" [checked]="selected.has(r.visitId)" (change)="toggleRow(r.visitId)"> }</td>
                <td><b style="color:var(--slate-900)">{{ r.labName }}</b><div class="small muted">{{ r.labDisplayCode }}@if (r.area) { · {{ r.area }} }</div></td>
                <td class="mono small">{{ r.visitDate }} · {{ r.visitTime }}</td>
                <td>{{ r.collectorName ?? '—' }}<div class="small muted">{{ r.transferRepName ?? '' }}</div></td>
                <td class="mono" style="font-weight:700">{{ r.samples ?? 0 }}</td>
                <td class="mono small">{{ when(r.transferTime) }}</td>
                <td><span class="badge" [class]="r.status === 'Received' ? 'b-ok' : 'b-warn'">{{ (r.status === 'Received' ? 'received' : 'transferred') | t : r.status }}</span></td>
                <td style="text-align:center">
                  @if (r.status === 'Received') { <span style="color:var(--ok-ink)" [title]="when(r.receivedTime)">✓</span> }
                </td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'nothing_awaiting_receipt' | t : 'Nothing awaiting receipt.' }}</td></tr> }
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

  private readonly today = localToday();
  start = this.today; end = this.today; branch = 'All'; gov = 'All'; city = 'All'; area = 'All'; repFilter = 'All';
  readonly selected = new Set<string>();

  readonly filtered = computed(() => this.items().filter((i) =>
    (this.branch === 'All' || i.branch === this.branch) &&
    (this.gov === 'All' || i.governorate === this.gov) &&
    (this.city === 'All' || i.city === this.city) &&
    (this.area === 'All' || i.area === this.area) &&
    (this.repFilter === 'All' || i.transferRepName === this.repFilter)));

  repNames(): string[] { return [...new Set(this.items().map((i) => i.transferRepName).filter((x): x is string => !!x))].sort(); }

  readonly k = computed(() => {
    const f = this.filtered();
    const total = f.reduce((a, r) => a + (r.samples ?? 0), 0);
    const received = f.filter((r) => r.status === 'Received').reduce((a, r) => a + (r.samples ?? 0), 0);
    return { total, received, pending: total - received, rate: total ? Math.round((received / total) * 100) : 0 };
  });

  constructor() { this.load(); }

  opts(field: 'branch' | 'governorate' | 'city' | 'area'): string[] {
    return [...new Set(this.items().map((i) => i[field]).filter((x): x is string => !!x))].sort();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<ReceivingItem[]>('/labcheckin', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  reset(): void { this.start = this.today; this.end = this.today; this.branch = this.gov = this.city = this.area = this.repFilter = 'All'; this.load(); }

  when(iso: string | null): string { return localDateTime(iso); }

  // ---- Selection + batch (reference: "Confirm Selected Receipts") ----

  private pendingRows(): ReceivingItem[] { return this.filtered().filter((r) => r.status !== 'Received'); }
  toggleRow(id: string): void { if (this.selected.has(id)) this.selected.delete(id); else this.selected.add(id); }
  allSelected(): boolean { const p = this.pendingRows(); return p.length > 0 && p.every((r) => this.selected.has(r.visitId)); }
  toggleAll(): void {
    const all = this.allSelected();
    for (const r of this.pendingRows()) { if (all) this.selected.delete(r.visitId); else this.selected.add(r.visitId); }
  }

  confirmSelected(): void {
    const visitIds = [...this.selected];
    if (!visitIds.length) return;
    this.busy.set(true);
    this.api.post('/labcheckin/confirm-batch', { visitIds }).subscribe({
      next: () => { this.busy.set(false); this.selected.clear(); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  // ---- Exports ----

  private exportRows(): (string | number | null)[][] {
    return this.filtered().map((r) => [r.labName, r.labDisplayCode, r.area, r.governorate, `${r.visitDate} ${r.visitTime}`,
      r.collectorName, r.transferRepName, r.samples ?? 0, this.when(r.transferTime), r.status, this.when(r.receivedTime)]);
  }
  private static readonly EXPORT_HEADER = ['Laboratory', 'Code', 'Area', 'Governorate', 'Collected', 'Collector rep', 'Transfer rep', 'Samples', 'Transfer time', 'Status', 'Received at'];
  exportExcel(): void { exportCsv('lab-checkin.csv', LabCheckInComponent.EXPORT_HEADER, this.exportRows()); }
  exportPdf(): void { printTable('Lab Checkin', LabCheckInComponent.EXPORT_HEADER, this.exportRows()); }
}
