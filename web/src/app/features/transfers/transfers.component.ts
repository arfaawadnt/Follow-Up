import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepListItem, TransferItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportCsv, printTable, localToday, localDateTime } from '../../shared/export.util';

interface Draft { rep: string; name: string; mobile: string; car: string; when: string; done: boolean; }

@Component({
  selector: 'app-transfers',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px">
      <div><div class="breadcrumbs">Home / {{ 'transfers' | t }}</div><h1>{{ 'transfers' | t }}</h1></div>
      <div style="display:flex;gap:8px">
        <button class="btn btn-s" (click)="exportExcel()" [disabled]="!filtered().length">Export Excel</button>
        <button class="btn btn-s" (click)="exportPdf()" [disabled]="!filtered().length">Export PDF</button>
        @if (auth.has('ConfirmTransfers')) {
          <button class="btn btn-p" (click)="saveAll()" [disabled]="busy() || readyCount() === 0">{{ 'save_transfer_confirmations' | t : 'Save Transfer Confirmations' }}</button>
        }
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:24px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_collected_samples' | t }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">{{ 'collected_samples' | t }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'transferred_samples' | t }}</div><div class="val">{{ k().transferred | number:'1.0-0' }}</div><div class="sub">{{ 'transferred_successfully' | t }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'pending_transfer' | t }}</div><div class="val">{{ k().pending | number:'1.0-0' }}</div><div class="sub">{{ 'awaiting_confirmation' | t }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'transfer_success_rate' | t }}</div><div class="val">{{ k().rate }}%</div><div class="sub">{{ 'overall_completion_rate' | t }}</div></div>
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
          <select class="select" [(ngModel)]="repFilter"><option value="All">{{ 'all_2' | t }}</option>@for (rep of transferReps(); track rep.id) { <option [value]="rep.id">{{ rep.fullName }}</option> }</select></div>
        <div class="field" style="align-self:end"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    @if (auth.has('ConfirmTransfers')) {
      <div class="card" style="padding:14px 20px;margin-bottom:20px;display:flex;gap:10px;align-items:end;flex-wrap:wrap">
        <b style="font-size:12.5px;align-self:center">Batch Add Driver Info:</b>
        <div class="field"><label>{{ 'driver_name' | t : 'Driver Name:' }}</label><input class="input" style="width:160px" [(ngModel)]="batchName"></div>
        <div class="field"><label>{{ 'mobile' | t : 'Mobile:' }}</label><input class="input" style="width:140px" [(ngModel)]="batchMobile"></div>
        <div class="field"><label>{{ 'car_no' | t : 'Car No.:' }}</label><input class="input" style="width:110px" [(ngModel)]="batchCar"></div>
        <div class="field"><label>Transfer rep</label>
          <select class="select" style="width:170px" [(ngModel)]="batchRep"><option value="">—</option>@for (rep of reps(); track rep.id) { <option [value]="rep.id">{{ rep.fullName }}</option> }</select></div>
        <button class="btn btn-s" style="height:36px" [disabled]="selectedCount() === 0" (click)="applyToSelected()">{{ 'apply_to_selected' | t : 'Apply to Selected' }}</button>
      </div>
    }

    @if (loading()) { <div class="card empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
    @else if (groups().length === 0) { <div class="card empty" style="padding:24px;text-align:center">{{ 'no_collected_samples_found_matching_the_' | t : 'No collected samples found.' }}</div> }
    @else {
      @for (grp of groups(); track grp.area) {
        <div class="card" style="margin-bottom:20px;padding:0;overflow:hidden">
          <div style="background:var(--slate-100);padding:10px 16px;font-weight:700;border-bottom:1px solid var(--slate-150);font-size:13px;display:flex;justify-content:space-between;align-items:center">
            <span>{{ 'area_3' | t : 'Area' }} <b>{{ grp.area }}</b></span>
            <span class="badge b-info">{{ grp.rows.length }} {{ 'lab_visit_s' | t : 'visit(s)' }}</span>
          </div>
          <div style="overflow-x:auto">
            <table class="grid-table" style="margin:0;border:none">
              <thead><tr>
                <th style="width:28px"><input type="checkbox" [checked]="allSelected(grp.rows)" (change)="toggleAll(grp.rows)"></th>
                <th>{{ 'laboratory_2' | t }}</th><th>{{ 'collection_date_and_time' | t }}</th><th>{{ 'collector_rep' | t }}</th>
                <th>{{ 'samples' | t }}</th><th>{{ 'status_3' | t }}</th><th>{{ 'driver_info' | t }}</th>
                <th>{{ 'transfer_rep' | t }}</th><th>Transfer date &amp; time</th><th style="text-align:center">Transferred?</th>
              </tr></thead>
              <tbody>
                @for (r of grp.rows; track r.visitId) {
                  <tr>
                    <td>@if (!r.transferDone) { <input type="checkbox" [checked]="selected.has(r.visitId)" (change)="toggleRow(r.visitId)"> }</td>
                    <td><b style="color:var(--slate-900)">{{ r.labName }}</b><div class="small muted">{{ r.labDisplayCode }} · {{ r.branch ?? '—' }}</div></td>
                    <td class="mono small">{{ r.visitDate }} · {{ r.visitTime }}</td>
                    <td>{{ r.collectorName ?? '—' }}</td>
                    <td class="mono" style="font-weight:700">{{ r.samples ?? 0 }}</td>
                    <td><span class="badge" [class]="r.transferDone ? 'b-ok' : 'b-warn'">{{ (r.transferDone ? 'transferred' : 'collected') | t : (r.transferDone ? 'Transferred' : 'Collected') }}</span></td>
                    <td>
                      @if (r.transferDone) { <div class="small muted">{{ r.driverName }} · {{ r.driverMobile }}<br>{{ r.carPlate }}</div> }
                      @else if (auth.has('ConfirmTransfers')) {
                        <div style="display:flex;flex-direction:column;gap:4px">
                          <input class="input" style="padding:4px 8px;font-size:11px;width:130px" [placeholder]="'driver_name_2' | t : 'Driver name'" [(ngModel)]="draft(r.visitId).name">
                          <input class="input" style="padding:4px 8px;font-size:11px;width:130px" [placeholder]="'driver_mobile' | t : 'Driver mobile'" [(ngModel)]="draft(r.visitId).mobile">
                          <input class="input" style="padding:4px 8px;font-size:11px;width:130px" [placeholder]="'car_no_2' | t : 'Car no.'" [(ngModel)]="draft(r.visitId).car">
                        </div>
                      }
                    </td>
                    <td>
                      @if (r.transferDone) { {{ r.transferRepName ?? '—' }} }
                      @else if (auth.has('ConfirmTransfers')) {
                        <select class="select" style="width:100%;padding:4px 8px" [(ngModel)]="draft(r.visitId).rep">
                          <option value="">{{ 'select_rep' | t : 'Select rep' }}</option>
                          @for (rep of reps(); track rep.id) { <option [value]="rep.id">{{ rep.fullName }}</option> }
                        </select>
                      }
                    </td>
                    <td>
                      @if (r.transferDone) { <span class="mono small">{{ transferAt(r) }}</span> }
                      @else if (auth.has('ConfirmTransfers')) {
                        <input type="datetime-local" class="input" style="padding:4px 8px;font-size:11px" [(ngModel)]="draft(r.visitId).when">
                      }
                    </td>
                    <td style="text-align:center">
                      @if (!r.transferDone && auth.has('ConfirmTransfers')) {
                        <input type="checkbox" [(ngModel)]="draft(r.visitId).done" title="Mark transferred (saved with Save Transfer Confirmations)">
                      } @else if (r.transferDone) { <span style="color:var(--ok-ink)">✓</span> }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    }
  `,
})
export class TransfersComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<TransferItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  private readonly drafts = new Map<string, Draft>();

  private readonly today = localToday();
  start = this.today; end = this.today;
  branch = 'All'; gov = 'All'; city = 'All'; area = 'All'; repFilter = 'All';
  batchName = ''; batchMobile = ''; batchCar = ''; batchRep = '';
  readonly selected = new Set<string>();
  readonly selectionVersion = signal(0);

  readonly filtered = computed(() => this.items().filter((i) =>
    (this.branch === 'All' || i.branch === this.branch) &&
    (this.gov === 'All' || i.governorate === this.gov) &&
    (this.city === 'All' || i.city === this.city) &&
    (this.area === 'All' || i.area === this.area) &&
    (this.repFilter === 'All' || i.transferRepId === this.repFilter || this.draft(i.visitId).rep === this.repFilter)));

  readonly transferReps = computed(() => this.reps().filter((r) => r.type === 'Transfer'));

  readonly k = computed(() => {
    const f = this.filtered();
    const total = f.reduce((a, r) => a + (r.samples ?? 0), 0);
    const transferred = f.filter((r) => r.transferDone).reduce((a, r) => a + (r.samples ?? 0), 0);
    return { total, transferred, pending: total - transferred, rate: total ? Math.round((transferred / total) * 100) : 0 };
  });

  readonly groups = computed(() => {
    const by = new Map<string, TransferItem[]>();
    for (const r of this.filtered()) { const a = r.area ?? '—'; (by.get(a) ?? by.set(a, []).get(a)!).push(r); }
    return [...by.entries()].map(([area, rows]) => ({ area, rows })).sort((a, b) => a.area.localeCompare(b.area));
  });

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    this.load();
  }

  draft(id: string): Draft {
    let d = this.drafts.get(id);
    if (!d) { d = { rep: '', name: '', mobile: '', car: '', when: '', done: false }; this.drafts.set(id, d); }
    return d;
  }
  opts(field: 'branch' | 'governorate' | 'city' | 'area'): string[] {
    return [...new Set(this.items().map((i) => i[field]).filter((x): x is string => !!x))].sort();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<TransferItem[]>('/transfers', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  reset(): void { this.start = this.today; this.end = this.today; this.branch = this.gov = this.city = this.area = 'All'; this.load(); }

  transferAt(r: TransferItem): string { return localDateTime(r.transferTime); }

  // ---- Selection + batch (reference: Batch Add Driver Info / Save Transfer Confirmations) ----

  toggleRow(id: string): void { if (this.selected.has(id)) this.selected.delete(id); else this.selected.add(id); this.selectionVersion.update((v) => v + 1); }
  allSelected(rows: TransferItem[]): boolean { const p = rows.filter((r) => !r.transferDone); return p.length > 0 && p.every((r) => this.selected.has(r.visitId)); }
  toggleAll(rows: TransferItem[]): void {
    const p = rows.filter((r) => !r.transferDone);
    const all = this.allSelected(rows);
    for (const r of p) { if (all) this.selected.delete(r.visitId); else this.selected.add(r.visitId); }
    this.selectionVersion.update((v) => v + 1);
  }
  selectedCount(): number { return this.selected.size; }

  applyToSelected(): void {
    for (const id of this.selected) {
      const d = this.draft(id);
      if (this.batchName) d.name = this.batchName;
      if (this.batchMobile) d.mobile = this.batchMobile;
      if (this.batchCar) d.car = this.batchCar;
      if (this.batchRep) d.rep = this.batchRep;
    }
    this.selectionVersion.update((v) => v + 1);
  }

  private readyLines(): { visitId: string; transferRepId: string; driverName: string; driverMobile: string; carPlate: string | null; transferredAt: string | null }[] {
    return this.filtered().filter((r) => !r.transferDone)
      .map((r) => ({ r, d: this.draft(r.visitId) }))
      .filter(({ d }) => d.done && d.rep && d.name && d.mobile)
      .map(({ r, d }) => ({
        visitId: r.visitId, transferRepId: d.rep, driverName: d.name, driverMobile: d.mobile, carPlate: d.car || null,
        transferredAt: d.when ? new Date(d.when).toISOString() : null,
      }));
  }
  readyCount(): number { return this.readyLines().length; }

  saveAll(): void {
    const lines = this.readyLines();
    if (!lines.length) return;
    this.busy.set(true);
    this.api.post('/transfers/confirm-batch', { lines }).subscribe({
      next: () => { this.busy.set(false); for (const l of lines) this.drafts.delete(l.visitId); this.selected.clear(); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  // ---- Exports ----

  private exportRows(): (string | number | null)[][] {
    return this.filtered().map((r) => [r.labName, r.labDisplayCode, r.area, r.branch, `${r.visitDate} ${r.visitTime}`,
      r.collectorName, r.samples ?? 0, r.transferDone ? 'Transferred' : 'Collected',
      r.driverName, r.driverMobile, r.carPlate, r.transferRepName, r.transferTime]);
  }
  private static readonly EXPORT_HEADER = ['Laboratory', 'Code', 'Area', 'Branch', 'Collected', 'Collector', 'Samples', 'Status', 'Driver', 'Mobile', 'Car', 'Transfer rep', 'Transferred at'];
  exportExcel(): void { exportCsv('transfers.csv', TransfersComponent.EXPORT_HEADER, this.exportRows()); }
  exportPdf(): void { printTable('Transfer Management', TransfersComponent.EXPORT_HEADER, this.exportRows()); }
}
