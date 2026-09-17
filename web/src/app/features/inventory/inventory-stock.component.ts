import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';
import { InventoryAlertResult, InventoryDashboard, StockLotDto, StockRow, StoreDto } from '../../core/models';
import { INV_STYLES, ITEM_KINDS, Opt, badge, label, money, qty } from './inventory.util';

/**
 * Stock Overview — the inventory home page: KPIs (items, stock value, low / out of stock, expiring / expired lots, open
 * purchase orders, transfers in transit), the alert list the 06:00 job also pushes as an in-app notification, and the
 * stock grid per item (summed over the visible stores or one store) with the lots (lot number, expiry, quantity) on
 * expand. Everything is scoped to the stores whose branch the caller's role covers.
 */
@Component({
  selector: 'app-inventory-stock',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, FilterSelectComponent, RouterLink],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_stock' | t : 'Stock Overview' }}</div><h1>{{ 'inv_stock' | t : 'Stock Overview' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-s" [disabled]="busy()" (click)="runAlerts()" [title]="'Evaluate stock limits and expiry dates now and push the in-app summary to the inventory users'">{{ busy() ? ('loading' | t : 'Loading…') : ('check_alerts_now' | t : 'Check alerts now') }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    @if (dash(); as d) {
      <div class="kpis" style="grid-template-columns:repeat(6,1fr);margin-bottom:16px">
        <div class="kpi kpi-blue"><div class="lbl">{{ 'items' | t : 'Items' }}</div><div class="val">{{ d.activeItems }}</div><div class="sub">{{ d.lotsWithStock }} {{ 'lots' | t : 'lots' }} · {{ d.stores }} {{ 'stores' | t : 'stores' }}</div></div>
        <div class="kpi kpi-teal"><div class="lbl">{{ 'stock_value' | t : 'Stock value' }}</div><div class="val">{{ d.stockValue | number:'1.0-0' }}</div><div class="sub">EGP</div></div>
        <div class="kpi" [class.kpi-amber]="d.lowStock > 0"><div class="lbl">{{ 'low_stock' | t : 'Low stock' }}</div><div class="val">{{ d.lowStock }}</div><div class="sub">{{ 'items at or below the limit' | t : 'items at or below the limit' }}</div></div>
        <div class="kpi" [class.kpi-red]="d.outOfStock > 0"><div class="lbl">{{ 'out_of_stock' | t : 'Out of stock' }}</div><div class="val">{{ d.outOfStock }}</div><div class="sub">{{ 'items' | t : 'items' }}</div></div>
        <div class="kpi" [class.kpi-orange]="d.expiring > 0"><div class="lbl">{{ 'expiring_soon' | t : 'Expiring soon' }}</div><div class="val">{{ d.expiring }}</div><div class="sub">{{ 'lots' | t : 'lots' }}</div></div>
        <div class="kpi" [class.kpi-red]="d.expired > 0"><div class="lbl">{{ 'expired' | t : 'Expired' }}</div><div class="val">{{ d.expired }}</div><div class="sub">{{ 'lots with stock' | t : 'lots with stock' }}</div></div>
      </div>

      <div style="display:grid;grid-template-columns:2fr 1fr;gap:16px;margin-bottom:16px">
        <div class="card" style="padding:14px 16px">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
            <h3 style="margin:0">{{ 'alerts' | t : 'Alerts' }} <span class="muted small">({{ d.alerts.length }})</span></h3>
            <label class="small"><input type="checkbox" [(ngModel)]="onlyAlerts" (ngModelChange)="load()"> {{ 'only_alerts' | t : 'Only alerts' }} {{ 'in the grid' | t : 'in the grid' }}</label>
          </div>
          @if (d.alerts.length) {
            <div class="alert-list">
              @for (a of d.alerts; track $index) {
                <div class="alert-row"><span class="badge" [class]="'badge ' + badge(a.kind)">{{ alertLabel(a.kind) }}</span><span>{{ a.message }}</span></div>
              }
            </div>
          } @else { <div class="empty" style="padding:12px">{{ 'No alerts: every item is above its limit and no lot is expiring.' | t : 'No alerts: every item is above its limit and no lot is expiring.' }}</div> }
        </div>
        <div class="card" style="padding:14px 16px">
          <h3 style="margin:0 0 8px">{{ 'In progress' | t : 'In progress' }}</h3>
          <div class="kv" style="grid-template-columns:1fr 1fr">
            <div><div class="k">{{ 'inv_purchase_orders' | t : 'Purchase Orders' }}</div><div class="v"><a routerLink="/inventory/purchase-orders">{{ d.openPurchaseOrders }} {{ 'open' | t : 'open' }}</a></div></div>
            <div><div class="k">{{ 'inv_transfers' | t : 'Stock Transfers' }}</div><div class="v"><a routerLink="/inventory/transfers">{{ d.transfersInTransit }} {{ 'in_transit' | t : 'in transit' }}</a></div></div>
          </div>
          <div class="hint" style="margin-top:12px">{{ 'Low stock = total on hand at or below the item minimum; expiring = within the item warning window.' | t : 'Low stock = total on hand at or below the item minimum; expiring = within the item warning window.' }}</div>
        </div>
      </div>
    }

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:2fr 1fr 1fr auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'search' | t : 'Search' }}</label><input class="input" [(ngModel)]="search" (keydown.enter)="load()" placeholder="code / name / catalogue no."></div>
        <div class="field"><label>{{ 'store' | t : 'Store' }}</label><app-filter-select [(ngModel)]="storeId" [options]="storeOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'kind' | t : 'Kind' }}</label><app-filter-select [(ngModel)]="kind" [options]="kindOptions" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th></th><th>{{ 'code' | t : 'Code' }}</th><th>{{ 'item' | t : 'Item' }}</th><th>{{ 'kind' | t : 'Kind' }}</th><th>{{ 'manufacturer' | t : 'Manufacturer' }}</th>
            <th class="r">{{ 'on_hand' | t : 'On hand' }}</th><th>{{ 'unit' | t : 'Unit' }}</th><th class="r">{{ 'min_stock' | t : 'Min. stock' }}</th><th class="r">{{ 'reorder_qty' | t : 'Reorder qty' }}</th>
            <th class="r">{{ 'lots' | t : 'Lots' }}</th><th class="r">{{ 'expiring_soon' | t : 'Expiring' }}</th><th class="r">{{ 'expired' | t : 'Expired' }}</th><th>{{ 'Nearest expiry' | t : 'Nearest expiry' }}</th>
            <th class="r">{{ 'stock_value' | t : 'Value' }}</th><th>{{ 'status' | t : 'Status' }}</th>
          </tr></thead>
          <tbody>
            @for (r of rows(); track r.itemId) {
              <tr class="clickable" (click)="toggle(r)">
                <td>{{ open() === r.itemId ? '▾' : '▸' }}</td><td class="mono"><b>{{ r.code }}</b></td><td>{{ r.name }}</td><td>{{ r.kind }}</td><td>{{ r.manufacturerName }}</td>
                <td class="r mono" [class.neg]="r.isOut || r.isLow"><b>{{ qty(r.onHand) }}</b></td><td>{{ r.unit }}</td>
                <td class="r mono">{{ r.minStock ? qty(r.minStock) : '—' }}</td><td class="r mono">{{ r.reorderQuantity ? qty(r.reorderQuantity) : '—' }}</td>
                <td class="r mono">{{ r.lotCount }}</td><td class="r mono" [class.neg]="r.expiringQuantity > 0">{{ r.expiringQuantity ? qty(r.expiringQuantity) : '' }}</td>
                <td class="r mono" [class.neg]="r.expiredQuantity > 0">{{ r.expiredQuantity ? qty(r.expiredQuantity) : '' }}</td><td>{{ ddmy(r.nearestExpiry) }}</td>
                <td class="r mono">{{ r.value | number:'1.2-2' }}</td>
                <td>
                  @if (r.isOut) { <span class="badge b-bad">{{ 'out_of_stock' | t : 'Out of stock' }}</span> }
                  @else if (r.isLow) { <span class="badge b-warn">{{ 'low_stock' | t : 'Low stock' }}</span> }
                  @if (r.expiredQuantity > 0) { <span class="badge b-bad">{{ 'expired' | t : 'Expired' }}</span> }
                  @else if (r.expiringQuantity > 0) { <span class="badge b-warn">{{ 'expiring_soon' | t : 'Expiring' }}</span> }
                  @if (!r.isOut && !r.isLow && !r.expiredQuantity && !r.expiringQuantity) { <span class="badge b-ok">OK</span> }
                </td>
              </tr>
              @if (open() === r.itemId) {
                <tr class="sub"><td></td><td colspan="14">
                  @if (lotsLoading()) { <span class="muted">{{ 'loading' | t : 'Loading…' }}</span> }
                  @else if (!lots().length) { <span class="muted">{{ 'No lots with stock.' | t : 'No lots with stock.' }}</span> }
                  @else {
                    <div class="grid-scroll"><table class="lines" style="width:auto;min-width:60%">
                      <thead><tr><th>{{ 'store' | t : 'Store' }}</th><th>{{ 'lot_number' | t : 'Lot No.' }}</th><th>{{ 'expiry_date' | t : 'Expiry' }}</th><th class="r">{{ 'inv_quantity' | t : 'Quantity' }}</th><th class="r">{{ 'unit_cost' | t : 'Unit cost' }}</th><th class="r">{{ 'value' | t : 'Value' }}</th><th>{{ 'First received' | t : 'First received' }}</th><th>{{ 'status' | t : 'Status' }}</th></tr></thead>
                      <tbody>
                        @for (l of lots(); track l.id) {
                          <tr><td>{{ l.storeName }}</td><td class="mono"><b>{{ l.lotNumber }}</b></td><td>{{ l.expiryDate ? ddmy(l.expiryDate) : '—' }}</td><td class="r mono">{{ qty(l.quantity) }} {{ l.unit }}</td>
                            <td class="r mono">{{ l.unitCost | number:'1.2-2' }}</td><td class="r mono">{{ l.value | number:'1.2-2' }}</td><td>{{ ddmy(l.firstReceivedOn) }}</td>
                            <td><span class="badge" [class]="'badge ' + badge(l.status)">{{ l.status }}</span></td></tr>
                        }
                      </tbody>
                    </table></div>
                  }
                </td></tr>
              }
            } @empty { <tr><td colspan="15" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) { <tfoot><tr><td colspan="13">{{ 'total' | t : 'Total' }} · {{ rows().length }} {{ 'items' | t : 'items' }}</td><td class="r mono">{{ totalValue() | number:'1.2-2' }}</td><td></td></tr></tfoot> }
        </table></div>
      }
    </div>
  `,
  styles: [INV_STYLES],
})
export class InventoryStockComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly ddmy = ddmy; readonly qty = qty; readonly badge = badge;

  readonly dash = signal<InventoryDashboard | null>(null);
  readonly stores = signal<StoreDto[]>([]);
  readonly rows = signal<StockRow[]>([]);
  readonly lots = signal<StockLotDto[]>([]);
  readonly loading = signal(true);
  readonly lotsLoading = signal(false);
  readonly busy = signal(false);
  readonly open = signal<string | null>(null);
  search = ''; storeId = ''; kind = ''; onlyAlerts = false;

  readonly kindOptions: Opt[] = ITEM_KINDS.map((k) => ({ value: k, label: k }));
  readonly storeOptions = computed<Opt[]>(() => this.stores().map((s) => ({ value: s.id, label: `${s.name} (${s.branch})` })));
  readonly totalValue = computed(() => this.rows().reduce((a, r) => a + r.value, 0));

  constructor() {
    this.api.get<StoreDto[]>('/inventory/stores').subscribe({ next: (r) => this.stores.set(r), error: () => {} });
    this.loadDashboard();
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageInventory'); }
  alertLabel(kind: string): string { return label(kind); }

  loadDashboard(): void { this.api.get<InventoryDashboard>('/inventory/dashboard').subscribe({ next: (d) => this.dash.set(d), error: () => {} }); }
  load(): void {
    this.loading.set(true); this.open.set(null);
    const params: Record<string, string | boolean> = { onlyAlerts: this.onlyAlerts };
    if (this.search.trim()) params['search'] = this.search.trim();
    if (this.storeId) params['storeId'] = this.storeId;
    if (this.kind) params['kind'] = this.kind;
    this.api.get<StockRow[]>('/inventory/stock', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  toggle(r: StockRow): void {
    if (this.open() === r.itemId) { this.open.set(null); return; }
    this.open.set(r.itemId); this.lots.set([]); this.lotsLoading.set(true);
    const params: Record<string, string> = { itemId: r.itemId };
    if (this.storeId) params['storeId'] = this.storeId;
    this.api.get<StockLotDto[]>('/inventory/lots', params).subscribe({ next: (l) => { this.lots.set(l); this.lotsLoading.set(false); }, error: () => this.lotsLoading.set(false) });
  }
  runAlerts(): void {
    this.busy.set(true);
    this.api.post<InventoryAlertResult>('/inventory/alerts/run').subscribe({
      next: (r) => { this.busy.set(false); this.toast.success(`Alerts checked: ${r.lowStock} low, ${r.outOfStock} out, ${r.expiring} expiring, ${r.expired} expired · summary sent to ${r.recipients} user(s).`); this.loadDashboard(); },
      error: () => this.busy.set(false),
    });
  }

  private static readonly HEADER = ['Code', 'Item', 'Kind', 'Manufacturer', 'On hand', 'Unit', 'Min. stock', 'Reorder qty', 'Lots', 'Expiring qty', 'Expired qty', 'Nearest expiry', 'Value', 'Status'];
  private exportRows() {
    return this.rows().map((r) => [r.code, r.name, r.kind, r.manufacturerName, r.onHand, r.unit, r.minStock, r.reorderQuantity, r.lotCount, r.expiringQuantity, r.expiredQuantity,
      ddmy(r.nearestExpiry), money(r.value), r.isOut ? 'Out of stock' : r.isLow ? 'Low stock' : r.expiredQuantity > 0 ? 'Expired lot' : r.expiringQuantity > 0 ? 'Expiring lot' : 'OK']);
  }
  exportExcel(): void { exportXlsx(`stock-${localToday()}.xlsx`, InventoryStockComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Stock Overview (${ddmy(localToday())})`, InventoryStockComponent.HEADER, this.exportRows()); }
}
