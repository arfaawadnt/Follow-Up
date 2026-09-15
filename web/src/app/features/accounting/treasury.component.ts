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
import { RefItem, TreasuryDto, TreasuryEntryDto, TreasuryReasonDto } from '../../core/models';
import { ACC_STYLES, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };

/**
 * Treasury Account — many treasuries, each assigned to branches; every movement is one-sided: Debit = cash INTO the
 * treasury, Credit = expenses OUT of the lab. The Setup tab maintains treasuries (name, branches, active) and the
 * configurable reasons; both are deactivated rather than deleted so history keeps resolving.
 */
@Component({
  selector: 'app-acc-treasury',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_treasury' | t : 'Treasury Account' }}</div><h1>{{ 'acc_treasury' | t : 'Treasury Account' }}</h1></div>
      <div class="pagehead-actions">
        @if (tab() === 'account') {
          @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'record_entry' | t : 'Record entry' }}</button> }
          <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
          <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
        }
      </div>
    </div>

    @if (canManage()) {
      <div class="tabs">
        <button [class.on]="tab() === 'account'" (click)="tab.set('account')">{{ 'acc_account' | t : 'Account' }}</button>
        <button [class.on]="tab() === 'setup'" (click)="tab.set('setup')">{{ 'acc_setup' | t : 'Treasury Setup' }}</button>
      </div>
    }

    @if (tab() === 'account') {
      <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
        <div class="kpi kpi-green"><div class="lbl">{{ 'total_debit' | t : 'Total debit' }}</div><div class="val">{{ k().debit | number:'1.2-2' }}</div><div class="sub">{{ 'debit_hint' | t : 'Cash into the treasury' }}</div></div>
        <div class="kpi kpi-amber"><div class="lbl">{{ 'total_credit' | t : 'Total credit' }}</div><div class="val">{{ k().credit | number:'1.2-2' }}</div><div class="sub">{{ 'credit_hint' | t : 'Expenses out of the lab' }}</div></div>
        <div class="kpi kpi-blue"><div class="lbl">{{ 'net' | t : 'Net' }}</div><div class="val">{{ k().net | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
        <div class="kpi kpi-teal"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ k().count }}</div></div>
      </div>

      <div class="card" style="padding:16px;margin-bottom:16px">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
          <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
          <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
          <div class="field"><label>{{ 'treasury' | t : 'Treasury' }}</label><app-filter-select [(ngModel)]="treasuryId" [options]="treasuryOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
          <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
        </div>
      </div>

      <div class="card" style="padding:10px 0;overflow-x:auto">
        @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
            <thead><tr>
              <th>{{ 'serial' | t : 'Serial' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'treasury' | t : 'Treasury' }}</th>
              <th class="r">{{ 'debit' | t : 'Debit' }}</th><th class="r">{{ 'credit_out' | t : 'Credit' }}</th><th>{{ 'reason' | t : 'Reason' }}</th><th>{{ 'notes' | t : 'Notes' }}</th>
              @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
            </tr></thead>
            <tbody>
              @for (e of rows(); track e.id; let i = $index) {
                <tr>
                  <td class="mono">{{ i + 1 }}</td><td>{{ ddmy(e.date) }}</td><td>{{ day(e.date) }}</td><td><b>{{ e.treasuryName }}</b></td>
                  <td class="r mono pos">{{ e.debit ? (e.debit | number:'1.2-2') : '' }}</td><td class="r mono neg">{{ e.credit ? (e.credit | number:'1.2-2') : '' }}</td>
                  <td>{{ e.reasonName }}</td><td>{{ e.notes || '—' }}</td>
                  @if (canManage()) {
                    <td class="ar actions">
                      <button class="icon-btn" title="Edit" (click)="openEdit(e)">✎</button>
                      <button class="icon-btn del" title="Delete" (click)="remove(e, i + 1)">🗑</button>
                    </td>
                  }
                </tr>
              } @empty { <tr><td colspan="9" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
            </tbody>
            @if (rows().length) { <tfoot><tr><td colspan="4">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ k().debit | number:'1.2-2' }}</td><td class="r mono">{{ k().credit | number:'1.2-2' }}</td><td colspan="2">{{ 'net' | t : 'Net' }}: {{ k().net | number:'1.2-2' }}</td>@if (canManage()) { <td></td> }</tr></tfoot> }
          </table></div>
        }
      </div>
    } @else {
      <div class="setup-grid" style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
        <div class="card" style="padding:16px">
          <h3 style="margin:0 0 10px">{{ 'treasuries' | t : 'Treasuries' }}</h3>
          <label class="lbl">{{ 'name' | t : 'Name' }}</label>
          <input class="input" [(ngModel)]="newTreasuryName" maxlength="100">
          <label class="lbl" style="margin-top:10px">{{ 'branches' | t : 'Branches' }}</label>
          <app-filter-select [multiple]="true" [(ngModel)]="newTreasuryBranches" [options]="branchOptions()" placeholder="—"></app-filter-select>
          <button class="btn btn-p" style="margin-top:12px" [disabled]="busy() || !newTreasuryName.trim() || !newTreasuryBranches.length" (click)="addTreasury()">{{ 'add' | t : 'Add' }}</button>
          <div class="grid-scroll"><table class="items" style="margin-top:14px">
            <thead><tr><th>{{ 'name' | t : 'Name' }}</th><th>{{ 'branches' | t : 'Branches' }}</th><th>{{ 'active' | t : 'Active' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th></tr></thead>
            <tbody>
              @for (t of treasuries(); track t.id) {
                <tr>
                  <td>@if (editTreasuryId() === t.id) { <input class="input" [(ngModel)]="editTreasuryName"> } @else { <b>{{ t.name }}</b> }</td>
                  <td>@if (editTreasuryId() === t.id) { <app-filter-select [multiple]="true" [(ngModel)]="editTreasuryBranches" [options]="branchOptions()"></app-filter-select> } @else { <div class="chip-list">@for (b of t.branches; track b) { <span>{{ b }}</span> }</div> }</td>
                  <td>@if (editTreasuryId() === t.id) { <input type="checkbox" [(ngModel)]="editTreasuryActive"> } @else { {{ t.isActive ? ('active' | t : 'Active') : ('inactive' | t : 'Inactive') }} }</td>
                  <td class="ar actions">
                    @if (editTreasuryId() === t.id) {
                      <button class="btn btn-mini btn-p" [disabled]="busy() || !editTreasuryName.trim() || !editTreasuryBranches.length" (click)="saveTreasury(t)">{{ 'save' | t : 'Save' }}</button>
                      <button class="btn btn-mini btn-s" (click)="editTreasuryId.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
                    } @else { <button class="icon-btn" title="Edit" (click)="startEditTreasury(t)">✎</button> }
                  </td>
                </tr>
              } @empty { <tr><td colspan="4" class="empty">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
            </tbody>
          </table></div>
        </div>
        <div class="card" style="padding:16px">
          <h3 style="margin:0 0 10px">{{ 'reasons' | t : 'Reasons' }}</h3>
          <label class="lbl">{{ 'name' | t : 'Name' }}</label>
          <input class="input" [(ngModel)]="newReasonName" maxlength="100">
          <button class="btn btn-p" style="margin-top:12px" [disabled]="busy() || !newReasonName.trim()" (click)="addReason()">{{ 'add' | t : 'Add' }}</button>
          <div class="grid-scroll"><table class="items" style="margin-top:14px">
            <thead><tr><th>{{ 'name' | t : 'Name' }}</th><th>{{ 'active' | t : 'Active' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th></tr></thead>
            <tbody>
              @for (r of reasons(); track r.id) {
                <tr>
                  <td>@if (editReasonId() === r.id) { <input class="input" [(ngModel)]="editReasonName"> } @else { <b>{{ r.name }}</b> }</td>
                  <td>@if (editReasonId() === r.id) { <input type="checkbox" [(ngModel)]="editReasonActive"> } @else { {{ r.isActive ? ('active' | t : 'Active') : ('inactive' | t : 'Inactive') }} }</td>
                  <td class="ar actions">
                    @if (editReasonId() === r.id) {
                      <button class="btn btn-mini btn-p" [disabled]="busy() || !editReasonName.trim()" (click)="saveReason(r)">{{ 'save' | t : 'Save' }}</button>
                      <button class="btn btn-mini btn-s" (click)="editReasonId.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
                    } @else { <button class="icon-btn" title="Edit" (click)="startEditReason(r)">✎</button> }
                  </td>
                </tr>
              } @empty { <tr><td colspan="3" class="empty">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
            </tbody>
          </table></div>
        </div>
      </div>
    }

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(false)">
        <div class="as-dlg" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ editId ? ('edit' | t : 'Edit') : ('record_entry' | t : 'Record entry') }}</h2><button class="btn btn-mini btn-s" (click)="dlg.set(false)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field" style="grid-column:1/-1"><label>{{ 'treasury' | t : 'Treasury' }} *</label><app-filter-select [(ngModel)]="f.treasuryId" [options]="activeTreasuryOptions()" [clearable]="true" placeholder="—" [disabled]="!!editId"></app-filter-select></div>
              <div class="field"><label>{{ 'date' | t : 'Date' }} *</label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field"><label>{{ 'day' | t : 'Day' }}</label><input class="input" [value]="day(f.date)" disabled></div>
              <div class="field" style="grid-column:1/-1">
                <label>{{ 'type' | t : 'Type' }} *</label>
                <div style="display:flex;gap:16px">
                  <label class="chk"><input type="radio" name="side" value="debit" [(ngModel)]="f.side"> {{ 'debit' | t : 'Debit' }} <span class="small muted">— {{ 'debit_hint' | t : 'Cash into the treasury' }}</span></label>
                  <label class="chk"><input type="radio" name="side" value="credit" [(ngModel)]="f.side"> {{ 'credit_out' | t : 'Credit' }} <span class="small muted">— {{ 'credit_hint' | t : 'Expenses out of the lab' }}</span></label>
                </div>
              </div>
              <div class="field"><label>{{ 'amount' | t : 'Amount' }} *</label><input class="input" type="number" min="0.01" step="0.01" [(ngModel)]="f.amount"></div>
              <div class="field"><label>{{ 'reason' | t : 'Reason' }} *</label>
                <select class="select" [(ngModel)]="f.reasonId"><option value="">—</option>@for (r of activeReasons(); track r.id) { <option [value]="r.id">{{ r.name }}</option> }</select></div>
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
export class TreasuryComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;

  readonly tab = signal<'account' | 'setup'>('account');
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal(false);
  readonly rows = signal<TreasuryEntryDto[]>([]);
  readonly treasuries = signal<TreasuryDto[]>([]);
  readonly reasons = signal<TreasuryReasonDto[]>([]);
  readonly branches = signal<string[]>([]);
  from = firstOfMonth(); to = localToday(); treasuryId = '';
  editId: string | null = null;
  f = this.blank();

  // setup state
  newTreasuryName = ''; newTreasuryBranches: string[] = []; newReasonName = '';
  readonly editTreasuryId = signal<string | null>(null); editTreasuryName = ''; editTreasuryBranches: string[] = []; editTreasuryActive = true;
  readonly editReasonId = signal<string | null>(null); editReasonName = ''; editReasonActive = true;

  readonly treasuryOptions = computed<Opt[]>(() => this.treasuries().map((t) => ({ value: t.id, label: t.name })));
  readonly activeTreasuryOptions = computed<Opt[]>(() => this.treasuries().filter((t) => t.isActive).map((t) => ({ value: t.id, label: t.name })));
  readonly activeReasons = computed(() => this.reasons().filter((r) => r.isActive));
  readonly branchOptions = computed(() => this.branches());
  readonly k = computed(() => {
    const r = this.rows();
    const debit = r.reduce((a, e) => a + e.debit, 0); const credit = r.reduce((a, e) => a + e.credit, 0);
    return { debit, credit, net: debit - credit, count: r.length };
  });

  constructor() {
    this.reloadConfig();
    this.api.get<RefItem[]>('/setup/refs', { type: 'Branch' }).subscribe({ next: (r) => this.branches.set(r.map((x) => x.nameEn).sort()), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }

  private reloadConfig(): void {
    this.api.get<TreasuryDto[]>('/accounting/treasuries').subscribe({ next: (r) => this.treasuries.set(r), error: () => {} });
    this.api.get<TreasuryReasonDto[]>('/accounting/treasury/reasons').subscribe({ next: (r) => this.reasons.set(r), error: () => {} });
  }
  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.treasuryId) params['treasuryId'] = this.treasuryId;
    this.api.get<TreasuryEntryDto[]>('/accounting/treasury/entries', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  // ---- entries ----
  private blank() { return { treasuryId: '', date: localToday(), side: 'debit' as 'debit' | 'credit', amount: null as number | null, reasonId: '', notes: '' }; }
  openNew(): void { this.editId = null; this.f = this.blank(); if (this.activeTreasuryOptions().length === 1) this.f.treasuryId = this.activeTreasuryOptions()[0].value; this.dlg.set(true); }
  openEdit(e: TreasuryEntryDto): void {
    this.editId = e.id;
    this.f = { treasuryId: e.treasuryId, date: e.date, side: e.debit > 0 ? 'debit' : 'credit', amount: e.debit > 0 ? e.debit : e.credit, reasonId: e.reasonId, notes: e.notes ?? '' };
    this.dlg.set(true);
  }
  valid(): boolean { const f = this.f; return !!f.treasuryId && !!f.date && !!f.reasonId && f.amount !== null && f.amount > 0; }
  save(): void {
    if (!this.valid()) return;
    this.busy.set(true);
    const amt = this.f.amount ?? 0;
    const body = { treasuryId: this.f.treasuryId, date: this.f.date, debit: this.f.side === 'debit' ? amt : 0, credit: this.f.side === 'credit' ? amt : 0, reasonId: this.f.reasonId, notes: this.f.notes.trim() || null };
    const req = this.editId ? this.api.put(`/accounting/treasury/entries/${this.editId}`, body) : this.api.post('/accounting/treasury/entries', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Entry saved.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(e: TreasuryEntryDto, rowNo: number): void {
    if (!confirm(`Delete entry #${rowNo} (${ddmy(e.date)} · ${e.treasuryName})?`)) return;
    this.api.delete(`/accounting/treasury/entries/${e.id}`).subscribe({ next: () => { this.toast.success('Entry deleted.'); this.load(); } });
  }

  // ---- setup: treasuries ----
  addTreasury(): void {
    this.busy.set(true);
    this.api.post('/accounting/treasuries', { name: this.newTreasuryName.trim(), branches: this.newTreasuryBranches, isActive: true })
      .subscribe({ next: () => { this.busy.set(false); this.newTreasuryName = ''; this.newTreasuryBranches = []; this.toast.success('Treasury added.'); this.reloadConfig(); }, error: () => this.busy.set(false) });
  }
  startEditTreasury(t: TreasuryDto): void { this.editTreasuryId.set(t.id); this.editTreasuryName = t.name; this.editTreasuryBranches = [...t.branches]; this.editTreasuryActive = t.isActive; }
  saveTreasury(t: TreasuryDto): void {
    this.busy.set(true);
    this.api.put(`/accounting/treasuries/${t.id}`, { name: this.editTreasuryName.trim(), branches: this.editTreasuryBranches, isActive: this.editTreasuryActive })
      .subscribe({ next: () => { this.busy.set(false); this.editTreasuryId.set(null); this.toast.success('Treasury saved.'); this.reloadConfig(); this.load(); }, error: () => this.busy.set(false) });
  }

  // ---- setup: reasons ----
  addReason(): void {
    this.busy.set(true);
    this.api.post('/accounting/treasury/reasons', { name: this.newReasonName.trim(), isActive: true })
      .subscribe({ next: () => { this.busy.set(false); this.newReasonName = ''; this.toast.success('Reason added.'); this.reloadConfig(); }, error: () => this.busy.set(false) });
  }
  startEditReason(r: TreasuryReasonDto): void { this.editReasonId.set(r.id); this.editReasonName = r.name; this.editReasonActive = r.isActive; }
  saveReason(r: TreasuryReasonDto): void {
    this.busy.set(true);
    this.api.put(`/accounting/treasury/reasons/${r.id}`, { name: this.editReasonName.trim(), isActive: this.editReasonActive })
      .subscribe({ next: () => { this.busy.set(false); this.editReasonId.set(null); this.toast.success('Reason saved.'); this.reloadConfig(); this.load(); }, error: () => this.busy.set(false) });
  }

  private static readonly HEADER = ['Serial', 'Date', 'Day', 'Treasury', 'Debit', 'Credit', 'Reason', 'Notes'];
  private exportRows() { return this.rows().map((e, i) => [i + 1, ddmy(e.date), this.day(e.date), e.treasuryName, money(e.debit), money(e.credit), e.reasonName, e.notes ?? '']); }
  exportExcel(): void { exportXlsx(`treasury-account-${localToday()}.xlsx`, TreasuryComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Treasury Account (${ddmy(this.from)} → ${ddmy(this.to)})`, TreasuryComponent.HEADER, this.exportRows()); }
}
