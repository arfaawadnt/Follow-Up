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
import { CollectionDto, PagedResult, RepListItem } from '../../core/models';
import { ACC_STYLES, COLLECTION_TYPES, IBAN_OPTIONS, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };
/** Dialog row: one rep and the amount they handed in (only asked for on a Group collection). */
type ShareRow = { repId: string; amount: number | null };

/**
 * Collection — money handed in by one Lab Responsible (Single) or several (Group), split into Cash and Bank. A
 * collection is the rep's act, not a lab's (a Lab Responsible collects from many labs), so there is no lab on it. On a
 * Group collection the user enters each rep's amount and the amounts must add up to cash + bank. A bank amount must name
 * the IBAN it went into (fixed pick 12 / 16 / 18); with no bank amount the IBAN is cleared.
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

    <div class="kpis" style="grid-template-columns:repeat(5,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">{{ 'total' | t : 'Total' }}</div><div class="val">{{ k().total | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'cash' | t : 'Cash' }}</div><div class="val">{{ k().cash | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'bank' | t : 'Bank' }}</div><div class="val">{{ k().bank | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'outsource_income' | t : 'Out-source income' }}</div><div class="val">{{ k().outsource | number:'1.2-2' }}</div><div class="sub">{{ 'collection_income' | t : 'Collection income' }} {{ k().net | number:'1.2-2' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ k().count }}</div>@if (k().noRef) { <div class="sub">{{ k().noRef }} {{ 'bank_without_reference' | t : 'bank records without reference' }}</div> }</div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:1fr 1fr 1fr 1.5fr 1.2fr 1.2fr auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'governorate' | t : 'Governorate' }}</label><app-filter-select [ngModel]="governorate" (ngModelChange)="pickGovernorate($event)" [options]="governorateOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'lab_responsible' | t : 'Lab Responsible' }}</label><app-filter-select [(ngModel)]="repId" [options]="filterRepOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'reference_number' | t : 'Reference Number' }}</label><input class="input" [(ngModel)]="reference" (ngModelChange)="referenceSig.set($event)" [placeholder]="'search' | t : 'Search…'"></div>
        <div class="field"><label>&nbsp;</label><label class="small" style="display:flex;align-items:center;gap:6px;height:36px"><input type="checkbox" [ngModel]="missingRef" (ngModelChange)="missingRefSig.set($event); missingRef = $event"> {{ 'bank_without_reference_filter' | t : 'Bank without reference' }}</label></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'serial' | t : 'Serial' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'rep' | t : 'Rep' }}</th>
            <th class="r">{{ 'cash' | t : 'Cash' }}</th><th class="r">{{ 'bank' | t : 'Bank' }}</th><th class="r">{{ 'total' | t : 'Total' }}</th><th class="r">{{ 'outsource_income' | t : 'Out-source income' }}</th><th class="r">{{ 'collection_income' | t : 'Collection income' }}</th>
            <th>{{ 'type' | t : 'Type' }}</th><th>{{ 'iban_number' | t : 'IBAN Number' }}</th><th>{{ 'reference_number' | t : 'Reference Number' }}</th><th>{{ 'done_by' | t : 'Done by' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (c of rows(); track c.id; let i = $index) {
              <tr>
                <td class="mono">{{ i + 1 }}</td><td>{{ ddmy(c.date) }}</td><td>{{ day(c.date) }}</td>
                <td><div class="chip-list">@for (s of c.shares; track s.repId) { <span>{{ s.repName }}@if (c.type === 'Group') { <b class="mono" style="margin-inline-start:6px">{{ s.amount | number:'1.2-2' }}</b> }</span> }</div></td>
                <td class="r mono">{{ c.cash | number:'1.2-2' }}</td><td class="r mono">{{ c.bank | number:'1.2-2' }}</td><td class="r mono" style="font-weight:700">{{ c.total | number:'1.2-2' }}</td>
                <td class="r mono">{{ c.outsourceIncome ? (c.outsourceIncome | number:'1.2-2') : '—' }}</td><td class="r mono">{{ c.netIncome | number:'1.2-2' }}</td>
                <td>{{ typeLabel(c.type) }}</td><td class="mono">{{ c.iban || '—' }}</td>
                <td class="mono">@if (c.referenceNumber) { {{ c.referenceNumber }} } @else if (c.bank > 0) { <span class="badge b-bad" [title]="'bank_without_reference_filter' | t : 'Bank records without Reference Number'">{{ 'missing' | t : 'Missing' }}</span> } @else { — }</td>
                <td>{{ c.doneBy || '—' }}</td><td>{{ c.notes || '—' }}</td>
                @if (canManage()) {
                  <td class="ar actions">
                    <button class="icon-btn" title="Edit" (click)="openEdit(c)">✎</button>
                    <button class="icon-btn del" title="Delete" (click)="remove(c, i + 1)">🗑</button>
                  </td>
                }
              </tr>
            } @empty { <tr><td colspan="15" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) { <tfoot><tr><td colspan="4">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ k().cash | number:'1.2-2' }}</td><td class="r mono">{{ k().bank | number:'1.2-2' }}</td><td class="r mono">{{ k().total | number:'1.2-2' }}</td><td class="r mono">{{ k().outsource | number:'1.2-2' }}</td><td class="r mono">{{ k().net | number:'1.2-2' }}</td><td colspan="5"></td>@if (canManage()) { <td></td> }</tr></tfoot> }
        </table></div>
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
              <div class="field"><label>{{ 'type' | t : 'Type' }} *</label>
                <select class="select" [ngModel]="f.type" (ngModelChange)="setType($event)">@for (t of types; track t) { <option [value]="t">{{ typeLabel(t) }}</option> }</select></div>
              <div class="field"><label>{{ 'governorate' | t : 'Governorate' }}</label>
                <app-filter-select [ngModel]="f.governorate" (ngModelChange)="pickDialogGovernorate($event)" [options]="governorateOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select>
                <div class="small muted" style="margin-top:4px">{{ 'governorate_reps_hint' | t : 'Narrows the Lab Responsible list to the governorate' }}</div></div>
              @if (f.type === 'Single') {
                <div class="field"><label>{{ 'lab_responsible' | t : 'Lab Responsible' }} *</label>
                  <app-filter-select [ngModel]="f.shares[0]?.repId || ''" (ngModelChange)="setSingleRep($event)" [options]="dialogRepOptions()" [clearable]="true" placeholder="—" [searchPlaceholder]="'search_responsibles' | t : 'Search responsibles…'"></app-filter-select>
                  <div class="small muted" style="margin-top:4px">{{ 'only_lab_responsibles' | t : 'Only Lab Responsible reps collect' }}</div>
                </div>
              } @else {
                <div class="field" style="grid-column:1/-1"><label>{{ 'acc_reps' | t : 'Reps' }} *</label>
                  <app-filter-select [multiple]="true" [ngModel]="f.groupIds" (ngModelChange)="setGroupReps($event)" [options]="dialogRepOptions()" placeholder="—" [searchPlaceholder]="'search_responsibles' | t : 'Search responsibles…'"></app-filter-select>
                  <div class="small muted" style="margin-top:4px">{{ 'only_lab_responsibles' | t : 'Only Lab Responsible reps collect' }}</div>
                </div>
              }
              <div class="field"><label>{{ 'cash' | t : 'Cash' }}</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.cash"></div>
              <div class="field"><label>{{ 'bank' | t : 'Bank' }}</label><input class="input" type="number" min="0" step="0.01" [ngModel]="f.bank" (ngModelChange)="setBank($event)"></div>
              @if ((f.bank ?? 0) > 0) {
                <div class="field"><label>{{ 'iban_number' | t : 'IBAN Number' }} *</label>
                  <select class="select" [(ngModel)]="f.iban"><option value="">—</option>@for (i of ibans; track i) { <option [value]="i">{{ i }}</option> }</select></div>
                <div class="field"><label>{{ 'reference_number' | t : 'Reference Number' }}</label><input class="input" [(ngModel)]="f.referenceNumber" maxlength="100" [placeholder]="'reference_number_hint' | t : 'Bank transfer reference (for revision)'"></div>
              }
              <div class="field"><label>{{ 'outsource_income' | t : 'Out-source income' }}</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.outsourceIncome">
                <div class="small muted" style="margin-top:4px">{{ 'outsource_income_hint' | t : 'Part of the cash / bank that is out-source income: the rep collection income = cash + bank − out-source' }}</div></div>
              <div class="field"><label>{{ 'total' | t : 'Total' }}</label><input class="input" [value]="formTotal() | number:'1.2-2'" disabled>
                @if ((f.outsourceIncome ?? 0) > 0) { <div class="small" [style.color]="outsourceOk() ? '' : 'var(--red-600, #dc2626)'" style="margin-top:4px">{{ 'collection_income' | t : 'Collection income' }}: {{ formNet() | number:'1.2-2' }}</div> }</div>
              @if (f.type === 'Group' && f.shares.length) {
                <div class="field" style="grid-column:1/-1">
                  <label>{{ 'amount_per_rep' | t : 'Amount per rep' }} *</label>
                  <div class="grid-scroll"><table class="grid-table" style="margin:0">
                    <thead><tr><th>{{ 'rep' | t : 'Rep' }}</th><th class="r" style="width:180px">{{ 'amount' | t : 'Amount' }}</th></tr></thead>
                    <tbody>
                      @for (s of f.shares; track s.repId) {
                        <tr><td>{{ repName(s.repId) }}</td><td class="r"><input class="input mono" type="number" min="0.01" step="0.01" [(ngModel)]="s.amount" style="text-align:end"></td></tr>
                      }
                    </tbody>
                    <tfoot><tr><td>{{ 'total' | t : 'Total' }}</td><td class="r mono" [style.color]="sharesOk() ? '' : 'var(--red-600, #dc2626)'">{{ sharesSum() | number:'1.2-2' }} / {{ formTotal() | number:'1.2-2' }}</td></tr></tfoot>
                  </table></div>
                  @if (!sharesOk()) { <div class="small" style="color:var(--red-600, #dc2626);margin-top:4px">{{ 'shares_must_equal_total' | t : 'The reps amounts must add up to cash + bank' }}</div> }
                </div>
              }
              <div class="field"><label>{{ 'done_by' | t : 'Done by' }}</label><input class="input" [(ngModel)]="f.doneBy" maxlength="200"></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="500"></div>
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
  readonly loaded = signal<CollectionDto[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  from = firstOfMonth(); to = localToday(); repId = ''; governorate = ''; reference = ''; missingRef = false;
  /** Grid-side filters (applied to the loaded range without a reload): reference number text and "bank without reference". */
  readonly referenceSig = signal(''); readonly missingRefSig = signal(false);
  private readonly pageGovernorate = signal(''); private readonly dialogGovernorate = signal('');
  editId: string | null = null;
  f = this.blank();

  /** The pickers list active Lab Responsible reps only — the type that collects the labs' money; a governorate narrows them. */
  private responsibles(governorate: string): RepListItem[] {
    return this.reps().filter((r) => r.isActive && r.type === 'LabResponsible' && (!governorate || r.governorate === governorate));
  }
  readonly repOptions = computed<Opt[]>(() => this.responsibles('').map((r) => ({ value: r.id, label: r.fullName })));
  readonly filterRepOptions = computed<Opt[]>(() => this.responsibles(this.pageGovernorate()).map((r) => ({ value: r.id, label: r.fullName })));
  readonly dialogRepOptions = computed<Opt[]>(() => this.responsibles(this.dialogGovernorate()).map((r) => ({ value: r.id, label: r.fullName })));
  /** The governorates of the Lab Responsible reps (a collection carries no lab, so the rep's governorate is the dimension). */
  readonly governorateOptions = computed<Opt[]>(() => [...new Set(this.responsibles('').map((r) => r.governorate).filter((g): g is string => !!g))]
    .sort((a, b) => a.localeCompare(b)).map((g) => ({ value: g, label: g })));
  /** The grid: the loaded range narrowed by governorate (a rep of it on the collection), reference text and the missing-reference toggle. */
  readonly rows = computed<CollectionDto[]>(() => {
    const gov = this.pageGovernorate(); const govReps = gov ? new Set(this.responsibles(gov).map((r) => r.id)) : null;
    const ref = this.referenceSig().trim().toLowerCase(); const missing = this.missingRefSig();
    return this.loaded().filter((c) => (!govReps || c.repIds.some((id) => govReps.has(id)))
      && (!ref || (c.referenceNumber ?? '').toLowerCase().includes(ref))
      && (!missing || (c.bank > 0 && !c.referenceNumber)));
  });
  readonly k = computed(() => {
    const r = this.rows();
    const cash = r.reduce((a, c) => a + c.cash, 0); const bank = r.reduce((a, c) => a + c.bank, 0); const outsource = r.reduce((a, c) => a + c.outsourceIncome, 0);
    return { cash, bank, total: cash + bank, outsource, net: cash + bank - outsource, count: r.length, noRef: r.filter((c) => c.bank > 0 && !c.referenceNumber).length };
  });

  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  typeLabel(t: string): string { return t; }
  repName(id: string): string { return this.reps().find((r) => r.id === id)?.fullName ?? '—'; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.repId) params['repId'] = this.repId;
    this.api.get<CollectionDto[]>('/accounting/collections', params).subscribe({ next: (r) => { this.loaded.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  /** A governorate pick narrows the rep filter; a rep outside it is cleared. */
  pickGovernorate(g: string): void { this.governorate = g ?? ''; this.pageGovernorate.set(this.governorate); if (this.repId && !this.filterRepOptions().some((o) => o.value === this.repId)) this.repId = ''; }
  pickDialogGovernorate(g: string): void {
    this.f.governorate = g ?? ''; this.dialogGovernorate.set(this.f.governorate);
    const offered = new Set(this.dialogRepOptions().map((o) => o.value));
    if (this.f.shares.some((s) => !offered.has(s.repId))) this.setShares(this.f.shares.filter((s) => offered.has(s.repId)));
  }

  /** `groupIds` mirrors `shares` for the multi-select's [ngModel]: a STABLE array reference, rebuilt only when the selection
   *  changes (a getter returning a fresh array each change-detection pass would make ngModel re-write the control every cycle). */
  private blank() {
    return { date: localToday(), type: 'Single', governorate: '', shares: [] as ShareRow[], groupIds: [] as string[], cash: null as number | null, bank: null as number | null, iban: '',
      referenceNumber: '', outsourceIncome: null as number | null, doneBy: '', notes: '' };
  }
  openNew(): void { this.editId = null; this.f = this.blank(); this.f.governorate = this.governorate; this.dialogGovernorate.set(this.governorate); this.dlg.set(true); }
  openEdit(c: CollectionDto): void {
    this.editId = c.id;
    this.dialogGovernorate.set('');
    this.f = { date: c.date, type: c.type, governorate: '', shares: c.shares.map((s) => ({ repId: s.repId, amount: s.amount })), groupIds: c.shares.map((s) => s.repId), cash: c.cash, bank: c.bank, iban: c.iban ?? '',
      referenceNumber: c.referenceNumber ?? '', outsourceIncome: c.outsourceIncome || null, doneBy: c.doneBy ?? '', notes: c.notes ?? '' };
    this.dlg.set(true);
  }
  /** Switching Single ↔ Group keeps at most one rep for Single so the rule "exactly one rep" is visible immediately. */
  setType(t: string): void { this.f.type = t; if (t === 'Single' && this.f.shares.length > 1) this.setShares([this.f.shares[0]]); }
  setSingleRep(id: string): void { this.setShares(id ? [{ repId: id, amount: null }] : []); }
  /** Keeps the amounts already typed for reps that stay selected; new reps start empty. */
  setGroupReps(ids: string[]): void { this.setShares((ids ?? []).map((id) => this.f.shares.find((s) => s.repId === id) ?? { repId: id, amount: null })); }
  private setShares(shares: ShareRow[]): void { this.f.shares = shares; this.f.groupIds = shares.map((s) => s.repId); }
  /** No bank amount → no IBAN and no reference number (mirrors the domain rule). */
  setBank(v: number | null): void { this.f.bank = v; if (!v || v <= 0) { this.f.iban = ''; this.f.referenceNumber = ''; } }
  formTotal(): number { return money((this.f.cash ?? 0) + (this.f.bank ?? 0)); }
  /** cash + bank − out-source income: what the rep statement credits as collection income. */
  formNet(): number { return money(this.formTotal() - (this.f.outsourceIncome ?? 0)); }
  outsourceOk(): boolean { const o = this.f.outsourceIncome ?? 0; return o >= 0 && o <= this.formTotal(); }
  sharesSum(): number { return money(this.f.shares.reduce((a, s) => a + (s.amount ?? 0), 0)); }
  /** Group: every amount > 0 and Σ = cash + bank (2-decimal money compare). Single: the one rep takes the total. */
  sharesOk(): boolean {
    if (this.f.type !== 'Group') return true;
    return this.f.shares.every((s) => (s.amount ?? 0) > 0) && this.sharesSum() === this.formTotal();
  }
  valid(): boolean {
    const f = this.f; const cash = f.cash ?? 0; const bank = f.bank ?? 0;
    if (!f.date || cash < 0 || bank < 0 || cash + bank <= 0) return false;
    if (f.type === 'Single' && f.shares.length !== 1) return false;
    if (f.type === 'Group' && (f.shares.length < 2 || !this.sharesOk())) return false;
    if (bank > 0 && !f.iban) return false;
    if (!this.outsourceOk()) return false;
    return true;
  }
  save(): void {
    if (!this.valid()) return;
    this.busy.set(true);
    const bank = this.f.bank ?? 0; const total = this.formTotal();
    const shares = this.f.type === 'Single' ? [{ repId: this.f.shares[0].repId, amount: total }] : this.f.shares.map((s) => ({ repId: s.repId, amount: money(s.amount) }));
    const body = { date: this.f.date, type: this.f.type, shares, cash: this.f.cash ?? 0, bank,
      iban: bank > 0 ? this.f.iban : null, referenceNumber: bank > 0 ? this.f.referenceNumber.trim() || null : null,
      outsourceIncome: money(this.f.outsourceIncome ?? 0), doneBy: this.f.doneBy.trim() || null, notes: this.f.notes.trim() || null };
    const req = this.editId ? this.api.put(`/accounting/collections/${this.editId}`, body) : this.api.post('/accounting/collections', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Collection saved.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(c: CollectionDto, rowNo: number): void {
    if (!confirm(`Delete collection #${rowNo} (${ddmy(c.date)} · ${c.repNames.join(', ')})?`)) return;
    this.api.delete(`/accounting/collections/${c.id}`).subscribe({ next: () => { this.toast.success('Collection deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Serial', 'Date', 'Day', 'Rep(s)', 'Amount per rep', 'Cash', 'Bank', 'Total', 'Out-source income', 'Collection income', 'Type', 'IBAN Number', 'Reference Number', 'Done by', 'Notes'];
  private exportRows() {
    return this.rows().map((c, i) => [i + 1, ddmy(c.date), this.day(c.date), c.repNames.join(', '),
      c.shares.map((s) => `${s.repName}: ${money(s.amount).toFixed(2)}`).join('; '),
      money(c.cash), money(c.bank), money(c.total), money(c.outsourceIncome), money(c.netIncome), c.type, c.iban ?? '', c.referenceNumber ?? '', c.doneBy ?? '', c.notes ?? '']);
  }
  exportExcel(): void { exportXlsx(`collections-${localToday()}.xlsx`, CollectionsComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Collection (${ddmy(this.from)} → ${ddmy(this.to)})`, CollectionsComponent.HEADER, this.exportRows()); }
}
