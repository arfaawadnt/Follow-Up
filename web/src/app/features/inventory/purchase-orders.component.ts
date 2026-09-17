import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, escHtml, exportXlsx, localToday, printDoc } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';
import { InventoryItemDto, PurchaseOrderDto, StoreDto, SupplierDto } from '../../core/models';
import { INV_STYLES, Opt, PO_STATUSES, badge, firstOfMonth, label, money, qty } from './inventory.util';

type LineDraft = { itemId: string; orderedQuantity: number | null; unitPrice: number | null; notes: string };
type ReceiptDraft = { orderLineId: string; itemCode: string; itemName: string; unit: string; outstanding: number; quantity: number | null; lotNumber: string; expiryDate: string; unitCost: number | null };

/**
 * Purchase Orders — orders of chemicals and consumables placed with a distributor for delivery to one store. Draft →
 * Submit (Ordered) → Receive goods line by line (lot number + expiry, counts validated against the outstanding quantity;
 * PartiallyReceived / Received) → Close (accept a short delivery) or Cancel (nothing received yet). Every receipt is a
 * document (GR-#) kept under the order.
 */
@Component({
  selector: 'app-purchase-orders',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_purchase_orders' | t : 'Purchase Orders' }}</div><h1>{{ 'inv_purchase_orders' | t : 'Purchase Orders' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'new_purchase_order' | t : 'New purchase order' }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'Orders' | t : 'Orders' }}</div><div class="val">{{ rows().length }}</div><div class="sub">{{ 'in the period' | t : 'in the period' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'Open' | t : 'Open' }}</div><div class="val">{{ k().open }}</div><div class="sub">{{ 'ordered / partially received' | t : 'ordered / partially received' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'received' | t : 'Received' }}</div><div class="val">{{ k().received }}</div><div class="sub">{{ 'fully received' | t : 'fully received' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total' | t : 'Total' }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">EGP ({{ 'excluding cancelled' | t : 'excluding cancelled' }})</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr) auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'status' | t : 'Status' }}</label><app-filter-select [(ngModel)]="status" [options]="statusOptions" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'distributor' | t : 'Distributor' }}</label><app-filter-select [(ngModel)]="supplierId" [options]="supplierOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'store' | t : 'Store' }}</label><app-filter-select [(ngModel)]="storeId" [options]="storeOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'Number' | t : 'Number' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'distributor' | t : 'Distributor' }}</th><th>{{ 'store' | t : 'Store' }}</th><th>{{ 'status' | t : 'Status' }}</th>
            <th class="r">{{ 'Lines' | t : 'Lines' }}</th><th class="r">{{ 'ordered' | t : 'Ordered' }}</th><th class="r">{{ 'received' | t : 'Received' }}</th><th class="r">{{ 'total' | t : 'Total' }}</th>
            <th>{{ 'Expected' | t : 'Expected' }}</th><th>{{ 'reference' | t : 'Reference' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th>
          </tr></thead>
          <tbody>
            @for (p of rows(); track p.id) {
              <tr>
                <td class="mono"><a href="javascript:void(0)" (click)="openView(p)"><b>{{ p.number }}</b></a></td><td>{{ ddmy(p.orderDate) }}</td><td>{{ p.supplierName }}</td><td>{{ p.storeName }}</td>
                <td><span class="badge" [class]="'badge ' + badge(p.status)">{{ label(p.status) }}</span></td>
                <td class="r mono">{{ p.lines.length }}</td><td class="r mono">{{ qty(p.orderedQuantity) }}</td><td class="r mono" [class.pos]="p.receivedQuantity > 0">{{ qty(p.receivedQuantity) }}</td>
                <td class="r mono">{{ p.total | number:'1.2-2' }}</td><td [class.neg]="isLate(p)">{{ p.expectedDate ? ddmy(p.expectedDate) : '—' }}</td><td class="mono">{{ p.reference || '—' }}</td>
                <td class="ar actions">
                  <button class="btn btn-mini btn-s" (click)="openView(p)">{{ 'view_details' | t : 'View' }}</button>
                  @if (canManage()) {
                    @if (p.status === 'Draft') {
                      <button class="icon-btn" title="Edit" (click)="openEdit(p)">✎</button>
                      <button class="btn btn-mini btn-p" (click)="act(p, 'submit')">{{ 'submit_order' | t : 'Submit' }}</button>
                    }
                    @if (p.status === 'Ordered' || p.status === 'PartiallyReceived') {
                      <button class="btn btn-mini btn-p" (click)="openReceive(p)">{{ 'receive_goods' | t : 'Receive goods' }}</button>
                      <button class="btn btn-mini btn-s" (click)="act(p, 'close')" title="Accept a short delivery: no more goods are expected">{{ 'close_order' | t : 'Close' }}</button>
                    }
                    @if (p.status === 'Draft' || p.status === 'Ordered') { <button class="btn btn-mini red" (click)="act(p, 'cancel')">{{ 'cancel_order' | t : 'Cancel' }}</button> }
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="12" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    <!-- New / edit (draft) -->
    @if (dlg() === 'edit') {
      <div class="as-overlay" (click)="dlg.set(null)">
        <div class="as-dlg wide" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ editId ? ('Edit purchase order' | t : 'Edit purchase order') : ('new_purchase_order' | t : 'New purchase order') }}</h2><button class="icon-btn" (click)="dlg.set(null)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
              <div class="field"><label>{{ 'distributor' | t : 'Distributor' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.supplierId" [options]="supplierOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'Deliver to store' | t : 'Deliver to store' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.storeId" [options]="storeOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'Order date' | t : 'Order date' }} <span class="req">*</span></label><app-date-input [(ngModel)]="f.orderDate"></app-date-input></div>
              <div class="field"><label>{{ 'Expected delivery' | t : 'Expected delivery' }}</label><app-date-input [(ngModel)]="f.expectedDate" [min]="f.orderDate"></app-date-input></div>
              <div class="field"><label>{{ 'reference' | t : 'Reference' }}</label><input class="input" [(ngModel)]="f.reference" maxlength="80" placeholder="quotation / offer no."></div>
              <div class="field" style="grid-column:span 3"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="1000"></div>
            </div>
            <div class="sec-title">{{ 'Lines' | t : 'Lines' }}</div>
            <div class="grid-scroll"><table class="lines">
              <thead><tr><th style="width:45%">{{ 'item' | t : 'Item' }}</th><th class="r">{{ 'inv_quantity' | t : 'Quantity' }}</th><th>{{ 'unit' | t : 'Unit' }}</th><th class="r">{{ 'unit_price' | t : 'Unit price' }}</th><th class="r">{{ 'total' | t : 'Total' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th></th></tr></thead>
              <tbody>
                @for (l of f.lines; track $index; let i = $index) {
                  <tr>
                    <td><app-filter-select [(ngModel)]="l.itemId" [options]="itemOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></td>
                    <td class="r"><input class="input" type="number" min="0" step="0.001" [(ngModel)]="l.orderedQuantity" style="width:110px;text-align:right"></td>
                    <td>{{ unitOf(l.itemId) }}</td>
                    <td class="r"><input class="input" type="number" min="0" step="0.01" [(ngModel)]="l.unitPrice" style="width:110px;text-align:right"></td>
                    <td class="r mono">{{ (l.orderedQuantity || 0) * (l.unitPrice || 0) | number:'1.2-2' }}</td>
                    <td><input class="input" [(ngModel)]="l.notes" maxlength="300"></td>
                    <td class="ar"><button class="icon-btn del" (click)="f.lines.splice(i, 1)">🗑</button></td>
                  </tr>
                }
              </tbody>
              <tfoot><tr><td colspan="4">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ draftTotal() | number:'1.2-2' }}</td><td colspan="2"><button class="btn btn-mini btn-s" (click)="addLine()">+ {{ 'Add line' | t : 'Add line' }}</button></td></tr></tfoot>
            </table></div>
          </div>
          <div class="as-dlg-foot">
            @if (missing().length) { <span class="hint" style="margin:0 auto 0 0">{{ 'missing_fields' | t : 'Missing' }}: {{ missing().join(', ') }}</span> }
            <button class="btn btn-s" (click)="dlg.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-s" [disabled]="busy()" (click)="save(false)">{{ 'Save as draft' | t : 'Save as draft' }}</button>
            @if (!editId) { <button class="btn btn-p" [disabled]="busy()" (click)="save(true)">{{ 'Save & submit' | t : 'Save & submit' }}</button> }
          </div>
        </div>
      </div>
    }

    <!-- Receive goods -->
    @if (dlg() === 'receive' && current(); as p) {
      <div class="as-overlay" (click)="dlg.set(null)">
        <div class="as-dlg wide" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ 'receive_goods' | t : 'Receive goods' }} — {{ p.number }} · {{ p.supplierName }} → {{ p.storeName }}</h2><button class="icon-btn" (click)="dlg.set(null)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
              <div class="field"><label>{{ 'Received date' | t : 'Received date' }} <span class="req">*</span></label><app-date-input [(ngModel)]="r.receivedDate" [min]="p.orderDate"></app-date-input></div>
              <div class="field"><label>{{ 'delivery_note' | t : 'Delivery note' }}</label><input class="input" [(ngModel)]="r.deliveryNote" maxlength="80"></div>
              <div class="field"><label>{{ 'invoice_number' | t : 'Invoice No.' }}</label><input class="input" [(ngModel)]="r.invoiceNumber" maxlength="80"></div>
              <div class="field"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="r.notes" maxlength="1000"></div>
            </div>
            <div class="hint">{{ 'Enter what actually arrived per line: the quantity is validated against the outstanding amount, and every quantity needs the lot number and its expiry date. Leave a line empty to skip it; use "+ lot" when one line arrived in two lots.' | t : 'Enter what actually arrived per line: the quantity is validated against the outstanding amount, and every quantity needs the lot number and its expiry date. Leave a line empty to skip it; use "+ lot" when one line arrived in two lots.' }}</div>
            <div class="grid-scroll"><table class="lines" style="margin-top:8px">
              <thead><tr><th>{{ 'item' | t : 'Item' }}</th><th class="r">{{ 'outstanding' | t : 'Outstanding' }}</th><th class="r">{{ 'Received qty' | t : 'Received qty' }}</th><th>{{ 'lot_number' | t : 'Lot No.' }}</th><th>{{ 'expiry_date' | t : 'Expiry' }}</th><th class="r">{{ 'unit_cost' | t : 'Unit cost' }}</th><th></th></tr></thead>
              <tbody>
                @for (l of r.lines; track $index; let i = $index) {
                  <tr>
                    <td><b>{{ l.itemCode }}</b> · {{ l.itemName }}</td><td class="r mono">{{ qty(l.outstanding) }} {{ l.unit }}</td>
                    <td class="r"><input class="input" type="number" min="0" step="0.001" [(ngModel)]="l.quantity" style="width:110px;text-align:right"></td>
                    <td><input class="input" [(ngModel)]="l.lotNumber" maxlength="64" style="width:150px"></td>
                    <td><app-date-input [(ngModel)]="l.expiryDate" [min]="r.receivedDate"></app-date-input></td>
                    <td class="r"><input class="input" type="number" min="0" step="0.01" [(ngModel)]="l.unitCost" style="width:100px;text-align:right" placeholder="order price"></td>
                    <td class="ar"><button class="btn btn-mini btn-s" (click)="splitLot(l, i)" title="This line arrived in more than one lot">+ lot</button></td>
                  </tr>
                }
              </tbody>
            </table></div>
          </div>
          <div class="as-dlg-foot">
            @if (receiveMissing().length) { <span class="hint" style="margin:0 auto 0 0">{{ 'missing_fields' | t : 'Missing' }}: {{ receiveMissing().join(', ') }}</span> }
            <button class="btn btn-s" (click)="dlg.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy()" (click)="saveReceipt()">{{ 'Record receipt' | t : 'Record receipt' }}</button>
          </div>
        </div>
      </div>
    }

    <!-- View -->
    @if (dlg() === 'view' && current(); as p) {
      <div class="as-overlay" (click)="dlg.set(null)">
        <div class="as-dlg wide" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ p.number }} <span class="badge" [class]="'badge ' + badge(p.status)">{{ label(p.status) }}</span></h2>
            <div><button class="btn btn-mini btn-s" (click)="print(p)">{{ 'Print' | t : 'Print' }}</button> <button class="icon-btn" (click)="dlg.set(null)">✕</button></div></div>
          <div class="as-dlg-body">
            <div class="kv">
              <div><div class="k">{{ 'distributor' | t : 'Distributor' }}</div><div class="v">{{ p.supplierName }}</div></div>
              <div><div class="k">{{ 'Deliver to store' | t : 'Deliver to store' }}</div><div class="v">{{ p.storeName }}</div></div>
              <div><div class="k">{{ 'Order date' | t : 'Order date' }}</div><div class="v">{{ ddmy(p.orderDate) }}</div></div>
              <div><div class="k">{{ 'Expected delivery' | t : 'Expected delivery' }}</div><div class="v">{{ p.expectedDate ? ddmy(p.expectedDate) : '—' }}</div></div>
              <div><div class="k">{{ 'reference' | t : 'Reference' }}</div><div class="v">{{ p.reference || '—' }}</div></div>
              <div><div class="k">{{ 'Submitted' | t : 'Submitted' }}</div><div class="v">{{ p.orderedOn ? ddmy(p.orderedOn) : '—' }}</div></div>
              <div><div class="k">{{ 'Closed' | t : 'Closed' }}</div><div class="v">{{ p.closedOn ? ddmy(p.closedOn) : '—' }}</div></div>
              <div><div class="k">{{ 'Created by' | t : 'Created by' }}</div><div class="v">{{ p.createdBy }}</div></div>
            </div>
            @if (p.notes) { <div class="hint" style="margin-top:8px">{{ p.notes }}</div> }
            <div class="sec-title">{{ 'Lines' | t : 'Lines' }}</div>
            <div class="grid-scroll"><table class="lines">
              <thead><tr><th>{{ 'item' | t : 'Item' }}</th><th class="r">{{ 'ordered' | t : 'Ordered' }}</th><th class="r">{{ 'received' | t : 'Received' }}</th><th class="r">{{ 'outstanding' | t : 'Outstanding' }}</th><th class="r">{{ 'unit_price' | t : 'Unit price' }}</th><th class="r">{{ 'total' | t : 'Total' }}</th><th>{{ 'notes' | t : 'Notes' }}</th></tr></thead>
              <tbody>
                @for (l of p.lines; track l.id) {
                  <tr><td><b>{{ l.itemCode }}</b> · {{ l.itemName }}</td><td class="r mono">{{ qty(l.orderedQuantity) }} {{ l.unit }}</td><td class="r mono pos">{{ qty(l.receivedQuantity) }}</td><td class="r mono" [class.neg]="l.outstanding > 0">{{ qty(l.outstanding) }}</td>
                    <td class="r mono">{{ l.unitPrice | number:'1.2-2' }}</td><td class="r mono">{{ l.lineTotal | number:'1.2-2' }}</td><td>{{ l.notes || '' }}</td></tr>
                }
              </tbody>
              <tfoot><tr><td colspan="5">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ p.total | number:'1.2-2' }}</td><td></td></tr></tfoot>
            </table></div>
            <div class="sec-title">{{ 'receipts' | t : 'Receipts' }} ({{ p.receipts.length }})</div>
            @if (!p.receipts.length) { <div class="muted small">{{ 'Nothing received yet.' | t : 'Nothing received yet.' }}</div> }
            @for (g of p.receipts; track g.id) {
              <div style="margin-bottom:10px">
                <div><b>{{ g.number }}</b> · {{ ddmy(g.receivedDate) }} · {{ g.receivedBy }} @if (g.deliveryNote) { · DN {{ g.deliveryNote }} } @if (g.invoiceNumber) { · Inv {{ g.invoiceNumber }} } @if (g.notes) { · <i>{{ g.notes }}</i> }</div>
                <div class="grid-scroll"><table class="lines" style="width:auto;min-width:70%">
                  <thead><tr><th>{{ 'item' | t : 'Item' }}</th><th class="r">{{ 'inv_quantity' | t : 'Quantity' }}</th><th>{{ 'lot_number' | t : 'Lot No.' }}</th><th>{{ 'expiry_date' | t : 'Expiry' }}</th><th class="r">{{ 'unit_cost' | t : 'Unit cost' }}</th></tr></thead>
                  <tbody>@for (l of g.lines; track l.id) { <tr><td>{{ l.itemCode }} · {{ l.itemName }}</td><td class="r mono">{{ qty(l.quantity) }} {{ l.unit }}</td><td class="mono">{{ l.lotNumber }}</td><td>{{ l.expiryDate ? ddmy(l.expiryDate) : '—' }}</td><td class="r mono">{{ l.unitCost | number:'1.2-2' }}</td></tr> }</tbody>
                </table></div>
              </div>
            }
          </div>
          <div class="as-dlg-foot"><button class="btn btn-s" (click)="dlg.set(null)">{{ 'close' | t : 'Close' }}</button></div>
        </div>
      </div>
    }
  `,
  styles: [INV_STYLES],
})
export class PurchaseOrdersComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly ddmy = ddmy; readonly qty = qty; readonly badge = badge; readonly label = label;

  readonly rows = signal<PurchaseOrderDto[]>([]);
  readonly suppliers = signal<SupplierDto[]>([]);
  readonly stores = signal<StoreDto[]>([]);
  readonly items = signal<InventoryItemDto[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal<'edit' | 'receive' | 'view' | null>(null);
  readonly current = signal<PurchaseOrderDto | null>(null);
  from = firstOfMonth(); to = localToday(); status = ''; supplierId = ''; storeId = '';
  editId: string | null = null;
  f = this.blank();
  r = { receivedDate: localToday(), deliveryNote: '', invoiceNumber: '', notes: '', lines: [] as ReceiptDraft[] };

  readonly statusOptions: Opt[] = PO_STATUSES.map((s) => ({ value: s, label: label(s) }));
  readonly supplierOptions = computed<Opt[]>(() => this.suppliers().map((s) => ({ value: s.id, label: s.name })));
  readonly storeOptions = computed<Opt[]>(() => this.stores().map((s) => ({ value: s.id, label: `${s.name} (${s.branch})` })));
  readonly itemOptions = computed<Opt[]>(() => this.items().map((i) => ({ value: i.id, label: `${i.code} · ${i.name}` })));
  readonly k = computed(() => {
    const r = this.rows();
    return { open: r.filter((p) => p.status === 'Ordered' || p.status === 'PartiallyReceived').length, received: r.filter((p) => p.status === 'Received').length,
      total: r.filter((p) => p.status !== 'Cancelled').reduce((a, p) => a + p.total, 0) };
  });

  constructor() {
    this.api.get<SupplierDto[]>('/inventory/suppliers').subscribe({ next: (r) => this.suppliers.set(r), error: () => {} });
    this.api.get<StoreDto[]>('/inventory/stores').subscribe({ next: (r) => this.stores.set(r), error: () => {} });
    this.api.get<InventoryItemDto[]>('/inventory/items').subscribe({ next: (r) => this.items.set(r), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageInventory'); }
  isLate(p: PurchaseOrderDto): boolean { return !!p.expectedDate && p.expectedDate < localToday() && (p.status === 'Ordered' || p.status === 'PartiallyReceived'); }
  unitOf(itemId: string): string { return this.items().find((i) => i.id === itemId)?.unit ?? ''; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.status) params['status'] = this.status;
    if (this.supplierId) params['supplierId'] = this.supplierId;
    if (this.storeId) params['storeId'] = this.storeId;
    this.api.get<PurchaseOrderDto[]>('/inventory/purchase-orders', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  // ---- create / edit ----
  private blank() { return { supplierId: '', storeId: '', orderDate: localToday(), expectedDate: '', reference: '', notes: '', lines: [{ itemId: '', orderedQuantity: null, unitPrice: null, notes: '' }] as LineDraft[] }; }
  addLine(): void { this.f.lines.push({ itemId: '', orderedQuantity: null, unitPrice: null, notes: '' }); }
  draftTotal(): number { return this.f.lines.reduce((a, l) => a + (l.orderedQuantity || 0) * (l.unitPrice || 0), 0); }
  openNew(): void { this.editId = null; this.f = this.blank(); this.dlg.set('edit'); }
  openEdit(p: PurchaseOrderDto): void {
    this.editId = p.id;
    this.f = { supplierId: p.supplierId, storeId: p.storeId, orderDate: p.orderDate, expectedDate: p.expectedDate ?? '', reference: p.reference ?? '', notes: p.notes ?? '',
      lines: p.lines.map((l) => ({ itemId: l.itemId, orderedQuantity: l.orderedQuantity, unitPrice: l.unitPrice, notes: l.notes ?? '' })) };
    this.dlg.set('edit');
  }
  missing(): string[] {
    const m: string[] = [];
    if (!this.f.supplierId) m.push('Distributor');
    if (!this.f.storeId) m.push('Store');
    if (!this.f.orderDate) m.push('Order date');
    if (this.f.expectedDate && this.f.expectedDate < this.f.orderDate) m.push('Expected date (before order date)');
    if (!this.f.lines.length) m.push('Lines');
    if (this.f.lines.some((l) => !l.itemId)) m.push('Item on every line');
    if (this.f.lines.some((l) => !(Number(l.orderedQuantity) > 0))) m.push('Quantity > 0');
    if (this.f.lines.some((l) => l.unitPrice === null || Number(l.unitPrice) < 0)) m.push('Unit price');
    const ids = this.f.lines.map((l) => l.itemId).filter(Boolean);
    if (new Set(ids).size !== ids.length) m.push('Duplicate item');
    return m;
  }
  save(submit: boolean): void {
    const missing = this.missing();
    if (missing.length) { this.toast.warning(`Please fill: ${missing.join(', ')}`); return; }
    this.busy.set(true);
    const body = { supplierId: this.f.supplierId, storeId: this.f.storeId, orderDate: this.f.orderDate, expectedDate: this.f.expectedDate || null, reference: this.f.reference || null, notes: this.f.notes || null,
      lines: this.f.lines.map((l) => ({ itemId: l.itemId, orderedQuantity: Number(l.orderedQuantity), unitPrice: Number(l.unitPrice), notes: l.notes || null })), submit };
    const req = this.editId ? this.api.put(`/inventory/purchase-orders/${this.editId}`, body) : this.api.post('/inventory/purchase-orders', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(null); this.toast.success(submit ? 'Purchase order submitted.' : 'Purchase order saved.'); this.load(); }, error: () => this.busy.set(false) });
  }

  // ---- status ----
  act(p: PurchaseOrderDto, action: 'submit' | 'close' | 'cancel'): void {
    const q = action === 'submit' ? `Submit ${p.number} to ${p.supplierName}?` : action === 'close' ? `Close ${p.number}? The outstanding quantities will no longer be expected.` : `Cancel ${p.number}?`;
    if (!confirm(q)) return;
    this.api.post(`/inventory/purchase-orders/${p.id}/${action}`).subscribe({ next: () => { this.toast.success(`${p.number} ${action === 'submit' ? 'submitted' : action === 'close' ? 'closed' : 'cancelled'}.`); this.load(); } });
  }

  // ---- receive ----
  openReceive(p: PurchaseOrderDto): void {
    this.api.get<PurchaseOrderDto>(`/inventory/purchase-orders/${p.id}`).subscribe({ next: (d) => {
      this.current.set(d);
      this.r = { receivedDate: localToday() < d.orderDate ? d.orderDate : localToday(), deliveryNote: '', invoiceNumber: '', notes: '',
        lines: d.lines.filter((l) => l.outstanding > 0).map((l) => ({ orderLineId: l.id, itemCode: l.itemCode, itemName: l.itemName, unit: l.unit, outstanding: l.outstanding, quantity: null, lotNumber: '', expiryDate: '', unitCost: null })) };
      this.dlg.set('receive');
    } });
  }
  splitLot(l: ReceiptDraft, i: number): void { this.r.lines.splice(i + 1, 0, { ...l, quantity: null, lotNumber: '', expiryDate: '', unitCost: null }); }
  receiveMissing(): string[] {
    const m: string[] = [];
    if (!this.r.receivedDate) m.push('Received date');
    const active = this.r.lines.filter((l) => l.quantity !== null && l.quantity !== undefined && String(l.quantity) !== '');
    if (!active.length) m.push('At least one received quantity');
    if (active.some((l) => !(Number(l.quantity) > 0))) m.push('Quantity > 0');
    if (active.some((l) => !l.lotNumber.trim())) m.push('Lot No.');
    if (active.some((l) => l.expiryDate && l.expiryDate < this.r.receivedDate)) m.push('Expiry (already expired)');
    if (active.some((l) => l.unitCost !== null && Number(l.unitCost) < 0)) m.push('Unit cost');
    // Same order line: the sum of the entered lots must not exceed the outstanding.
    const byLine = new Map<string, number>();
    for (const l of active) byLine.set(l.orderLineId, (byLine.get(l.orderLineId) ?? 0) + Number(l.quantity));
    for (const [id, sum] of byLine) { const out = this.r.lines.find((l) => l.orderLineId === id)?.outstanding ?? 0; if (sum > out + 1e-9) m.push(`Over-receipt (${this.r.lines.find((l) => l.orderLineId === id)?.itemCode})`); }
    return m;
  }
  saveReceipt(): void {
    const p = this.current(); if (!p) return;
    const missing = this.receiveMissing();
    if (missing.length) { this.toast.warning(`Please check: ${missing.join(', ')}`); return; }
    this.busy.set(true);
    const lines = this.r.lines.filter((l) => l.quantity !== null && String(l.quantity) !== '')
      .map((l) => ({ orderLineId: l.orderLineId, quantity: Number(l.quantity), lotNumber: l.lotNumber.trim(), expiryDate: l.expiryDate || null, unitCost: l.unitCost === null || String(l.unitCost) === '' ? null : Number(l.unitCost) }));
    this.api.post(`/inventory/purchase-orders/${p.id}/receive`, { receivedDate: this.r.receivedDate, deliveryNote: this.r.deliveryNote || null, invoiceNumber: this.r.invoiceNumber || null, notes: this.r.notes || null, lines })
      .subscribe({ next: () => { this.busy.set(false); this.dlg.set(null); this.toast.success('Goods received into stock.'); this.load(); }, error: () => this.busy.set(false) });
  }

  // ---- view / print ----
  openView(p: PurchaseOrderDto): void { this.api.get<PurchaseOrderDto>(`/inventory/purchase-orders/${p.id}`).subscribe({ next: (d) => { this.current.set(d); this.dlg.set('view'); } }); }
  print(p: PurchaseOrderDto): void {
    const h = (v: unknown) => escHtml(v as string);
    const lines = p.lines.map((l, i) => `<tr><td>${i + 1}</td><td>${h(l.itemCode)}</td><td>${h(l.itemName)}</td><td style="text-align:right">${qty(l.orderedQuantity)} ${h(l.unit)}</td><td style="text-align:right">${money(l.unitPrice).toFixed(2)}</td><td style="text-align:right">${money(l.lineTotal).toFixed(2)}</td><td>${h(l.notes ?? '')}</td></tr>`).join('');
    const html = `<p><b>Distributor:</b> ${h(p.supplierName)} &nbsp; <b>Deliver to:</b> ${h(p.storeName)} &nbsp; <b>Order date:</b> ${ddmy(p.orderDate)} &nbsp; <b>Expected:</b> ${p.expectedDate ? ddmy(p.expectedDate) : '—'} &nbsp; <b>Reference:</b> ${h(p.reference ?? '—')} &nbsp; <b>Status:</b> ${label(p.status)}</p>
      <table><thead><tr><th>#</th><th>Code</th><th>Item</th><th>Quantity</th><th>Unit price</th><th>Total</th><th>Notes</th></tr></thead><tbody>${lines}</tbody>
      <tfoot><tr><td colspan="5" style="text-align:right"><b>Total</b></td><td style="text-align:right"><b>${money(p.total).toFixed(2)}</b></td><td></td></tr></tfoot></table>
      ${p.notes ? `<p><b>Notes:</b> ${h(p.notes)}</p>` : ''}<p style="margin-top:32px">Prepared by: ${h(p.createdBy)} &nbsp;&nbsp;&nbsp; Approved by: ____________________</p>`;
    printDoc(`Purchase Order ${p.number}`, html);
  }

  exportExcel(): void {
    exportXlsx(`purchase-orders-${localToday()}.xlsx`, ['Number', 'Order date', 'Distributor', 'Store', 'Status', 'Lines', 'Ordered qty', 'Received qty', 'Total', 'Expected', 'Reference', 'Created by'],
      this.rows().map((p) => [p.number, ddmy(p.orderDate), p.supplierName, p.storeName, label(p.status), p.lines.length, p.orderedQuantity, p.receivedQuantity, money(p.total), ddmy(p.expectedDate), p.reference ?? '', p.createdBy]));
  }
}
