import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';
import { InventoryItemDto, StoreDto, UtilizationRow } from '../../core/models';
import { INV_STYLES, Opt, firstOfMonth, qty } from './inventory.util';

/**
 * Utilization — consumption tracking per item: the tests performed in the period (from the synced test statistics) ×
 * the quantity per test linked on the item give the EXPECTED consumption; the Consumption issues from stock give the
 * ACTUAL one. Variance = actual − expected (positive = more used than the tests justify); Utilization % = expected /
 * actual. A store narrows the actual side to that store and, when its branch resolves to a Branch code, the test counts
 * to that branch's registrations.
 */
@Component({
  selector: 'app-inventory-utilization',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'inventory' | t : 'Inventory' }} / {{ 'inv_utilization' | t : 'Utilization' }}</div><h1>{{ 'inv_utilization' | t : 'Utilization' }}</h1></div>
      <div class="pagehead-actions">
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'items' | t : 'Items' }}</div><div class="val">{{ rows().length }}</div><div class="sub">{{ 'with linked tests or consumption' | t : 'with linked tests or consumption' }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'tests_performed' | t : 'Tests' }}</div><div class="val">{{ k().tests | number:'1.0-0' }}</div><div class="sub">{{ 'linked tests performed in the period' | t : 'linked tests performed in the period' }}</div></div>
      <div class="kpi" [class.kpi-red]="k().over > 0" [class.kpi-green]="k().over === 0"><div class="lbl">{{ 'Over-consuming' | t : 'Over-consuming' }}</div><div class="val">{{ k().over }}</div><div class="sub">{{ 'items using more than the tests justify' | t : 'items using more than the tests justify' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'No consumption recorded' | t : 'No consumption recorded' }}</div><div class="val">{{ k().none }}</div><div class="sub">{{ 'tests ran but nothing was issued' | t : 'tests ran but nothing was issued' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr) auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'store' | t : 'Store' }}</label><app-filter-select [(ngModel)]="storeId" [options]="storeOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'item' | t : 'Item' }}</label><app-filter-select [(ngModel)]="itemId" [options]="itemOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      <div class="hint">{{ 'Expected = tests performed × quantity per test (Items → Linked tests). Actual = Consumption issues from stock. Link every item to the tests it is used for so the expected side is complete.' | t : 'Expected = tests performed × quantity per test (Items → Linked tests). Actual = Consumption issues from stock. Link every item to the tests it is used for so the expected side is complete.' }}</div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th></th><th>{{ 'code' | t : 'Code' }}</th><th>{{ 'item' | t : 'Item' }}</th><th>{{ 'unit' | t : 'Unit' }}</th><th class="r">{{ 'tests_performed' | t : 'Tests' }}</th>
            <th class="r">{{ 'expected_consumption' | t : 'Expected' }}</th><th class="r">{{ 'actual_consumption' | t : 'Actual' }}</th><th class="r">{{ 'variance' | t : 'Variance' }}</th><th class="r">{{ 'utilization_pct' | t : 'Utilization %' }}</th><th>{{ 'Assessment' | t : 'Assessment' }}</th>
          </tr></thead>
          <tbody>
            @for (r of rows(); track r.itemId) {
              <tr class="clickable" (click)="open.set(open() === r.itemId ? null : r.itemId)">
                <td>{{ open() === r.itemId ? '▾' : '▸' }}</td><td class="mono"><b>{{ r.itemCode }}</b></td><td>{{ r.itemName }}</td><td>{{ r.unit }}</td><td class="r mono">{{ r.testsPerformed | number:'1.0-0' }}</td>
                <td class="r mono">{{ qty(r.expected) }}</td><td class="r mono">{{ qty(r.actual) }}</td>
                <td class="r mono" [class.neg]="r.variance > 0" [class.pos]="r.variance < 0">{{ r.variance > 0 ? '+' : '' }}{{ qty(r.variance) }}</td>
                <td class="r mono">{{ r.utilizationPct !== null ? (r.utilizationPct | number:'1.0-1') + '%' : '—' }}</td>
                <td><span class="badge" [class]="'badge ' + assess(r).cls">{{ assess(r).text }}</span></td>
              </tr>
              @if (open() === r.itemId) {
                <tr class="sub"><td></td><td colspan="9">
                  @if (!r.tests.length) { <span class="muted">{{ 'No tests linked to this item — consumption cannot be expected.' | t : 'No tests linked to this item — consumption cannot be expected.' }}</span> }
                  @else {
                    <div class="grid-scroll"><table class="lines" style="width:auto;min-width:60%">
                      <thead><tr><th>{{ 'test' | t : 'Test' }}</th><th class="r">{{ 'tests_performed' | t : 'Tests' }}</th><th class="r">{{ 'qty_per_test' | t : 'Qty / test' }}</th><th class="r">{{ 'expected_consumption' | t : 'Expected' }}</th></tr></thead>
                      <tbody>@for (t of r.tests; track t.testCode + t.testType) { <tr><td><b>{{ t.testCode }}</b> · {{ t.testName }}</td><td class="r mono">{{ t.testCount | number:'1.0-0' }}</td><td class="r mono">{{ qty(t.quantityPerTest) }}</td><td class="r mono">{{ qty(t.expected) }} {{ r.unit }}</td></tr> }</tbody>
                    </table></div>
                  }
                </td></tr>
              }
            } @empty { <tr><td colspan="10" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [INV_STYLES],
})
export class InventoryUtilizationComponent {
  private readonly api = inject(ApiService);
  readonly qty = qty;

  readonly rows = signal<UtilizationRow[]>([]);
  readonly stores = signal<StoreDto[]>([]);
  readonly items = signal<InventoryItemDto[]>([]);
  readonly loading = signal(true);
  readonly open = signal<string | null>(null);
  from = firstOfMonth(); to = localToday(); storeId = ''; itemId = '';

  readonly storeOptions = computed<Opt[]>(() => this.stores().map((s) => ({ value: s.id, label: `${s.name} (${s.branch})` })));
  readonly itemOptions = computed<Opt[]>(() => this.items().map((i) => ({ value: i.id, label: `${i.code} · ${i.name}` })));
  readonly k = computed(() => {
    const r = this.rows();
    return { tests: r.reduce((a, x) => a + x.testsPerformed, 0), over: r.filter((x) => x.expected > 0 && x.actual > x.expected * 1.1).length, none: r.filter((x) => x.expected > 0 && x.actual === 0).length };
  });

  constructor() {
    this.api.get<StoreDto[]>('/inventory/stores').subscribe({ next: (r) => this.stores.set(r), error: () => {} });
    this.api.get<InventoryItemDto[]>('/inventory/items').subscribe({ next: (r) => this.items.set(r), error: () => {} });
    this.load();
  }

  load(): void {
    this.loading.set(true); this.open.set(null);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.storeId) params['storeId'] = this.storeId;
    if (this.itemId) params['itemId'] = this.itemId;
    this.api.get<UtilizationRow[]>('/inventory/utilization', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  /** Within ±10 % of the expected consumption = OK; more = over-consuming; less = under (tests ran, less issued); no basis when nothing is linked. */
  assess(r: UtilizationRow): { cls: string; text: string } {
    if (!r.tests.length) return { cls: 'b-neu', text: r.actual > 0 ? 'No linked tests' : 'No activity' };
    if (r.testsPerformed === 0) return { cls: 'b-neu', text: r.actual > 0 ? 'No tests recorded' : 'No activity' };
    if (r.actual === 0) return { cls: 'b-warn', text: 'Nothing issued' };
    const ratio = r.actual / r.expected;
    if (ratio > 1.1) return { cls: 'b-bad', text: `Over by ${Math.round((ratio - 1) * 100)}%` };
    if (ratio < 0.9) return { cls: 'b-warn', text: `Under by ${Math.round((1 - ratio) * 100)}%` };
    return { cls: 'b-ok', text: 'On target' };
  }

  private static readonly HEADER = ['Code', 'Item', 'Unit', 'Tests performed', 'Expected', 'Actual', 'Variance', 'Utilization %', 'Assessment', 'Linked tests'];
  private exportRows() {
    return this.rows().map((r) => [r.itemCode, r.itemName, r.unit, r.testsPerformed, r.expected, r.actual, r.variance, r.utilizationPct ?? '', this.assess(r).text,
      r.tests.map((t) => `${t.testCode} (${t.testCount} × ${t.quantityPerTest} = ${t.expected})`).join('; ')]);
  }
  exportExcel(): void { exportXlsx(`utilization-${this.from}_${this.to}.xlsx`, InventoryUtilizationComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Utilization (${ddmy(this.from)} → ${ddmy(this.to)})`, InventoryUtilizationComponent.HEADER, this.exportRows()); }
}
