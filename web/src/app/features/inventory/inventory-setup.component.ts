import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';
import { ManufacturerDto, RefItem, StoreDto, SupplierDto } from '../../core/models';
import { INV_STYLES, Opt, qty } from './inventory.util';

/**
 * Stores & Partners — the inventory master data behind the documents: stores (each on a Branch — the org-scope dimension
 * of everything inside it), distributors purchase orders are placed with, and the manufacturers items are linked to.
 * All three are deactivated rather than deleted so history keeps resolving.
 */
@Component({
  selector: 'app-inventory-setup',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_setup' | t : 'Stores & Partners' }}</div><h1>{{ 'inv_setup' | t : 'Stores & Partners' }}</h1></div>
    </div>

    <div class="tabs">
      <button [class.on]="tab() === 'stores'" (click)="tab.set('stores')">{{ 'stores' | t : 'Stores' }} ({{ stores().length }})</button>
      <button [class.on]="tab() === 'suppliers'" (click)="tab.set('suppliers')">{{ 'distributors' | t : 'Distributors' }} ({{ suppliers().length }})</button>
      <button [class.on]="tab() === 'manufacturers'" (click)="tab.set('manufacturers')">{{ 'manufacturers' | t : 'Manufacturers' }} ({{ manufacturers().length }})</button>
    </div>

    @switch (tab()) {
      @case ('stores') {
        <div class="card" style="padding:16px">
          @if (canManage()) {
            <div class="frm-grid" style="grid-template-columns:2fr 2fr 2fr auto;gap:12px;align-items:end">
              <div class="field"><label>{{ 'name' | t : 'Name' }} <span class="req">*</span></label><input class="input" [(ngModel)]="ns.name" maxlength="120"></div>
              <div class="field"><label>{{ 'branch' | t : 'Branch' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="ns.branch" [options]="branchOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'Location' | t : 'Location' }}</label><input class="input" [(ngModel)]="ns.location" maxlength="200" placeholder="floor / room / fridge"></div>
              <div class="field"><button class="btn btn-p" style="height:36px" [disabled]="busy() || !ns.name.trim() || !ns.branch" (click)="addStore()">{{ 'add' | t : 'Add' }}</button></div>
            </div>
            <div class="hint">{{ 'A store is visible to users whose role scope covers its branch; every lot, receipt and transfer of the store follows that rule.' | t : 'A store is visible to users whose role scope covers its branch; every lot, receipt and transfer of the store follows that rule.' }}</div>
          }
          <div class="grid-scroll"><table class="grid-table" style="margin-top:14px">
            <thead><tr><th>{{ 'name' | t : 'Name' }}</th><th>{{ 'branch' | t : 'Branch' }}</th><th>{{ 'Location' | t : 'Location' }}</th><th class="r">{{ 'lots' | t : 'Lots' }}</th><th class="r">{{ 'stock_value' | t : 'Stock value' }}</th><th>{{ 'active' | t : 'Active' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th></tr></thead>
            <tbody>
              @for (s of stores(); track s.id) {
                <tr>
                  <td>@if (editId() === s.id) { <input class="input" [(ngModel)]="es.name"> } @else { <b>{{ s.name }}</b> }</td>
                  <td>@if (editId() === s.id) { <app-filter-select [(ngModel)]="es.branch" [options]="branchOptions()"></app-filter-select> } @else { {{ s.branch }} }</td>
                  <td>@if (editId() === s.id) { <input class="input" [(ngModel)]="es.location"> } @else { {{ s.location || '—' }} }</td>
                  <td class="r mono">{{ s.lotCount }}</td><td class="r mono">{{ s.stockValue | number:'1.2-2' }}</td>
                  <td>@if (editId() === s.id) { <input type="checkbox" [(ngModel)]="es.isActive"> } @else { {{ s.isActive ? ('active' | t : 'Active') : ('inactive' | t : 'Inactive') }} }</td>
                  <td class="ar actions">
                    @if (editId() === s.id) {
                      <button class="btn btn-mini btn-p" [disabled]="busy() || !es.name.trim() || !es.branch" (click)="saveStore(s)">{{ 'save' | t : 'Save' }}</button>
                      <button class="btn btn-mini btn-s" (click)="editId.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
                    } @else if (canManage()) { <button class="icon-btn" title="Edit" (click)="editStore(s)">✎</button> }
                  </td>
                </tr>
              } @empty { <tr><td colspan="7" class="empty">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
            </tbody>
          </table></div>
        </div>
      }
      @case ('suppliers') {
        <div class="card" style="padding:16px">
          @if (canManage()) {
            <div class="frm-grid" style="grid-template-columns:2fr 1.5fr 1fr 1.5fr 2fr auto;gap:12px;align-items:end">
              <div class="field"><label>{{ 'name' | t : 'Name' }} <span class="req">*</span></label><input class="input" [(ngModel)]="nd.name" maxlength="150"></div>
              <div class="field"><label>{{ 'Contact person' | t : 'Contact person' }}</label><input class="input" [(ngModel)]="nd.contactPerson" maxlength="120"></div>
              <div class="field"><label>{{ 'phone' | t : 'Phone' }}</label><input class="input" [(ngModel)]="nd.phone" maxlength="40"></div>
              <div class="field"><label>{{ 'email' | t : 'Email' }}</label><input class="input" [(ngModel)]="nd.email" maxlength="150"></div>
              <div class="field"><label>{{ 'address' | t : 'Address' }}</label><input class="input" [(ngModel)]="nd.address" maxlength="300"></div>
              <div class="field"><button class="btn btn-p" style="height:36px" [disabled]="busy() || !nd.name.trim()" (click)="addSupplier()">{{ 'add' | t : 'Add' }}</button></div>
            </div>
          }
          <div class="grid-scroll"><table class="grid-table" style="margin-top:14px">
            <thead><tr><th>{{ 'name' | t : 'Name' }}</th><th>{{ 'Contact person' | t : 'Contact person' }}</th><th>{{ 'phone' | t : 'Phone' }}</th><th>{{ 'email' | t : 'Email' }}</th><th>{{ 'address' | t : 'Address' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th class="r">{{ 'Open orders' | t : 'Open orders' }}</th><th>{{ 'active' | t : 'Active' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th></tr></thead>
            <tbody>
              @for (s of suppliers(); track s.id) {
                <tr>
                  @if (editId() === s.id) {
                    <td><input class="input" [(ngModel)]="ed.name"></td><td><input class="input" [(ngModel)]="ed.contactPerson"></td><td><input class="input" [(ngModel)]="ed.phone"></td>
                    <td><input class="input" [(ngModel)]="ed.email"></td><td><input class="input" [(ngModel)]="ed.address"></td><td><input class="input" [(ngModel)]="ed.notes"></td>
                    <td class="r mono">{{ s.openOrders }}</td><td><input type="checkbox" [(ngModel)]="ed.isActive"></td>
                    <td class="ar actions"><button class="btn btn-mini btn-p" [disabled]="busy() || !ed.name.trim()" (click)="saveSupplier(s)">{{ 'save' | t : 'Save' }}</button> <button class="btn btn-mini btn-s" (click)="editId.set(null)">{{ 'cancel' | t : 'Cancel' }}</button></td>
                  } @else {
                    <td><b>{{ s.name }}</b></td><td>{{ s.contactPerson || '—' }}</td><td class="mono">{{ s.phone || '—' }}</td><td>{{ s.email || '—' }}</td><td>{{ s.address || '—' }}</td><td>{{ s.notes || '—' }}</td>
                    <td class="r mono">{{ s.openOrders }}</td><td>{{ s.isActive ? ('active' | t : 'Active') : ('inactive' | t : 'Inactive') }}</td>
                    <td class="ar actions">@if (canManage()) { <button class="icon-btn" title="Edit" (click)="editSupplier(s)">✎</button> }</td>
                  }
                </tr>
              } @empty { <tr><td colspan="9" class="empty">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
            </tbody>
          </table></div>
        </div>
      }
      @case ('manufacturers') {
        <div class="card" style="padding:16px">
          @if (canManage()) {
            <div class="frm-grid" style="grid-template-columns:2fr 1fr 2fr auto;gap:12px;align-items:end">
              <div class="field"><label>{{ 'name' | t : 'Name' }} <span class="req">*</span></label><input class="input" [(ngModel)]="nm.name" maxlength="150"></div>
              <div class="field"><label>{{ 'Country' | t : 'Country' }}</label><input class="input" [(ngModel)]="nm.country" maxlength="80"></div>
              <div class="field"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="nm.notes" maxlength="500"></div>
              <div class="field"><button class="btn btn-p" style="height:36px" [disabled]="busy() || !nm.name.trim()" (click)="addManufacturer()">{{ 'add' | t : 'Add' }}</button></div>
            </div>
          }
          <div class="grid-scroll"><table class="grid-table" style="margin-top:14px">
            <thead><tr><th>{{ 'name' | t : 'Name' }}</th><th>{{ 'Country' | t : 'Country' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th class="r">{{ 'items' | t : 'Items' }}</th><th>{{ 'active' | t : 'Active' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th></tr></thead>
            <tbody>
              @for (m of manufacturers(); track m.id) {
                <tr>
                  @if (editId() === m.id) {
                    <td><input class="input" [(ngModel)]="em.name"></td><td><input class="input" [(ngModel)]="em.country"></td><td><input class="input" [(ngModel)]="em.notes"></td>
                    <td class="r mono">{{ m.itemCount }}</td><td><input type="checkbox" [(ngModel)]="em.isActive"></td>
                    <td class="ar actions"><button class="btn btn-mini btn-p" [disabled]="busy() || !em.name.trim()" (click)="saveManufacturer(m)">{{ 'save' | t : 'Save' }}</button> <button class="btn btn-mini btn-s" (click)="editId.set(null)">{{ 'cancel' | t : 'Cancel' }}</button></td>
                  } @else {
                    <td><b>{{ m.name }}</b></td><td>{{ m.country || '—' }}</td><td>{{ m.notes || '—' }}</td><td class="r mono">{{ m.itemCount }}</td><td>{{ m.isActive ? ('active' | t : 'Active') : ('inactive' | t : 'Inactive') }}</td>
                    <td class="ar actions">@if (canManage()) { <button class="icon-btn" title="Edit" (click)="editManufacturer(m)">✎</button> }</td>
                  }
                </tr>
              } @empty { <tr><td colspan="6" class="empty">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
            </tbody>
          </table></div>
        </div>
      }
    }
  `,
  styles: [INV_STYLES],
})
export class InventorySetupComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly qty = qty;

  readonly tab = signal<'stores' | 'suppliers' | 'manufacturers'>('stores');
  readonly stores = signal<StoreDto[]>([]);
  readonly suppliers = signal<SupplierDto[]>([]);
  readonly manufacturers = signal<ManufacturerDto[]>([]);
  readonly branches = signal<string[]>([]);
  readonly busy = signal(false);
  readonly editId = signal<string | null>(null);
  readonly branchOptions = computed<Opt[]>(() => this.branches().map((b) => ({ value: b, label: b })));

  ns = { name: '', branch: '', location: '' }; es = { name: '', branch: '', location: '', isActive: true };
  nd = { name: '', contactPerson: '', phone: '', email: '', address: '' }; ed = { name: '', contactPerson: '', phone: '', email: '', address: '', notes: '', isActive: true };
  nm = { name: '', country: '', notes: '' }; em = { name: '', country: '', notes: '', isActive: true };

  constructor() {
    this.api.get<RefItem[]>('/setup/refs', { type: 'Branch' }).subscribe({ next: (r) => this.branches.set(r.map((x) => x.nameEn).sort()), error: () => {} });
    this.loadAll();
  }
  canManage(): boolean { return this.auth.has('ManageInventory'); }
  loadAll(): void {
    this.api.get<StoreDto[]>('/inventory/stores', { includeInactive: true }).subscribe({ next: (r) => this.stores.set(r), error: () => {} });
    this.api.get<SupplierDto[]>('/inventory/suppliers', { includeInactive: true }).subscribe({ next: (r) => this.suppliers.set(r), error: () => {} });
    this.api.get<ManufacturerDto[]>('/inventory/manufacturers', { includeInactive: true }).subscribe({ next: (r) => this.manufacturers.set(r), error: () => {} });
  }
  private run(req: ReturnType<ApiService['post']>, ok: string, reset: () => void): void {
    this.busy.set(true);
    req.subscribe({ next: () => { this.busy.set(false); this.editId.set(null); reset(); this.toast.success(ok); this.loadAll(); }, error: () => this.busy.set(false) });
  }

  addStore(): void { this.run(this.api.post('/inventory/stores', { name: this.ns.name.trim(), branch: this.ns.branch, location: this.ns.location || null }), 'Store added.', () => { this.ns = { name: '', branch: '', location: '' }; }); }
  editStore(s: StoreDto): void { this.editId.set(s.id); this.es = { name: s.name, branch: s.branch, location: s.location ?? '', isActive: s.isActive }; }
  saveStore(s: StoreDto): void { this.run(this.api.put(`/inventory/stores/${s.id}`, { name: this.es.name.trim(), branch: this.es.branch, location: this.es.location || null, isActive: this.es.isActive }), 'Store saved.', () => {}); }

  addSupplier(): void { this.run(this.api.post('/inventory/suppliers', { ...this.nd, name: this.nd.name.trim(), contactPerson: this.nd.contactPerson || null, phone: this.nd.phone || null, email: this.nd.email || null, address: this.nd.address || null, notes: null }), 'Distributor added.', () => { this.nd = { name: '', contactPerson: '', phone: '', email: '', address: '' }; }); }
  editSupplier(s: SupplierDto): void { this.editId.set(s.id); this.ed = { name: s.name, contactPerson: s.contactPerson ?? '', phone: s.phone ?? '', email: s.email ?? '', address: s.address ?? '', notes: s.notes ?? '', isActive: s.isActive }; }
  saveSupplier(s: SupplierDto): void { this.run(this.api.put(`/inventory/suppliers/${s.id}`, { name: this.ed.name.trim(), contactPerson: this.ed.contactPerson || null, phone: this.ed.phone || null, email: this.ed.email || null, address: this.ed.address || null, notes: this.ed.notes || null, isActive: this.ed.isActive }), 'Distributor saved.', () => {}); }

  addManufacturer(): void { this.run(this.api.post('/inventory/manufacturers', { name: this.nm.name.trim(), country: this.nm.country || null, notes: this.nm.notes || null }), 'Manufacturer added.', () => { this.nm = { name: '', country: '', notes: '' }; }); }
  editManufacturer(m: ManufacturerDto): void { this.editId.set(m.id); this.em = { name: m.name, country: m.country ?? '', notes: m.notes ?? '', isActive: m.isActive }; }
  saveManufacturer(m: ManufacturerDto): void { this.run(this.api.put(`/inventory/manufacturers/${m.id}`, { name: this.em.name.trim(), country: this.em.country || null, notes: this.em.notes || null, isActive: this.em.isActive }), 'Manufacturer saved.', () => {}); }
}
