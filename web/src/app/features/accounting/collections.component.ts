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
import { CollectionDto, LabListItem, PagedResult, RepListItem } from '../../core/models';
import { ACC_STYLES, COLLECTION_TYPES, IBAN_OPTIONS, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };

/**
 * Collection — money collected from a lab by one rep (Single) or several (Group), split into Cash and Bank. A bank
 * amount must name the IBAN it went into (fixed pick 12 / 16 / 18); with no bank amount the IBAN is cleared.
 */
@Component({
  selector: 'app-acc-collections',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_collections' | t : 'Collection' }}</div><h1>{{ 'acc_collections' | t : 'Collection' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'record_collection' | t : 'Record collection' }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">{{ 'total' | t : 'Total' }}</div><div class="val">{{ k().total | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'cash' | t : 'Cash' }}</div><div class="val">{{ k().cash | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'bank' | t : 'Bank' }}</div><div class="val">{{ k().bank | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ k().count }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'lab' | t : 'Lab' }}</label><app-filter-select [(ngModel)]="labId" [options]="labOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'rep' | t : 'Rep' }}</label><app-filter-select [(ngModel)]="repId" [options]="repOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'serial' | t : 'Serial' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'lab' | t : 'Lab' }}</th><th>{{ 'rep' | t : 'Rep' }}</th>
            <th class="r">{{ 'cash' | t : 'Cash' }}</th><th class="r">{{ 'bank' | t : 'Bank' }}</th><th class="r">{{ 'total' | t : 'Total' }}</th>
            <th>{{ 'type' | t : 'Type' }}</th><th>{{ 'iban_number' | t : 'IBAN Number' }}</th><th>{{ 'done_by' | t : 'Done by' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (c of rows(); track c.id) {
              <tr>
                <td class="mono">{{ c.serial }}</td><td>{{ ddmy(c.date) }}</td><td>{{ day(c.date) }}</td>
                <td><b>{{ c.labName }}</b> <span class="small muted">{{ c.labDisplayCode }}</span></td>
                <td><div class="chip-list">@for (n of c.repNames; track $index) { <span>{{ n }}</span> }</div></td>
                <td class="r mono">{{ c.cash | number:'1.2-2' }}</td><td class="r mono">{{ c.bank | number:'1.2-2' }}</td><td class="r mono" style="font-weight:700">{{ c.total | number:'1.2-2' }}</td>
                <td>{{ typeLabel(c.type) }}</td><td class="mono">{{ c.iban || '—' }}</td><td>{{ c.doneBy || '—' }}</td><td>{{ c.notes || '—' }}</td>
                @if (canManage()) {
                  <td class="ar actions">
                    <button class="icon-btn" title="Edit" (click)="openEdit(c)">✎</button>
                    <button class="icon-btn del" title="Delete" (click)="remove(c)">🗑</button>
                  </td>
                }
              </tr>
            } @empty { <tr><td colspan="13" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) { <tfoot><tr><td colspan="5">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ k().cash | number:'1.2-2' }}</td><td class="r mono">{{ k().bank | number:'1.2-2' }}</td><td class="r mono">{{ k().total | number:'1.2-2' }}</td><td colspan="4"></td>@if (canManage()) { <td></td> }</tr></tfoot> }
        </table>
      }
    </div>

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(false)">
        <div class="as-dlg" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ editId ? ('edit' | t : 'Edit') : ('record_collection' | t : 'Record collection') }}</h2><button class="btn btn-mini btn-s" (click)="dlg.set(false)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'date' | t : 'Date' }} *</label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field"><label>{{ 'day' | t : 'Day' }}</label><input class="input" [value]="day(f.date)" disabled></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'lab' | t : 'Lab' }} *</label><app-filter-select [(ngModel)]="f.laboratoryId" [options]="labOptions()" [clearable]="true" placeholder="—" [disabled]="!!editId"></app-filter-select></div>
              <div class="field"><label>{{ 'type' | t : 'Type' }} *</label>
                <select class="select" [ngModel]="f.type" (ngModelChange)="setType($event)">@for (t of types; track t) { <option [value]="t">{{ typeLabel(t) }}</option> }</select></div>
              <div class="field"><label>{{ f.type === 'Group' ? ('acc_reps' | t : 'Reps') : ('rep' | t : 'Rep') }} *</label>
                @if (f.type === 'Group') { <app-filter-select [multiple]="true" [(ngModel)]="f.repIds" [options]="repOptions()" placeholder="—"></app-filter-select> }
                @else { <app-filter-select [ngModel]="f.repIds[0] || ''" (ngModelChange)="f.repIds = $event ? [$event] : []" [options]="repOptions()" [clearable]="true" placeholder="—"></app-filter-select> }
              </div>
              <div class="field"><label>{{ 'cash' | t : 'Cash' }}</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.cash"></div>
              <div class="field"><label>{{ 'bank' | t : 'Bank' }}</label><input class="input" type="number" min="0" step="0.01" [ngModel]="f.bank" (ngModelChange)="setBank($event)"></div>
              @if ((f.bank ?? 0) > 0) {
                <div class="field"><label>{{ 'iban_number' | t : 'IBAN Number' }} *</label>
                  <select class="select" [(ngModel)]="f.iban"><option value="">—</option>@for (i of ibans; track i) { <option [value]="i">{{ i }}</option> }</select></div>
              }
              <div class="field"><label>{{ 'done_by' | t : 'Done by' }}</label><input class="input" [(ngModel)]="f.doneBy" maxlength="200"></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="500"></div>
              <div class="field"><label>{{ 'total' | t : 'Total' }}</label><input class="input" [value]="(f.cash ?? 0) + (f.bank ?? 0) | number:'1.2-2'" disabled></div>
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
export class CollectionsComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;
  readonly types = COLLECTION_TYPES;
  readonly ibans = IBAN_OPTIONS;

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal(false);
  readonly rows = signal<CollectionDto[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  from = firstOfMonth(); to = localToday(); labId = ''; repId = '';
  editId: string | null = null;
  f = this.blank();

  readonly labOptions = computed<Opt[]>(() => this.labs().map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` })));
  readonly repOptions = computed<Opt[]>(() => this.reps().filter((r) => r.isActive).map((r) => ({ value: r.id, label: r.fullName })));
  readonly k = computed(() => {
    const r = this.rows();
    const cash = r.reduce((a, c) => a + c.cash, 0); const bank = r.reduce((a, c) => a + c.bank, 0);
    return { cash, bank, total: cash + bank, count: r.length };
  });

  constructor() {
    this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items), error: () => {} });
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  typeLabel(t: string): string { return t; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.labId) params['laboratoryId'] = this.labId;
    if (this.repId) params['repId'] = this.repId;
    this.api.get<CollectionDto[]>('/accounting/collections', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private blank() { return { date: localToday(), laboratoryId: '', type: 'Single', repIds: [] as string[], cash: null as number | null, bank: null as number | null, iban: '', doneBy: '', notes: '' }; }
  openNew(): void { this.editId = null; this.f = this.blank(); this.dlg.set(true); }
  openEdit(c: CollectionDto): void {
    this.editId = c.id;
    this.f = { date: c.date, laboratoryId: c.laboratoryId, type: c.type, repIds: [...c.repIds], cash: c.cash, bank: c.bank, iban: c.iban ?? '', doneBy: c.doneBy ?? '', notes: c.notes ?? '' };
    this.dlg.set(true);
  }
  /** Switching Single ↔ Group keeps at most one rep for Single so the rule "exactly one rep" is visible immediately. */
  setType(t: string): void { this.f.type = t; if (t === 'Single' && this.f.repIds.length > 1) this.f.repIds = [this.f.repIds[0]]; }
  /** No bank amount → no IBAN (mirrors the domain rule). */
  setBank(v: number | null): void { this.f.bank = v; if (!v || v <= 0) this.f.iban = ''; }
  valid(): boolean {
    const f = this.f; const cash = f.cash ?? 0; const bank = f.bank ?? 0;
    if (!f.date || !f.laboratoryId || cash < 0 || bank < 0 || cash + bank <= 0) return false;
    if (f.type === 'Single' && f.repIds.length !== 1) return false;
    if (f.type === 'Group' && f.repIds.length < 2) return false;
    if (bank > 0 && !f.iban) return false;
    return true;
  }
  save(): void {
    if (!this.valid()) return;
    this.busy.set(true);
    const bank = this.f.bank ?? 0;
    const body = { date: this.f.date, laboratoryId: this.f.laboratoryId, type: this.f.type, repIds: this.f.repIds, cash: this.f.cash ?? 0, bank,
      iban: bank > 0 ? this.f.iban : null, doneBy: this.f.doneBy.trim() || null, notes: this.f.notes.trim() || null };
    const req = this.editId ? this.api.put(`/accounting/collections/${this.editId}`, body) : this.api.post('/accounting/collections', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Collection saved.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(c: CollectionDto): void {
    if (!confirm(`Delete collection #${c.serial}?`)) return;
    this.api.delete(`/accounting/collections/${c.id}`).subscribe({ next: () => { this.toast.success('Collection deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Serial', 'Date', 'Day', 'Lab', 'Code', 'Rep(s)', 'Cash', 'Bank', 'Total', 'Type', 'IBAN Number', 'Done by', 'Notes'];
  private exportRows() {
    return this.rows().map((c) => [c.serial, ddmy(c.date), this.day(c.date), c.labName, c.labDisplayCode, c.repNames.join(', '), money(c.cash), money(c.bank), money(c.total), c.type, c.iban ?? '', c.doneBy ?? '', c.notes ?? '']);
  }
  exportExcel(): void { exportXlsx(`collections-${localToday()}.xlsx`, CollectionsComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Collection (${ddmy(this.from)} → ${ddmy(this.to)})`, CollectionsComponent.HEADER, this.exportRows()); }
}
