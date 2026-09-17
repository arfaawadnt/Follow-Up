import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { exportXlsx, localToday } from '../../shared/export.util';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';
import { InventoryItemDto, ItemTestLink, ManufacturerDto, TestLookup } from '../../core/models';
import { INV_STYLES, ITEM_KINDS, Opt, qty } from './inventory.util';

/**
 * Items — the catalogue of chemicals and consumables: code, name, kind, manufacturer, catalogue number, unit, stock limit
 * (low-stock alert), suggested reorder quantity, expiry warning window, storage conditions, and the tests the item is
 * consumed by (quantity per test — drives the Utilization report). Items are deactivated, never deleted.
 */
@Component({
  selector: 'app-inventory-items',
  standalone: true,
  imports: [FormsModule, TranslatePipe, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_items' | t : 'Items' }}</div><h1>{{ 'inv_items' | t : 'Items' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'New item' | t : 'New item' }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
      </div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:2fr 1fr 1fr auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'search' | t : 'Search' }}</label><input class="input" [(ngModel)]="search" (keydown.enter)="load()" placeholder="code / name / catalogue no."></div>
        <div class="field"><label>{{ 'kind' | t : 'Kind' }}</label><app-filter-select [(ngModel)]="kind" [options]="kindOptions" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>&nbsp;</label><label class="small" style="height:36px;display:flex;align-items:center"><input type="checkbox" [(ngModel)]="includeInactive"> {{ 'Include inactive' | t : 'Include inactive' }}</label></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'code' | t : 'Code' }}</th><th>{{ 'name' | t : 'Name' }}</th><th>{{ 'kind' | t : 'Kind' }}</th><th>{{ 'manufacturer' | t : 'Manufacturer' }}</th><th>{{ 'catalog_number' | t : 'Catalogue No.' }}</th>
            <th>{{ 'unit' | t : 'Unit' }}</th><th class="r">{{ 'on_hand' | t : 'On hand' }}</th><th class="r">{{ 'min_stock' | t : 'Min. stock' }}</th><th class="r">{{ 'reorder_qty' | t : 'Reorder qty' }}</th>
            <th class="r">{{ 'expiry_warning_days' | t : 'Expiry warning' }}</th><th>{{ 'linked_tests' | t : 'Linked tests' }}</th><th>{{ 'storage_conditions' | t : 'Storage' }}</th><th>{{ 'active' | t : 'Active' }}</th><th class="ar">{{ 'actions' | t : 'Actions' }}</th>
          </tr></thead>
          <tbody>
            @for (i of rows(); track i.id) {
              <tr [class.muted]="!i.isActive">
                <td class="mono"><b>{{ i.code }}</b></td><td>{{ i.name }}</td><td>{{ i.kind }}</td><td>{{ i.manufacturerName }}</td><td class="mono">{{ i.catalogNumber || '—' }}</td>
                <td>{{ i.unit }}</td><td class="r mono" [class.neg]="i.minStock > 0 && i.onHand <= i.minStock">{{ qty(i.onHand) }}</td><td class="r mono">{{ i.minStock ? qty(i.minStock) : '—' }}</td><td class="r mono">{{ i.reorderQuantity ? qty(i.reorderQuantity) : '—' }}</td>
                <td class="r mono">{{ i.expiryWarningDays }} d</td>
                <td><div class="chip-list">@for (t of i.testLinks; track t.testCode + t.testType) { <span [title]="t.testName">{{ t.testCode }} × {{ qty(t.quantityPerTest) }}</span> }</div></td>
                <td>{{ i.storageConditions || '—' }}</td><td>{{ i.isActive ? ('active' | t : 'Active') : ('inactive' | t : 'Inactive') }}</td>
                <td class="ar actions">@if (canManage()) { <button class="icon-btn" title="Edit" (click)="openEdit(i)">✎</button> }</td>
              </tr>
            } @empty { <tr><td colspan="14" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(false)">
        <div class="as-dlg wide" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ editId ? ('Edit item' | t : 'Edit item') : ('New item' | t : 'New item') }}</h2><button class="icon-btn" (click)="dlg.set(false)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
              <div class="field"><label>{{ 'code' | t : 'Code' }} <span class="req">*</span></label><input class="input" [(ngModel)]="f.code" maxlength="40" [disabled]="!!editId" style="text-transform:uppercase"></div>
              <div class="field" style="grid-column:span 2"><label>{{ 'name' | t : 'Name' }} <span class="req">*</span></label><input class="input" [(ngModel)]="f.name" maxlength="200"></div>
              <div class="field"><label>{{ 'kind' | t : 'Kind' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.kind" [options]="kindOptions"></app-filter-select></div>
              <div class="field" style="grid-column:span 2"><label>{{ 'manufacturer' | t : 'Manufacturer' }} <span class="req">*</span></label><app-filter-select [(ngModel)]="f.manufacturerId" [options]="manufacturerOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'catalog_number' | t : 'Catalogue No.' }}</label><input class="input" [(ngModel)]="f.catalogNumber" maxlength="80"></div>
              <div class="field"><label>{{ 'unit' | t : 'Unit' }} <span class="req">*</span></label><input class="input" [(ngModel)]="f.unit" maxlength="20" placeholder="mL / g / test / box"></div>
              <div class="field"><label>{{ 'min_stock' | t : 'Min. stock' }}</label><input class="input" type="number" min="0" step="0.001" [(ngModel)]="f.minStock"><div class="hint">{{ 'Low-stock alert at or below this total (0 = none).' | t : 'Low-stock alert at or below this total (0 = none).' }}</div></div>
              <div class="field"><label>{{ 'reorder_qty' | t : 'Reorder qty' }}</label><input class="input" type="number" min="0" step="0.001" [(ngModel)]="f.reorderQuantity"></div>
              <div class="field"><label>{{ 'expiry_warning_days' | t : 'Expiry warning (days)' }}</label><input class="input" type="number" min="0" max="730" [(ngModel)]="f.expiryWarningDays"></div>
              <div class="field"><label>{{ 'storage_conditions' | t : 'Storage' }}</label><input class="input" [(ngModel)]="f.storageConditions" maxlength="200" placeholder="2–8 °C, dark"></div>
              <div class="field" style="grid-column:span 4"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" [(ngModel)]="f.notes" maxlength="1000"></div>
              @if (editId) { <div class="field"><label>{{ 'active' | t : 'Active' }}</label><input type="checkbox" [(ngModel)]="f.isActive"></div> }
            </div>

            <div class="sec-title">{{ 'linked_tests' | t : 'Linked tests' }} <span class="muted" style="font-weight:400;text-transform:none">— {{ 'consumption per test performed (Utilization report)' | t : 'consumption per test performed (Utilization report)' }}</span></div>
            <div class="frm-grid" style="grid-template-columns:3fr 1fr auto;gap:12px;align-items:end">
              <div class="field"><label>{{ 'test' | t : 'Test' }}</label><app-filter-select [(ngModel)]="linkKey" [options]="testOptions()" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              <div class="field"><label>{{ 'qty_per_test' | t : 'Qty / test' }}</label><input class="input" type="number" min="0" step="0.001" [(ngModel)]="linkQty"></div>
              <div class="field"><button class="btn btn-s" style="height:36px" [disabled]="!linkKey || !linkQty" (click)="addLink()">{{ 'add' | t : 'Add' }}</button></div>
            </div>
            <div class="grid-scroll"><table class="lines" style="margin-top:8px">
              <thead><tr><th>{{ 'code' | t : 'Code' }}</th><th>{{ 'test_name_3' | t : 'Test Name' }}</th><th class="r">{{ 'qty_per_test' | t : 'Qty / test' }}</th><th></th></tr></thead>
              <tbody>
                @for (l of f.testLinks; track l.testCode + l.testType) {
                  <tr><td class="mono">{{ l.testCode }}</td><td>{{ l.testName }}</td><td class="r"><input class="input" type="number" min="0" step="0.001" [(ngModel)]="l.quantityPerTest" style="width:110px;text-align:right"></td><td class="ar"><button class="icon-btn del" (click)="removeLink(l)">🗑</button></td></tr>
                } @empty { <tr><td colspan="4" class="muted small">{{ 'No tests linked yet.' | t : 'No tests linked yet.' }}</td></tr> }
              </tbody>
            </table></div>
          </div>
          <div class="as-dlg-foot">
            @if (missing().length) { <span class="hint" style="margin:0 auto 0 0">{{ 'missing_fields' | t : 'Missing' }}: {{ missing().join(', ') }}</span> }
            <button class="btn btn-s" (click)="dlg.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy()" (click)="save()">{{ 'save' | t : 'Save' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [INV_STYLES],
})
export class InventoryItemsComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly qty = qty;

  readonly rows = signal<InventoryItemDto[]>([]);
  readonly manufacturers = signal<ManufacturerDto[]>([]);
  readonly tests = signal<TestLookup[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal(false);
  search = ''; kind = ''; includeInactive = false;
  editId: string | null = null;
  linkKey = ''; linkQty: number | null = 1;
  f = this.blank();

  readonly kindOptions: Opt[] = ITEM_KINDS.map((k) => ({ value: k, label: k }));
  readonly manufacturerOptions = computed<Opt[]>(() => this.manufacturers().filter((m) => m.isActive || m.id === this.f.manufacturerId).map((m) => ({ value: m.id, label: m.name })));
  // Test codes repeat across test types in the catalogue, so the option key carries both.
  readonly testOptions = computed<Opt[]>(() => this.tests().map((t) => ({ value: `${t.code}|${t.testType}`, label: `${t.code} · ${t.name}` })));

  constructor() {
    this.api.get<ManufacturerDto[]>('/inventory/manufacturers', { includeInactive: true }).subscribe({ next: (r) => this.manufacturers.set(r), error: () => {} });
    this.api.get<TestLookup[]>('/test-lookup').subscribe({ next: (r) => this.tests.set(r), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageInventory'); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | boolean> = { includeInactive: this.includeInactive };
    if (this.search.trim()) params['search'] = this.search.trim();
    if (this.kind) params['kind'] = this.kind;
    this.api.get<InventoryItemDto[]>('/inventory/items', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private blank() {
    return { code: '', name: '', kind: 'Chemical', manufacturerId: '', catalogNumber: '', unit: '', minStock: 0 as number | null, reorderQuantity: 0 as number | null,
      expiryWarningDays: 30 as number | null, storageConditions: '', notes: '', isActive: true, testLinks: [] as ItemTestLink[] };
  }
  openNew(): void { this.editId = null; this.f = this.blank(); this.linkKey = ''; this.linkQty = 1; this.dlg.set(true); }
  openEdit(i: InventoryItemDto): void {
    this.editId = i.id;
    this.f = { code: i.code, name: i.name, kind: i.kind, manufacturerId: i.manufacturerId, catalogNumber: i.catalogNumber ?? '', unit: i.unit, minStock: i.minStock, reorderQuantity: i.reorderQuantity,
      expiryWarningDays: i.expiryWarningDays, storageConditions: i.storageConditions ?? '', notes: i.notes ?? '', isActive: i.isActive, testLinks: i.testLinks.map((t) => ({ ...t })) };
    this.linkKey = ''; this.linkQty = 1;
    this.dlg.set(true);
  }
  addLink(): void {
    const [code, type] = this.linkKey.split('|');
    const t = this.tests().find((x) => x.code === code && String(x.testType) === type);
    if (!t) return;
    if (this.f.testLinks.some((l) => l.testCode === t.code && l.testType === t.testType)) { this.toast.warning('This test is already linked.'); return; }
    this.f.testLinks = [...this.f.testLinks, { testCode: t.code, testType: t.testType, testName: t.name, quantityPerTest: Number(this.linkQty) }];
    this.linkKey = '';
  }
  removeLink(l: ItemTestLink): void { this.f.testLinks = this.f.testLinks.filter((x) => x !== l); }
  missing(): string[] {
    const f = this.f; const m: string[] = [];
    if (!f.code.trim()) m.push('Code');
    if (!f.name.trim()) m.push('Name');
    if (!f.kind) m.push('Kind');
    if (!f.manufacturerId) m.push('Manufacturer');
    if (!f.unit.trim()) m.push('Unit');
    if (f.minStock === null || Number(f.minStock) < 0) m.push('Min. stock');
    if (f.reorderQuantity === null || Number(f.reorderQuantity) < 0) m.push('Reorder qty');
    if (f.expiryWarningDays === null || Number(f.expiryWarningDays) < 0 || Number(f.expiryWarningDays) > 730) m.push('Expiry warning (0–730)');
    if (f.testLinks.some((l) => !(Number(l.quantityPerTest) > 0))) m.push('Qty / test');
    return m;
  }
  save(): void {
    const missing = this.missing();
    if (missing.length) { this.toast.warning(`Please fill: ${missing.join(', ')}`); return; }
    this.busy.set(true);
    const body = { code: this.f.code.trim().toUpperCase(), name: this.f.name.trim(), kind: this.f.kind, manufacturerId: this.f.manufacturerId, catalogNumber: this.f.catalogNumber || null,
      unit: this.f.unit.trim(), minStock: Number(this.f.minStock), reorderQuantity: Number(this.f.reorderQuantity), expiryWarningDays: Number(this.f.expiryWarningDays),
      storageConditions: this.f.storageConditions || null, notes: this.f.notes || null, isActive: this.f.isActive,
      testLinks: this.f.testLinks.map((l) => ({ testCode: l.testCode, testType: l.testType, testName: l.testName, quantityPerTest: Number(l.quantityPerTest) })) };
    const req = this.editId ? this.api.put(`/inventory/items/${this.editId}`, body) : this.api.post('/inventory/items', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Item saved.'); this.load(); }, error: () => this.busy.set(false) });
  }

  exportExcel(): void {
    exportXlsx(`inventory-items-${localToday()}.xlsx`, ['Code', 'Name', 'Kind', 'Manufacturer', 'Catalogue No.', 'Unit', 'On hand', 'Min. stock', 'Reorder qty', 'Expiry warning (days)', 'Linked tests', 'Storage', 'Active'],
      this.rows().map((i) => [i.code, i.name, i.kind, i.manufacturerName, i.catalogNumber ?? '', i.unit, i.onHand, i.minStock, i.reorderQuantity, i.expiryWarningDays,
        i.testLinks.map((t) => `${t.testCode} × ${t.quantityPerTest}`).join(', '), i.storageConditions ?? '', i.isActive ? 'Yes' : 'No']));
  }
}
