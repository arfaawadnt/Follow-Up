import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, exportXlsx, localToday } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';
import { StockLotDto, StockTransferDto, StoreDto } from '../../core/models';
import { INV_STYLES, Opt, TRANSFER_STATUSES, badge, firstOfMonth, label, qty } from './inventory.util';

type TransferLineDraft = { lotId: string; quantity: number | null };

/**
 * Stock Transfers — moving lots between stores. Creating a transfer takes the quantities out of the source store at
 * once (in transit); the destination confirms the counts line by line (what arrived lands in its lots, a shortfall stays
 * on the transfer); an in-transit transfer can be cancelled, which returns the stock to the source.
 */
@Component({
  selector: 'app-stock-transfers',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_transfers' | t : 'Stock Transfers' }}</div><h1>{{ 'inv_transfers' | t : 'Stock Transfers' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'new_transfer' | t : 'New transfer' }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
      </div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr) auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'status' | t : 'Status' }}</label><app-filter-select [(ngModel)]="status" [options]="statusOptions" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'store' | t : 'Store' }}</label><app-filter-select [(ngModel)]="storeId" [options]="storeOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th></th><th>{{ 'Number' | t : 'Number' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'from_store' | t : 'From store' }}</th><th>{{ 'to_store' | t : 'To store' }}</th><th>{{ 'status' | t : 'Status' }}</th>
            <th class="r">{{ 'Lines' | t : 'Lines' }}</th><th class="r">{{ 'Dispatched' | t : 'Dispatched' }}</th><th class="r">{{ 'received' | t : 'Received' }}</th><th>{{ 'Received on' | t : 'Received on' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th>
          </tr></thead>
          <tbody>
            @for (t of rows(); track t.id) {
              <tr class="clickable" (click)="open.set(open() === t.id ? null : t.id)">
                <td>{{ open() === t.id ? '▾' : '▸' }}</td><td class="mono"><b>{{ t.number }}</b></td><td>{{ ddmy(t.date) }}</td><td>{{ t.fromStoreName }}</td><td>{{ t.toStoreName }}</td>
                <td><span class="badge" [class]="'badge ' + badge(t.status)">{{ label(t.status) }}</span></td>
                <td class="r mono">{{ t.lines.length }}</td><td class="r mono">{{ qty(sum(t, 'quantity')) }}</td><td class="r mono" [class.neg]="shortfall(t) > 0">{{ t.status === 'Received' ? qty(sum(t, 'receivedQuantity')) : '' }}</td>
                <td>{{ t.receivedDate ? ddmy(t.receivedDate) : '—' }}</td><td>{{ t.notes || '' }} @if (t.receiveNotes) { <i class="muted">· {{ t.receiveNotes }}</i> }</td>
                <td class="ar actions" (click)="$event.stopPropagation()">
                  @if (canManage() && t.status === 'InTransit') {
                    @if (t.canReceive) { <button class="btn btn-mini btn-p" (click)="openReceive(t)">{{ 'confirm_receipt' | t : 'Confirm receipt' }}</button> }
                    <button class="btn btn-mini red" (click)="cancel(t)">{{ 'cancel' | t : 'Cancel' }}</button>
                  }
                </td>
              </tr>
              @if (open() === t.id) {
                <tr class="sub"><td></td><td colspan="11">
                  <div class="grid-scroll"><table class="lines" style="width:auto;min-width:70%">
                    <thead><tr><th>{{ 'item' | t : 'Item' }}</th><th>{{ 'lot_number' | t : 'Lot No.' }}</th><th>{{ 'expiry_date' | t : 'Expiry' }}</th><th class="r">{{ 'Dispatched' | t : 'Dispatched' }}</th><th class="r">{{ 'received' | t : 'Received' }}</th><th class="r">{{ 'Shortfall' | t : 'Shortfall' }}</th></tr></thead>
                    <tbody>
                      @for (l of t.lines; track l.id) {
                        <tr><td><b>{{ l.itemCode }}</b> · {{ l.itemName }}</td><td class="mono">{{ l.lotNumber }}</td><td>{{ l.expiryDate ? ddmy(l.expiryDate) : '—' }}</td><td class="r mono">{{ qty(l.quantity) }} {{ l.unit }}</td>
                          <td class="r mono">{{ l.receivedQuantity !== null ? qty(l.receivedQuantity) : '—' }}</td><td class="r mono" [class.neg]="l.receivedQuantity !== null && l.quantity - l.receivedQuantity > 0">{{ l.receivedQuantity !== null && l.quantity - l.receivedQuantity > 0 ? qty(l.quantity - l.receivedQuantity) : '' }}</td></tr>
                      }
                    </tbody>
                  </table></div>
                </td></tr>
              }
            } @empty { <tr><td colspan="12" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    <!-- New transfer -->
    @if (dlg() === 'new') {
      <div class="as-overlay" (click)="dlg.set(null)">
        <div class="as-dlg wide" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ 'new_transfer' | t : 'New transfer' }}</h2><button class="icon-btn" (click)="dlg.set(null)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
              <div class="field"><label>{{ 'from_store' | t : 'From store' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.fromStoreId" (ngModelChange)="loadSourceLots()" [options]="storeOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'to_store' | t : 'To store' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.toStoreId" [options]="storeOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'date' | t : 'Date' }} <span class="req">*</span></label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="1000"></div>
            </div>
            <div class="sec-title">{{ 'Lots to move' | t : 'Lots to move' }}</div>
            @if (!f.fromStoreId) { <div class="hint">{{ 'Choose the source store to list its lots.' | t : 'Choose the source store to list its lots.' }}</div> }
            <div class="grid-scroll"><table class="lines">
              <thead><tr><th style="width:60%">{{ 'Lot (item · lot · expiry · on hand)' | t : 'Lot (item · lot · expiry · on hand)' }}</th><th class="r">{{ 'inv_quantity' | t : 'Quantity' }}</th><th></th></tr></thead>
              <tbody>
                @for (l of f.lines; track $index; let i = $index) {
                  <tr>
                    <td><app-filter-select [(ngModel)]="l.lotId" [options]="lotOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></td>
                    <td class="r"><input class="input" type="number" min="0" step="0.001" [(ngModel)]="l.quantity" style="width:120px;text-align:right"> <span class="muted small">/ {{ qty(onHand(l.lotId)) }}</span></td>
                    <td class="ar"><button class="icon-btn del" (click)="f.lines.splice(i, 1)">🗑</button></td>
                  </tr>
                }
              </tbody>
              <tfoot><tr><td colspan="3"><button class="btn btn-mini btn-s" (click)="f.lines.push({ lotId: '', quantity: null })">+ {{ 'Add line' | t : 'Add line' }}</button></td></tr></tfoot>
            </table></div>
          </div>
          <div class="as-dlg-foot">
            @if (missing().length) { <span class="hint" style="margin:0 auto 0 0">{{ 'missing_fields' | t : 'Missing' }}: {{ missing().join(', ') }}</span> }
            <button class="btn btn-s" (click)="dlg.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy()" (click)="save()">{{ 'Dispatch' | t : 'Dispatch' }}</button>
          </div>
        </div>
      </div>
    }

    <!-- Confirm receipt -->
    @if (dlg() === 'receive' && current(); as t) {
      <div class="as-overlay" (click)="dlg.set(null)">
        <div class="as-dlg wide" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ 'confirm_receipt' | t : 'Confirm receipt' }} — {{ t.number }} · {{ t.fromStoreName }} → {{ t.toStoreName }}</h2><button class="icon-btn" (click)="dlg.set(null)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 3fr;gap:12px">
              <div class="field"><label>{{ 'Received date' | t : 'Received date' }} <span class="req">*</span></label><app-date-input [(ngModel)]="rc.receivedDate" [min]="t.date"></app-date-input></div>
              <div class="field"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="rc.notes" maxlength="1000" placeholder="e.g. one bottle broken in transit"></div>
            </div>
            <div class="hint">{{ 'Count what arrived. A quantity below the dispatched one is recorded as a shortfall on the transfer; only the received quantity enters the destination store.' | t : 'Count what arrived. A quantity below the dispatched one is recorded as a shortfall on the transfer; only the received quantity enters the destination store.' }}</div>
            <div class="grid-scroll"><table class="lines" style="margin-top:8px">
              <thead><tr><th>{{ 'item' | t : 'Item' }}</th><th>{{ 'lot_number' | t : 'Lot No.' }}</th><th>{{ 'expiry_date' | t : 'Expiry' }}</th><th class="r">{{ 'Dispatched' | t : 'Dispatched' }}</th><th class="r">{{ 'Received qty' | t : 'Received qty' }}</th></tr></thead>
              <tbody>
                @for (l of rc.lines; track l.lineId) {
                  <tr><td><b>{{ l.itemCode }}</b> · {{ l.itemName }}</td><td class="mono">{{ l.lotNumber }}</td><td>{{ l.expiryDate ? ddmy(l.expiryDate) : '—' }}</td><td class="r mono">{{ qty(l.quantity) }} {{ l.unit }}</td>
                    <td class="r"><input class="input" type="number" min="0" step="0.001" [max]="l.quantity" [(ngModel)]="l.receivedQuantity" style="width:120px;text-align:right"></td></tr>
                }
              </tbody>
            </table></div>
          </div>
          <div class="as-dlg-foot">
            <button class="btn btn-s" (click)="dlg.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy() || !rc.receivedDate || receiveInvalid()" (click)="saveReceive()">{{ 'confirm_receipt' | t : 'Confirm receipt' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [INV_STYLES],
})
export class StockTransfersComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly ddmy = ddmy; readonly qty = qty; readonly badge = badge; readonly label = label;

  readonly rows = signal<StockTransferDto[]>([]);
  readonly stores = signal<StoreDto[]>([]);
  readonly sourceLots = signal<StockLotDto[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly open = signal<string | null>(null);
  readonly dlg = signal<'new' | 'receive' | null>(null);
  readonly current = signal<StockTransferDto | null>(null);
  from = firstOfMonth(); to = localToday(); status = ''; storeId = '';
  f = { fromStoreId: '', toStoreId: '', date: localToday(), notes: '', lines: [{ lotId: '', quantity: null }] as TransferLineDraft[] };
  rc = { receivedDate: localToday(), notes: '', lines: [] as { lineId: string; itemCode: string; itemName: string; unit: string; lotNumber: string; expiryDate: string | null; quantity: number; receivedQuantity: number | null }[] };

  readonly statusOptions: Opt[] = TRANSFER_STATUSES.map((s) => ({ value: s, label: label(s) }));
  readonly storeOptions = computed<Opt[]>(() => this.stores().map((s) => ({ value: s.id, label: `${s.name} (${s.branch})` })));
  readonly lotOptions = computed<Opt[]>(() => this.sourceLots().map((l) => ({ value: l.id, label: `${l.itemCode} · ${l.itemName} · lot ${l.lotNumber}${l.expiryDate ? ' · exp ' + ddmy(l.expiryDate) : ''} · ${qty(l.quantity)} ${l.unit}` })));

  constructor() {
    this.api.get<StoreDto[]>('/inventory/stores').subscribe({ next: (r) => this.stores.set(r), error: () => {} });
    this.load();
  }
  canManage(): boolean { return this.auth.has('ManageInventory'); }
  sum(t: StockTransferDto, k: 'quantity' | 'receivedQuantity'): number { return t.lines.reduce((a, l) => a + (l[k] ?? 0), 0); }
  shortfall(t: StockTransferDto): number { return t.status === 'Received' ? this.sum(t, 'quantity') - this.sum(t, 'receivedQuantity') : 0; }
  onHand(lotId: string): number { return this.sourceLots().find((l) => l.id === lotId)?.quantity ?? 0; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.status) params['status'] = this.status;
    if (this.storeId) params['storeId'] = this.storeId;
    this.api.get<StockTransferDto[]>('/inventory/transfers', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  openNew(): void { this.f = { fromStoreId: '', toStoreId: '', date: localToday(), notes: '', lines: [{ lotId: '', quantity: null }] }; this.sourceLots.set([]); this.dlg.set('new'); }
  loadSourceLots(): void {
    this.f.lines = [{ lotId: '', quantity: null }]; this.sourceLots.set([]);
    if (!this.f.fromStoreId) return;
    this.api.get<StockLotDto[]>('/inventory/lots', { storeId: this.f.fromStoreId }).subscribe({ next: (r) => this.sourceLots.set(r), error: () => {} });
  }
  missing(): string[] {
    const m: string[] = [];
    if (!this.f.fromStoreId) m.push('From store');
    if (!this.f.toStoreId) m.push('To store');
    if (this.f.fromStoreId && this.f.fromStoreId === this.f.toStoreId) m.push('Different stores');
    if (!this.f.date) m.push('Date');
    if (!this.f.lines.length) m.push('Lines');
    if (this.f.lines.some((l) => !l.lotId)) m.push('Lot on every line');
    if (this.f.lines.some((l) => !(Number(l.quantity) > 0))) m.push('Quantity > 0');
    if (this.f.lines.some((l) => l.lotId && Number(l.quantity) > this.onHand(l.lotId))) m.push('Quantity exceeds on hand');
    const ids = this.f.lines.map((l) => l.lotId).filter(Boolean);
    if (new Set(ids).size !== ids.length) m.push('Duplicate lot');
    return m;
  }
  save(): void {
    const missing = this.missing();
    if (missing.length) { this.toast.warning(`Please check: ${missing.join(', ')}`); return; }
    this.busy.set(true);
    this.api.post('/inventory/transfers', { fromStoreId: this.f.fromStoreId, toStoreId: this.f.toStoreId, date: this.f.date, notes: this.f.notes || null, lines: this.f.lines.map((l) => ({ lotId: l.lotId, quantity: Number(l.quantity) })) })
      .subscribe({ next: () => { this.busy.set(false); this.dlg.set(null); this.toast.success('Transfer dispatched: the stock left the source store and is in transit.'); this.load(); }, error: () => this.busy.set(false) });
  }

  openReceive(t: StockTransferDto): void {
    this.current.set(t);
    this.rc = { receivedDate: localToday() < t.date ? t.date : localToday(), notes: '', lines: t.lines.map((l) => ({ lineId: l.id, itemCode: l.itemCode, itemName: l.itemName, unit: l.unit, lotNumber: l.lotNumber, expiryDate: l.expiryDate, quantity: l.quantity, receivedQuantity: l.quantity })) };
    this.dlg.set('receive');
  }
  receiveInvalid(): boolean { return this.rc.lines.some((l) => l.receivedQuantity === null || Number(l.receivedQuantity) < 0 || Number(l.receivedQuantity) > l.quantity); }
  saveReceive(): void {
    const t = this.current(); if (!t) return;
    this.busy.set(true);
    this.api.post(`/inventory/transfers/${t.id}/receive`, { receivedDate: this.rc.receivedDate, notes: this.rc.notes || null, lines: this.rc.lines.map((l) => ({ lineId: l.lineId, receivedQuantity: Number(l.receivedQuantity) })) })
      .subscribe({ next: () => { this.busy.set(false); this.dlg.set(null); this.toast.success(`${t.number} received into ${t.toStoreName}.`); this.load(); }, error: () => this.busy.set(false) });
  }
  cancel(t: StockTransferDto): void {
    const notes = prompt(`Cancel ${t.number}? The dispatched quantities return to ${t.fromStoreName}. Reason (optional):`);
    if (notes === null) return;
    this.api.post(`/inventory/transfers/${t.id}/cancel`, { notes: notes || null }).subscribe({ next: () => { this.toast.success(`${t.number} cancelled; stock returned to ${t.fromStoreName}.`); this.load(); } });
  }

  exportExcel(): void {
    exportXlsx(`stock-transfers-${localToday()}.xlsx`, ['Number', 'Date', 'From store', 'To store', 'Status', 'Item', 'Lot No.', 'Expiry', 'Dispatched', 'Received', 'Received on', 'Notes'],
      this.rows().flatMap((t) => t.lines.map((l) => [t.number, ddmy(t.date), t.fromStoreName, t.toStoreName, label(t.status), `${l.itemCode} · ${l.itemName}`, l.lotNumber, ddmy(l.expiryDate), l.quantity, l.receivedQuantity ?? '', ddmy(t.receivedDate), [t.notes, t.receiveNotes].filter(Boolean).join(' · ')])));
  }
}
