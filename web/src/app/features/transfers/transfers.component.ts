import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepListItem, TransferItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface Draft { rep: string; name: string; mobile: string; car: string; }

@Component({
  selector: 'app-transfers',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'transfers' | t }}</div><h1>{{ 'transfers' | t }}</h1></div>
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
        <div class="field" style="align-self:end"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_dates' | t }}</button></div>
        <div class="field" style="align-self:end"><button class="btn btn-s" (click)="reset()" style="height:36px">{{ 'reset_filters' | t }}</button></div>
      </div>
    </div>

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
                <th>{{ 'laboratory_2' | t }}</th><th>{{ 'collection_date_and_time' | t }}</th><th>{{ 'collector_rep' | t }}</th>
                <th>{{ 'samples' | t }}</th><th>{{ 'status_3' | t }}</th><th>{{ 'driver_info' | t }}</th>
                <th>{{ 'transfer_rep' | t }}</th><th style="text-align:center">{{ 'transferred_2' | t }}</th>
              </tr></thead>
              <tbody>
                @for (r of grp.rows; track r.visitId) {
                  <tr>
                    <td><b style="color:var(--slate-900)">{{ r.labName }}</b><div class="small muted">{{ r.labCode }} · {{ r.branch ?? '—' }}</div></td>
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
                    <td style="text-align:center">
                      @if (!r.transferDone && auth.has('ConfirmTransfers')) {
                        <button class="btn btn-mini btn-p" [disabled]="busy() || !draft(r.visitId).rep || !draft(r.visitId).name || !draft(r.visitId).mobile" (click)="confirm(r)">{{ 'confirm' | t : 'Confirm' }}</button>
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

  private readonly today = new Date().toISOString().slice(0, 10);
  start = this.today; end = this.today;
  branch = 'All'; gov = 'All'; city = 'All'; area = 'All';

  readonly filtered = computed(() => this.items().filter((i) =>
    (this.branch === 'All' || i.branch === this.branch) &&
    (this.gov === 'All' || i.governorate === this.gov) &&
    (this.city === 'All' || i.city === this.city) &&
    (this.area === 'All' || i.area === this.area)));

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
    if (!d) { d = { rep: '', name: '', mobile: '', car: '' }; this.drafts.set(id, d); }
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

  confirm(r: TransferItem): void {
    const d = this.draft(r.visitId);
    this.busy.set(true);
    this.api.post('/transfers/confirm', {
      visitId: r.visitId, transferRepId: d.rep, driverName: d.name, driverMobile: d.mobile, carPlate: d.car || null,
    }).subscribe({
      next: () => { this.busy.set(false); this.drafts.delete(r.visitId); this.load(); },
      error: () => this.busy.set(false),
    });
  }
}
