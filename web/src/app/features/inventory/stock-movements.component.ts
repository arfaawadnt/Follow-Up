import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';
import { InventoryItemDto, StockLotDto, StockMovementDto, StoreDto, TestLookup } from '../../core/models';
import { INV_STYLES, ISSUE_REASONS, MOVEMENT_TYPES, Opt, firstOfMonth, label, qty } from './inventory.util';

/**
 * Stock Ledger — every quantity change of every lot, signed (+ into the store, − out of it), with the balance of the lot
 * after it and the document it came from (goods receipt, transfer, manual). Manual entries are made here: "Issue stock"
 * (consumption for a test, damage, expiry disposal, return to the distributor) and "Adjust count" (a physical count that
 * corrects the on-hand quantity). The ledger itself is never edited — corrections are new adjustments.
 */
@Component({
  selector: 'app-stock-movements',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_movements' | t : 'Stock Ledger' }}</div><h1>{{ 'inv_movements' | t : 'Stock Ledger' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) {
          <button class="btn btn-p" (click)="openDlg('issue')">{{ 'issue_stock' | t : 'Issue stock' }}</button>
          <button class="btn btn-s" (click)="openDlg('adjust')">{{ 'adjust_count' | t : 'Adjust count' }}</button>
        }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr) auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'store' | t : 'Store' }}</label><app-filter-select [(ngModel)]="storeId" [options]="storeOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'item' | t : 'Item' }}</label><app-filter-select [(ngModel)]="itemId" [options]="itemOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'movement_type' | t : 'Type' }}</label><app-filter-select [(ngModel)]="type" [options]="typeOptions" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>#</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'movement_type' | t : 'Type' }}</th><th>{{ 'store' | t : 'Store' }}</th><th>{{ 'item' | t : 'Item' }}</th><th>{{ 'lot_number' | t : 'Lot No.' }}</th><th>{{ 'expiry_date' | t : 'Expiry' }}</th>
            <th class="r">{{ 'inv_quantity' | t : 'Quantity' }}</th><th class="r">{{ 'balance_after' | t : 'Balance' }}</th><th>{{ 'reference' | t : 'Reference' }}</th><th>{{ 'reason' | t : 'Reason' }}</th><th>{{ 'test' | t : 'Test' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th>{{ 'By' | t : 'By' }}</th>
          </tr></thead>
          <tbody>
            @for (m of rows(); track m.id) {
              <tr>
                <td class="mono">{{ m.serial }}</td><td>{{ ddmy(m.date) }}</td><td><span class="badge" [class]="'badge ' + typeClass(m)">{{ label(m.type) }}</span></td><td>{{ m.storeName }}</td>
                <td><b>{{ m.itemCode }}</b> · {{ m.itemName }}</td><td class="mono">{{ m.lotNumber }}</td><td>{{ m.expiryDate ? ddmy(m.expiryDate) : '—' }}</td>
                <td class="r mono" [class.pos]="m.quantity > 0" [class.neg]="m.quantity < 0">{{ m.quantity > 0 ? '+' : '' }}{{ qty(m.quantity) }} {{ m.unit }}</td><td class="r mono">{{ qty(m.balanceAfter) }}</td>
                <td class="mono">{{ m.referenceNumber || (m.referenceKind === 'Manual' ? ('Manual' | t : 'Manual') : m.referenceKind) }}</td><td>{{ m.reason ? label(m.reason) : '' }}</td><td class="mono">{{ m.testCode || '' }}</td><td>{{ m.notes || '' }}</td><td>{{ m.performedBy }}</td>
              </tr>
            } @empty { <tr><td colspan="14" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) { <tfoot><tr><td colspan="7">{{ rows().length }} {{ 'movements' | t : 'movements' }}</td><td class="r mono pos">+{{ qty(k().inQty) }}</td><td class="r mono neg">−{{ qty(k().outQty) }}</td><td colspan="5"></td></tr></tfoot> }
        </table></div>
      }
    </div>

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(null)">
        <div class="as-dlg" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ dlg() === 'issue' ? ('issue_stock' | t : 'Issue stock') : ('adjust_count' | t : 'Adjust count') }}</h2><button class="icon-btn" (click)="dlg.set(null)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'store' | t : 'Store' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.storeId" (ngModelChange)="loadLots()" [options]="storeOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'date' | t : 'Date' }} <span class="req">*</span></label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field" style="grid-column:span 2"><label>{{ 'Lot' | t : 'Lot' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.lotId" [options]="lotOptions()" [placeholder]="f.storeId ? ('select_user' | t : 'Select…') : ('Choose the store first' | t : 'Choose the store first')"></app-filter-select>
                @if (lot(); as l) { <div class="hint">{{ 'on_hand' | t : 'On hand' }}: <b>{{ qty(l.quantity) }} {{ l.unit }}</b>{{ l.expiryDate ? ' · ' + ('expiry_date' | t : 'Expiry') + ' ' + ddmy(l.expiryDate) : '' }} · <span class="badge" [class]="'badge ' + (l.status === 'Ok' ? 'b-ok' : l.status === 'Expired' ? 'b-bad' : 'b-warn')">{{ l.status }}</span></div> }</div>
              @if (dlg() === 'issue') {
                <div class="field"><label>{{ 'inv_quantity' | t : 'Quantity' }} <span class="req">*</span></label><input class="input" type="number" min="0" step="0.001" [(ngModel)]="f.quantity"></div>
                <div class="field"><label>{{ 'reason' | t : 'Reason' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.reason" [options]="reasonOptions"></app-filter-select></div>
                <div class="field" style="grid-column:span 2"><label>{{ 'test' | t : 'Test' }} <span class="muted">({{ 'optional' | t : 'optional' }} — {{ 'the test this consumption was for' | t : 'the test this consumption was for' }})</span></label><app-filter-select [(ngModel)]="f.testCode" [options]="testOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
              } @else {
                <div class="field"><label>{{ 'Counted quantity' | t : 'Counted quantity' }} <span class="req">*</span></label><input class="input" type="number" min="0" step="0.001" [(ngModel)]="f.quantity">
                  @if (lot(); as l) { <div class="hint">{{ 'Adjustment' | t : 'Adjustment' }}: <b>{{ (Number(f.quantity) || 0) - l.quantity > 0 ? '+' : '' }}{{ qty((Number(f.quantity) || 0) - l.quantity) }}</b></div> }</div>
                <div class="field"><label>{{ 'reason' | t : 'Reason' }} <span class="req">*</span></label><input class="input" [(ngModel)]="f.adjustReason" maxlength="40" placeholder="Physical count / Breakage / Correction"></div>
              }
              <div class="field" style="grid-column:span 2"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="500"></div>
            </div>
          </div>
          <div class="as-dlg-foot">
            @if (missing().length) { <span class="hint" style="margin:0 auto 0 0">{{ 'missing_fields' | t : 'Missing' }}: {{ missing().join(', ') }}</span> }
            <button class="btn btn-s" (click)="dlg.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy()" (click)="save()">{{ 'save' | t : 'Save' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [INV_STYLES],
})
export class StockMovementsComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly ddmy = ddmy; readonly qty = qty; readonly label = label; readonly Number = Number;

  readonly rows = signal<StockMovementDto[]>([]);
  readonly stores = signal<StoreDto[]>([]);
  readonly items = signal<InventoryItemDto[]>([]);
  readonly tests = signal<TestLookup[]>([]);
  readonly lots = signal<StockLotDto[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal<'issue' | 'adjust' | null>(null);
  from = firstOfMonth(); to = localToday(); storeId = ''; itemId = ''; type = '';
  f = this.blank();

  readonly typeOptions: Opt[] = MOVEMENT_TYPES.map((t) => ({ value: t, label: label(t) }));
  readonly reasonOptions: Opt[] = ISSUE_REASONS.map((r) => ({ value: r, label: label(r) }));
  readonly storeOptions = computed<Opt[]>(() => this.stores().map((s) => ({ value: s.id, label: `${s.name} (${s.branch})` })));
  readonly itemOptions = computed<Opt[]>(() => this.items().map((i) => ({ value: i.id, label: `${i.code} · ${i.name}` })));
  readonly testOptions = computed<Opt[]>(() => { const seen = new Set<string>(); return this.tests().filter((t) => !seen.has(t.code) && seen.add(t.code)).map((t) => ({ value: t.code, label: `${t.code} · ${t.name}` })); });
  readonly lotOptions = computed<Opt[]>(() => this.lots().map((l) => ({ value: l.id, label: `${l.itemCode} · ${l.itemName} · lot ${l.lotNumber}${l.expiryDate ? ' · exp ' + ddmy(l.expiryDate) : ''} · ${qty(l.quantity)} ${l.unit}` })));
  readonly k = computed(() => ({ inQty: this.rows().filter((m) => m.quantity > 0).reduce((a, m) => a + m.quantity, 0), outQty: -this.rows().filter((m) => m.quantity < 0).reduce((a, m) => a + m.quantity, 0) }));

  constructor() {
    this.api.get<StoreDto[]>('/inventory/stores').subscribe({ next: (r) => this.stores.set(r), error: () => {} });
    this.api.get<InventoryItemDto[]>('/inventory/items').subscribe({ next: (r) => this.items.set(r), error: () => {} });
    this.api.get<TestLookup[]>('/test-lookup').subscribe({ next: (r) => this.tests.set(r), error: () => {} });
    this.load();
  }
  canManage(): boolean { return this.auth.has('ManageInventory'); }
  typeClass(m: StockMovementDto): string { return m.type === 'Receipt' || m.type === 'TransferIn' ? 'b-ok' : m.type === 'Adjustment' ? 'b-neu' : m.type === 'Disposal' ? 'b-bad' : 'b-warn'; }
  lot(): StockLotDto | undefined { return this.lots().find((l) => l.id === this.f.lotId); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.storeId) params['storeId'] = this.storeId;
    if (this.itemId) params['itemId'] = this.itemId;
    if (this.type) params['type'] = this.type;
    this.api.get<StockMovementDto[]>('/inventory/movements', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private blank() { return { storeId: this.storeId, date: localToday(), lotId: '', quantity: null as number | null, reason: 'Consumption', testCode: '', adjustReason: '', notes: '' }; }
  openDlg(kind: 'issue' | 'adjust'): void { this.f = this.blank(); this.lots.set([]); if (this.f.storeId) this.loadLots(); this.dlg.set(kind); }
  loadLots(): void {
    this.f.lotId = ''; this.lots.set([]);
    if (!this.f.storeId) return;
    this.api.get<StockLotDto[]>('/inventory/lots', { storeId: this.f.storeId, includeEmpty: this.dlg() === 'adjust' }).subscribe({ next: (r) => this.lots.set(r), error: () => {} });
  }
  missing(): string[] {
    const m: string[] = [];
    if (!this.f.storeId) m.push('Store');
    if (!this.f.lotId) m.push('Lot');
    if (!this.f.date) m.push('Date');
    if (this.dlg() === 'issue') {
      if (!(Number(this.f.quantity) > 0)) m.push('Quantity > 0');
      const l = this.lot(); if (l && Number(this.f.quantity) > l.quantity) m.push(`Quantity exceeds on hand (${qty(l.quantity)})`);
      if (!this.f.reason) m.push('Reason');
    } else {
      if (this.f.quantity === null || Number(this.f.quantity) < 0) m.push('Counted quantity');
      const l = this.lot(); if (l && Number(this.f.quantity) === l.quantity) m.push('Counted quantity equals on hand');
      if (!this.f.adjustReason.trim()) m.push('Reason');
    }
    return m;
  }
  save(): void {
    const missing = this.missing();
    if (missing.length) { this.toast.warning(`Please check: ${missing.join(', ')}`); return; }
    this.busy.set(true);
    const req = this.dlg() === 'issue'
      ? this.api.post('/inventory/issues', { lotId: this.f.lotId, date: this.f.date, quantity: Number(this.f.quantity), reason: this.f.reason, testCode: this.f.testCode || null, notes: this.f.notes || null })
      : this.api.post('/inventory/adjustments', { lotId: this.f.lotId, date: this.f.date, countedQuantity: Number(this.f.quantity), reason: this.f.adjustReason.trim(), notes: this.f.notes || null });
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(null); this.toast.success(this.dlg() === 'issue' ? 'Stock issued.' : 'Count adjusted.'); this.load(); }, error: () => this.busy.set(false) });
  }

  private static readonly HEADER = ['#', 'Date', 'Type', 'Store', 'Item', 'Lot No.', 'Expiry', 'Quantity', 'Unit', 'Balance after', 'Reference', 'Reason', 'Test', 'Notes', 'By'];
  private exportRows() {
    return this.rows().map((m) => [m.serial, ddmy(m.date), label(m.type), m.storeName, `${m.itemCode} · ${m.itemName}`, m.lotNumber, ddmy(m.expiryDate), m.quantity, m.unit, m.balanceAfter,
      m.referenceNumber ?? m.referenceKind, m.reason ? label(m.reason) : '', m.testCode ?? '', m.notes ?? '', m.performedBy]);
  }
  exportExcel(): void { exportXlsx(`stock-ledger-${localToday()}.xlsx`, StockMovementsComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Stock Ledger (${ddmy(this.from)} → ${ddmy(this.to)})`, StockMovementsComponent.HEADER, this.exportRows()); }
}
