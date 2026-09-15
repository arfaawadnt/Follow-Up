import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { UiService } from '../../core/ui.service';
import { TranslatePipe } from '../../core/i18n';
import { DeductionDto, DeductionSuggestion } from '../../core/models';
import { ACC_STYLES, DEDUCTION_REASONS, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };
interface AreaOpt { id: string; name: string; percentageDeal: boolean; percentage: number | null; }

/**
 * Deductions — per-area deductions. Transportation is typed; Penalty and Percentage Deal values are suggested by the
 * server over a period (Σ penalties of the area's labs / area income × deal %) and stay editable.
 */
@Component({
  selector: 'app-acc-deductions',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_deductions' | t : 'Deductions' }}</div><h1>{{ 'acc_deductions' | t : 'Deductions' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'record_deduction' | t : 'Record deduction' }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">{{ 'total' | t : 'Total' }}</div><div class="val">{{ k().total | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'transportation' | t : 'Transportation' }}</div><div class="val">{{ k().transportation | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'penalty' | t : 'Penalty' }}</div><div class="val">{{ k().penalty | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'percentage_deal' | t : 'Percentage Deal' }}</div><div class="val">{{ k().deal | number:'1.2-2' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [(ngModel)]="areaId" [options]="areaOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'serial' | t : 'Serial' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'area_2' | t : 'Area' }}</th>
            <th>{{ 'reason' | t : 'Reason' }}</th><th>{{ 'period' | t : 'Period' }}</th><th class="r">{{ 'value' | t : 'Value' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (d of rows(); track d.id) {
              <tr>
                <td class="mono">{{ d.serial }}</td><td>{{ ddmy(d.date) }}</td><td>{{ day(d.date) }}</td><td><b>{{ d.areaName }}</b></td>
                <td>{{ reasonLabel(d.reason) }}</td>
                <td class="small muted">@if (d.periodFrom) { {{ ddmy(d.periodFrom) }} → {{ ddmy(d.periodTo) }} } @else { — }</td>
                <td class="r mono">{{ d.value | number:'1.2-2' }}</td><td>{{ d.notes || '—' }}</td>
                @if (canManage()) {
                  <td class="ar actions">
                    <button class="icon-btn" title="Edit" (click)="openEdit(d)">✎</button>
                    <button class="icon-btn del" title="Delete" (click)="remove(d)">🗑</button>
                  </td>
                }
              </tr>
            } @empty { <tr><td colspan="9" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) { <tfoot><tr><td colspan="6">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ k().total | number:'1.2-2' }}</td><td></td>@if (canManage()) { <td></td> }</tr></tfoot> }
        </table></div>
      }
    </div>

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(false)">
        <div class="as-dlg" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ editId ? ('edit' | t : 'Edit') : ('record_deduction' | t : 'Record deduction') }}</h2><button class="btn btn-mini btn-s" (click)="dlg.set(false)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'date' | t : 'Date' }} *</label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field"><label>{{ 'day' | t : 'Day' }}</label><input class="input" [value]="day(f.date)" disabled></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'area_2' | t : 'Area' }} *</label><app-filter-select [(ngModel)]="f.areaId" [options]="areaOptions()" [clearable]="true" placeholder="—" [disabled]="!!editId"></app-filter-select></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'reason' | t : 'Reason' }} *</label>
                <select class="select" [ngModel]="f.reason" (ngModelChange)="setReason($event)">@for (r of reasons; track r) { <option [value]="r">{{ reasonLabel(r) }}</option> }</select>
                @if (f.reason === 'PercentageDeal' && selectedArea() && !selectedArea()!.percentageDeal) { <div class="basis neg">{{ 'no_active_deal' | t : 'This area has no active Percentage Deal.' }}</div> }
              </div>
              @if (f.reason !== 'Transportation') {
                <div class="field"><label>{{ 'period' | t : 'Period' }} · {{ 'start_date' | t }}</label><app-date-input [(ngModel)]="f.periodFrom"></app-date-input></div>
                <div class="field"><label>{{ 'period' | t : 'Period' }} · {{ 'end_date' | t }}</label><app-date-input [(ngModel)]="f.periodTo"></app-date-input></div>
                <div class="field" style="grid-column:1/-1">
                  <button class="btn btn-s" type="button" [disabled]="suggesting() || !f.areaId || !f.periodFrom || !f.periodTo" (click)="suggest()">{{ suggesting() ? ('loading' | t : 'Loading…') : ('suggest_value' | t : 'Suggest value') }}</button>
                  @if (basis()) { <div class="basis">{{ basis() }}</div> }
                </div>
              }
              <div class="field"><label>{{ 'value' | t : 'Value' }} *</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.value"></div>
              <div class="field"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="500"></div>
            </div>
          </div>
          <div class="as-dlg-foot">
            <button class="btn btn-s" (click)="dlg.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy() || !valid()" (click)="save()">{{ 'save' | t : 'Save' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [ACC_STYLES],
})
export class DeductionsComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;
  readonly reasons = DEDUCTION_REASONS;

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly suggesting = signal(false);
  readonly dlg = signal(false);
  readonly basis = signal('');
  readonly rows = signal<DeductionDto[]>([]);
  readonly areas = signal<AreaOpt[]>([]);
  from = firstOfMonth(); to = localToday(); areaId = '';
  editId: string | null = null;
  f = this.blank();

  readonly areaOptions = computed<Opt[]>(() => this.areas().map((a) => ({ value: a.id, label: a.percentageDeal ? `${a.name} · ${a.percentage}%` : a.name })));
  readonly selectedArea = computed(() => this.areas().find((a) => a.id === this.f.areaId) ?? null);
  readonly k = computed(() => {
    const r = this.rows();
    const sum = (reason: string) => r.filter((d) => d.reason === reason).reduce((a, d) => a + d.value, 0);
    return { total: r.reduce((a, d) => a + d.value, 0), transportation: sum('Transportation'), penalty: sum('Penalty'), deal: sum('PercentageDeal') };
  });

  constructor() {
    this.api.get<AreaOpt[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  reasonLabel(r: string): string { return r === 'PercentageDeal' ? 'Percentage Deal' : r; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.areaId) params['areaId'] = this.areaId;
    this.api.get<DeductionDto[]>('/accounting/deductions', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private blank() { return { date: localToday(), areaId: '', reason: 'Transportation', value: null as number | null, notes: '', periodFrom: firstOfMonth(), periodTo: localToday() }; }
  openNew(): void { this.editId = null; this.f = this.blank(); this.basis.set(''); this.dlg.set(true); }
  openEdit(d: DeductionDto): void {
    this.editId = d.id; this.basis.set('');
    this.f = { date: d.date, areaId: d.areaId, reason: d.reason, value: d.value, notes: d.notes ?? '', periodFrom: d.periodFrom ?? firstOfMonth(), periodTo: d.periodTo ?? localToday() };
    this.dlg.set(true);
  }
  setReason(r: string): void { this.f.reason = r; this.basis.set(''); }
  suggest(): void {
    this.suggesting.set(true);
    this.api.get<DeductionSuggestion>('/accounting/deductions/suggest', { areaId: this.f.areaId, reason: this.f.reason, from: this.f.periodFrom, to: this.f.periodTo })
      .subscribe({ next: (s) => { this.f.value = s.value; this.basis.set(s.basis); this.suggesting.set(false); }, error: () => this.suggesting.set(false) });
  }
  valid(): boolean {
    const f = this.f;
    if (!f.date || !f.areaId || !f.reason || f.value === null || f.value < 0) return false;
    if (f.reason !== 'Transportation' && (!f.periodFrom || !f.periodTo || f.periodTo < f.periodFrom)) return false;
    return true;
  }
  save(): void {
    if (!this.valid()) return;
    this.busy.set(true);
    const typed = this.f.reason === 'Transportation';
    const body = { date: this.f.date, areaId: this.f.areaId, reason: this.f.reason, value: this.f.value, notes: this.f.notes.trim() || null,
      periodFrom: typed ? null : this.f.periodFrom, periodTo: typed ? null : this.f.periodTo };
    const req = this.editId ? this.api.put(`/accounting/deductions/${this.editId}`, body) : this.api.post('/accounting/deductions', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Deduction saved.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(d: DeductionDto): void {
    if (!confirm(`Delete deduction #${d.serial}?`)) return;
    this.api.delete(`/accounting/deductions/${d.id}`).subscribe({ next: () => { this.toast.success('Deduction deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Serial', 'Date', 'Day', 'Area', 'Reason', 'Period from', 'Period to', 'Value', 'Notes'];
  private exportRows() {
    return this.rows().map((d) => [d.serial, ddmy(d.date), this.day(d.date), d.areaName, this.reasonLabel(d.reason), d.periodFrom ? ddmy(d.periodFrom) : '', d.periodTo ? ddmy(d.periodTo) : '', money(d.value), d.notes ?? '']);
  }
  exportExcel(): void { exportXlsx(`deductions-${localToday()}.xlsx`, DeductionsComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Deductions (${ddmy(this.from)} → ${ddmy(this.to)})`, DeductionsComponent.HEADER, this.exportRows()); }
}
