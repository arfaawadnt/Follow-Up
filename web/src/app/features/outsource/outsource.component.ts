import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { LabListItem, OutsourceSample, OutsourceTrackingRow, PagedResult, TestLookup } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportXlsx, printTable, localToday, ddmy } from '../../shared/export.util';
import { AppDatePipe } from '../../shared/app-date.pipe';

const STATUSES = ['All', 'Collected', 'Sent', 'Received'];
const NEXT: Record<string, string> = { Collected: 'Sent', Sent: 'Received' };
const VOLUMES = ['Small', 'Medium', 'Large'];

interface TestRow { id?: string; testCode: string; testName: string; sampleVolume: string; testFees: number; outsourceFees: number; search?: string; open?: boolean; }

@Component({
  selector: 'app-outsource',
  standalone: true,
  imports: [FormsModule, ReactiveFormsModule, DecimalPipe, NgTemplateOutlet, TranslatePipe, AppDatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'outsource_samples' | t }}</div><h1>{{ 'outsource_tracking_title' | t : 'Outsource Samples Tracking' }}</h1></div>
      @if (tab() === 'samples' && auth.has('OutsourceSamples')) {
        <button class="btn btn-p" (click)="openCreate()">+ {{ 'add_outsource_sample_manually' | t : 'Add outsource sample' }}</button>
      }
    </div>

    <div class="tabbar">
      <button class="tab" [class.on]="tab() === 'samples'" (click)="tab.set('samples')">{{ 'outsource_samples' | t : 'Outsource Samples' }}</button>
      <button class="tab" [class.on]="tab() === 'report'" (click)="tab.set('report'); loadReport()">{{ 'outsource_tracking_report' | t : 'Outsource Tracking Report' }}</button>
    </div>

    @if (tab() === 'samples') {
    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:20px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_outsource' | t : 'Total outsource' }}</div><div class="val">{{ k().total | number:'1.0-0' }}</div><div class="sub">{{ 'outsource_tests' | t : 'outsource samples' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'collected_2' | t : 'Collected' }}</div><div class="val">{{ k().collected | number:'1.0-0' }}</div><div class="sub">{{ 'awaiting_dispatch' | t : 'awaiting dispatch' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'sent' | t : 'Sent' }}</div><div class="val">{{ k().sent | number:'1.0-0' }}</div><div class="sub">{{ 'in_transit_to_destination' | t : 'in transit' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'received' | t : 'Received' }}</div><div class="val">{{ k().received | number:'1.0-0' }}</div><div class="sub">{{ 'delivered_at_destination' | t : 'delivered' }}</div></div>
    </div>
    <div class="card" style="padding:20px;margin-bottom:20px">
      <div class="frm-grid" style="grid-template-columns:repeat(3,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="start"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="end"></app-date-input></div>
        <div class="field" style="display:flex;gap:8px"><button class="btn btn-p" (click)="load()" style="height:38px">{{ 'apply_filters_2' | t : 'Apply' }}</button>
          <button class="btn btn-s" (click)="exportExcel()" [disabled]="!filtered().length" style="height:38px">Export Excel</button></div>
      </div>
      <div style="display:flex;gap:4px;margin-top:12px;padding-top:12px;border-top:1px solid var(--slate-150)">
        @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="status.set(s)">{{ s === 'All' ? ('all' | t) : (s | t : s) }}</span> }
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'source_lab_col' | t : 'Source Lab' }}</th>
            <th>{{ 'quantity' | t : 'Samples' }}</th><th>{{ 'status_3' | t }}</th><th>{{ 'destination_lab' | t : 'Destination Lab' }}</th><th>{{ 'tests' | t : 'Tests' }}</th><th>{{ 'notes' | t : 'Notes' }}</th><th>{{ 'actions_4' | t : 'Actions' }}</th></tr></thead>
          <tbody>
            @for (o of filtered(); track o.id) {
              <tr>
                <td class="mono small">{{ o.visitDate | appDate }}</td>
                <td><b style="color:var(--slate-900)">{{ o.labName }}</b><div class="small muted">{{ o.labDisplayCode }}</div></td>
                <td>
                  @if (auth.has('OutsourceSamples')) { <input type="number" min="1" class="input" style="width:76px;padding:4px 8px" [ngModel]="draft(o).quantity" (ngModelChange)="setD(o, 'quantity', $event)"> }
                  @else { <span class="mono" style="font-weight:700">{{ o.quantity }}</span> }
                </td>
                <td><span class="badge" [class]="badgeClass(o.status)">{{ o.status | t : o.status }}</span></td>
                <td>
                  @if (auth.has('OutsourceSamples')) { <input class="input" style="min-width:130px;padding:4px 8px" [ngModel]="draft(o).destinationLab" (ngModelChange)="setD(o, 'destinationLab', $event)"> }
                  @else { {{ o.destinationLab ?? '—' }} }
                </td>
                <td><button class="btn btn-mini btn-s" (click)="openTests(o)">🧪 {{ (o.tests?.length ?? 0) }}</button></td>
                <td>
                  @if (auth.has('OutsourceSamples')) { <input class="input" style="min-width:130px;padding:4px 8px" [ngModel]="draft(o).notes" (ngModelChange)="setD(o, 'notes', $event)"> }
                  @else { <span class="small">{{ o.notes ?? '—' }}</span> }
                </td>
                <td class="actions">
                  @if (auth.has('OutsourceSamples')) {
                    <button class="btn btn-mini btn-p" (click)="saveRow(o)" [disabled]="busy() || !draft(o).dirty">{{ 'save' | t : 'Save' }}</button>
                    @if (next(o.status); as nx) { <button class="btn btn-mini" (click)="advance(o)" [disabled]="busy()">→ {{ nx | t : nx }}</button> }
                    <button class="btn btn-mini btn-d" (click)="remove(o)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button>
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_records' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
    }

    <!-- ===== Tracking report ===== -->
    @if (tab() === 'report') {
      <div class="card" style="padding:20px;margin-bottom:20px">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="rStart"></app-date-input></div>
          <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="rEnd"></app-date-input></div>
          <div class="field"><label>{{ 'branch_2' | t }}</label>
            <app-filter-select [options]="rOpts('branch')" [(ngModel)]="rBranch" [allValue]="'All'" [placeholder]="'all_2' | t"></app-filter-select></div>
          <div class="field"><label>{{ 'governorate_2' | t }}</label>
            <app-filter-select [multiple]="true" [options]="rOpts('governorate')" [(ngModel)]="rGov" [placeholder]="'all_2' | t"></app-filter-select></div>
        </div>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;margin-top:10px;align-items:end">
          <div class="field"><label>{{ 'city_2' | t }}</label>
            <app-filter-select [multiple]="true" [options]="rOpts('city')" [(ngModel)]="rCity" [placeholder]="'all_2' | t"></app-filter-select></div>
          <div class="field"><label>{{ 'area_2' | t }}</label>
            <app-filter-select [multiple]="true" [options]="rOpts('area')" [(ngModel)]="rArea" [placeholder]="'all_2' | t"></app-filter-select></div>
          <div class="field" style="display:flex;gap:8px">
            <button class="btn btn-p" (click)="loadReport()" style="height:38px">{{ 'apply_filters_2' | t : 'Apply' }}</button>
            <button class="btn btn-s" (click)="exportReport()" [disabled]="!reportF().length" style="height:38px">Export Excel</button>
            <button class="btn btn-s" (click)="printReport()" [disabled]="!reportF().length" style="height:38px">Export PDF</button>
          </div>
        </div>
      </div>

      @if (!reportF().length) { <div class="card empty" style="padding:24px;text-align:center">{{ 'no_records' | t : 'No records.' }}</div> }
      @else {
        <div class="card" style="padding:14px 18px;margin-bottom:16px;display:flex;gap:24px;font-size:13px">
          <span>{{ 'test_fees' | t : 'Test Fees' }}: <b>{{ grand().testFees | number:'1.2-2' }}</b></span>
          <span>{{ 'outsource_fees' | t : 'Outsource Fees' }}: <b>{{ grand().outsourceFees | number:'1.2-2' }}</b></span>
          <span>{{ 'net_revenue' | t : 'Net Revenue' }}: <b [style.color]="grand().netRevenue < 0 ? '#991b1b' : '#166534'">{{ grand().netRevenue | number:'1.2-2' }}</b></span>
        </div>
        @for (dg of reportGroups(); track dg.date) {
          <div class="card" style="margin-bottom:16px;padding:0;overflow:hidden">
            <div style="background:var(--slate-100);padding:10px 16px;font-weight:700;border-bottom:1px solid var(--slate-150);font-size:13px">{{ dg.date | appDate }}</div>
            @for (lg of dg.labs; track lg.labCode) {
              <div style="padding:8px 16px;font-weight:600;font-size:12.5px;color:var(--slate-800);border-bottom:1px solid var(--slate-100)">{{ lg.labName }} <span class="small muted">· {{ lg.labCode }}</span></div>
              <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
                <thead><tr><th>{{ 'test_name' | t : 'Test' }}</th><th>{{ 'sample_volume' | t : 'Sample Volume' }}</th>
                  <th style="text-align:right">{{ 'test_fees' | t : 'Test Fees' }}</th><th style="text-align:right">{{ 'outsource_fees' | t : 'Outsource Fees' }}</th><th style="text-align:right">{{ 'net_revenue' | t : 'Net Revenue' }}</th></tr></thead>
                <tbody>
                  @for (r of lg.rows; track $index) {
                    <tr>
                      <td>{{ r.testName }}<div class="small muted">{{ r.testCode }}</div></td>
                      <td>{{ r.sampleVolume }}</td>
                      <td class="mono" style="text-align:right">{{ r.testFees | number:'1.2-2' }}</td>
                      <td class="mono" style="text-align:right">{{ r.outsourceFees | number:'1.2-2' }}</td>
                      <td class="mono" style="text-align:right">{{ r.netRevenue | number:'1.2-2' }}</td>
                    </tr>
                  }
                  <tr style="background:var(--slate-50,#faf9f8);font-weight:700">
                    <td colspan="2" style="text-align:right">{{ 'subtotal' | t : 'Subtotal' }}</td>
                    <td class="mono" style="text-align:right">{{ lg.totals.testFees | number:'1.2-2' }}</td>
                    <td class="mono" style="text-align:right">{{ lg.totals.outsourceFees | number:'1.2-2' }}</td>
                    <td class="mono" style="text-align:right">{{ lg.totals.netRevenue | number:'1.2-2' }}</td>
                  </tr>
                </tbody>
              </table></div>
            }
          </div>
        }
      }
    }

    <!-- Shared searchable test-lines editor (bound to testRows) -->
    <ng-template #testEditor>
      @if (testRows.length) {
        <div class="trow thead">
          <div class="tsearch">{{ 'test_name' | t : 'Test' }}</div>
          <div style="width:110px">{{ 'sample_volume' | t : 'Sample Volume' }}</div>
          <div style="width:100px">{{ 'test_fees' | t : 'Test Fees' }}</div>
          <div style="width:110px">{{ 'outsource_fees' | t : 'Outsource Fees' }}</div>
          <div class="net" style="text-align:right">{{ 'net_revenue' | t : 'Net Revenue' }}</div>
          <div style="width:26px"></div>
        </div>
      }
      @for (row of testRows; track $index) {
        <div class="trow">
          <div class="tsearch">
            @if (row.testCode) {
              <div class="picked"><b>{{ row.testName }}</b> <span class="small muted">({{ row.testCode }})</span>
                <button type="button" class="btn btn-mini btn-s" (click)="clearTest(row)">✕</button></div>
            } @else {
              <input class="input" [(ngModel)]="row.search" (focus)="row.open = true" (ngModelChange)="row.open = true" [placeholder]="'select_test' | t : 'Search test…'">
              @if (row.open && (row.search ?? '').length > 0) {
                <div class="tresults">
                  @for (t of filterTests(row.search); track $index) {
                    <div class="topt" (click)="pickTestRow(row, t)">{{ t.name }} <span class="small muted">({{ t.code }})</span></div>
                  } @empty { <div class="topt muted">{{ 'no_results' | t : 'No matches' }}</div> }
                </div>
              }
            }
          </div>
          <select class="select" [(ngModel)]="row.sampleVolume" style="width:110px">
            @for (v of volumes; track v) { <option [value]="v">{{ v }}</option> }
          </select>
          <input class="input" type="number" min="0" step="0.01" [(ngModel)]="row.testFees" placeholder="Test fees" style="width:100px">
          <input class="input" type="number" min="0" step="0.01" [(ngModel)]="row.outsourceFees" placeholder="Outsource fees" style="width:110px">
          <span class="mono net" [style.color]="net(row) < 0 ? '#991b1b' : '#166534'">{{ net(row) | number:'1.2-2' }}</span>
          <button type="button" class="btn btn-mini btn-d" (click)="removeTestRow($index)">✕</button>
        </div>
      } @empty { <div class="small muted" style="margin:6px 0">{{ 'no_tests_yet' | t : 'No tests added yet.' }}</div> }
      <button type="button" class="btn btn-s" style="margin-top:8px" (click)="addTestRow()">+ {{ 'add_test' | t : 'Add test' }}</button>
    </ng-template>

    <!-- ===== Create dialog (popup) ===== -->
    @if (createOpen()) {
      <div class="overlay" (click)="createOpen.set(false)">
        <div class="dlg" (click)="$event.stopPropagation()">
          <h3 style="margin:0 0 12px">{{ 'add_outsource_sample_manually' | t : 'Add outsource sample' }}</h3>
          <form [formGroup]="form">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'date' | t : 'Date' }}</label><app-date-input formControlName="visitDate"></app-date-input></div>
              <div class="field"><label>{{ 'laboratory' | t }}</label>
                <select class="select" formControlName="laboratoryId"><option value="">—</option>@for (l of labs(); track l.id) { <option [value]="l.id">{{ l.displayCode }} · {{ l.name }}</option> }</select></div>
              <div class="field"><label>{{ 'quantity' | t : 'Samples Count' }}</label><input class="input" type="number" min="1" formControlName="quantity"></div>
              <div class="field"><label>{{ 'destination' | t : 'Destination Lab' }}</label><input class="input" formControlName="destinationLab"></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'notes' | t : 'Notes' }}</label><input class="input" formControlName="notes"></div>
            </div>
          </form>
          <div style="margin-top:14px;padding-top:12px;border-top:1px solid var(--slate-150)">
            <label class="lbl" style="display:block;margin-bottom:6px;font-weight:600">{{ 'tests' | t : 'Tests' }}</label>
            <ng-container [ngTemplateOutlet]="testEditor"></ng-container>
          </div>
          <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:16px">
            <button class="btn btn-s" (click)="createOpen.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="form.invalid || busy()" (click)="add()">{{ 'add_sample_btn' | t : 'Add Sample' }}</button>
          </div>
        </div>
      </div>
    }

    <!-- ===== Per-record tests dialog ===== -->
    @if (editingTests(); as o) {
      <div class="overlay" (click)="editingTests.set(null)">
        <div class="dlg" (click)="$event.stopPropagation()">
          <h3 style="margin:0 0 4px">{{ 'outsource_tests' | t : 'Outsource tests' }}</h3>
          <div class="small muted" style="margin-bottom:12px">{{ o.labName }} · {{ o.labDisplayCode }} · {{ o.visitDate | appDate }}</div>
          <ng-container [ngTemplateOutlet]="testEditor"></ng-container>
          <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:16px">
            <button class="btn btn-s" (click)="editingTests.set(null)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy()" (click)="saveTests()">{{ 'save' | t : 'Save' }}</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .tabbar { display:flex; gap:6px; flex-wrap:wrap; margin-bottom:16px }
    .tab { background:var(--white); border:1px solid var(--slate-300); color:var(--slate-700); border-radius:var(--r-btn,8px); padding:8px 16px; font:600 12.5px var(--ui); cursor:pointer }
    .tab.on { background:var(--primary-blue,#0078d4); color:#fff; border-color:var(--primary-blue,#0078d4) }
    .actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:flex-start;justify-content:center;z-index:1000;overflow-y:auto;padding:40px 12px}
    .dlg{background:var(--white,#fff);border-radius:12px;padding:22px;width:min(94vw,760px);box-shadow:0 16px 48px rgba(0,0,0,.25)}
    .trow{display:flex;gap:8px;align-items:center;margin-bottom:8px}
    .tsearch{flex:1;position:relative;min-width:180px}
    .picked{display:flex;align-items:center;gap:8px;padding:7px 10px;border:1px solid var(--slate-200,#e5e7eb);border-radius:8px}
    .tresults{position:absolute;z-index:5;left:0;right:0;top:100%;margin-top:2px;background:#fff;border:1px solid var(--slate-200,#e5e7eb);border-radius:8px;max-height:200px;overflow:auto;box-shadow:0 8px 24px rgba(0,0,0,.12)}
    .topt{padding:7px 10px;cursor:pointer;font-size:12.5px;border-bottom:1px solid var(--slate-100,#f3f2f1)}
    .topt:hover{background:var(--slate-50,#faf9f8)}
    .net{width:90px;text-align:right;font-weight:700}
    .thead{margin-bottom:2px}
    .thead > div{font-size:11px;font-weight:600;color:var(--slate-600)}
    .lbl{font-size:11px;color:var(--slate-600)}
  `],
})
export class OutsourceComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<OutsourceSample[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly status = signal('All');
  readonly statuses = STATUSES;
  readonly volumes = VOLUMES;
  readonly tab = signal<'samples' | 'report'>('samples');
  readonly testLookup = signal<TestLookup[]>([]);
  readonly createOpen = signal(false);

  private readonly today = localToday();
  start = this.today; end = this.today;

  readonly form = this.fb.group({
    visitDate: this.fb.control(localToday(), Validators.required),
    laboratoryId: this.fb.control('', Validators.required),
    destinationLab: this.fb.control('', Validators.required),
    quantity: this.fb.control(1, [Validators.required, Validators.min(1)]),
    notes: this.fb.control(''),
  });

  readonly filtered = computed(() => this.items().filter((s) => this.status() === 'All' || s.status === this.status()));
  readonly k = computed(() => {
    const f = this.filtered();
    const sum = (st: string) => f.filter((s) => s.status === st).reduce((a, s) => a + s.quantity, 0);
    return { total: f.reduce((a, s) => a + s.quantity, 0), collected: sum('Collected'), sent: sum('Sent'), received: sum('Received') };
  });

  constructor() {
    this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items) });
    this.api.get<TestLookup[]>('/test-lookup').subscribe({ next: (r) => this.testLookup.set(r), error: () => {} });
    this.load();
  }

  next(status: string): string | undefined { return NEXT[status]; }
  badgeClass(s: string): string { return s === 'Received' ? 'b-ok' : s === 'Sent' ? 'b-warn' : 'b-info'; }

  // ---- Inline row drafts (samples/destination/notes editable in the grid) ----
  private readonly drafts = new Map<string, { quantity: number; destinationLab: string; notes: string; dirty: boolean }>();
  draft(o: OutsourceSample): { quantity: number; destinationLab: string; notes: string; dirty: boolean } {
    let d = this.drafts.get(o.id);
    if (!d) { d = { quantity: o.quantity, destinationLab: o.destinationLab ?? '', notes: o.notes ?? '', dirty: false }; this.drafts.set(o.id, d); }
    return d;
  }
  setD(o: OutsourceSample, key: 'quantity' | 'destinationLab' | 'notes', value: string | number): void {
    const d = this.draft(o);
    if (key === 'quantity') d.quantity = Number(value) || 0;
    else d[key] = String(value ?? '');
    d.dirty = true;
  }
  saveRow(o: OutsourceSample): void {
    const d = this.draft(o);
    this.busy.set(true);
    this.api.put(`/outsource-samples/${o.id}`, {
      quantity: d.quantity, destinationLab: d.destinationLab.trim() || null, notes: d.notes.trim() || null,
    }).subscribe({
      next: () => { this.toast.success('Sample updated.'); this.busy.set(false); this.drafts.delete(o.id); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  // ---- Test line items (shared searchable editor) ----
  testRows: TestRow[] = [];
  readonly editingTests = signal<OutsourceSample | null>(null);
  openTests(o: OutsourceSample): void {
    this.editingTests.set(o);
    this.testRows = (o.tests ?? []).map((t) => ({ ...t, search: '', open: false }));
  }
  addTestRow(): void { this.testRows = [...this.testRows, { testCode: '', testName: '', sampleVolume: 'Medium', testFees: 0, outsourceFees: 0, search: '', open: true }]; }
  removeTestRow(i: number): void { this.testRows = this.testRows.filter((_, x) => x !== i); }
  filterTests(search?: string): TestLookup[] {
    const q = (search ?? '').trim().toLowerCase();
    if (!q) return [];
    return this.testLookup().filter((t) => t.name.toLowerCase().includes(q) || t.code.toLowerCase().includes(q)).slice(0, 30);
  }
  pickTestRow(row: TestRow, t: TestLookup): void { row.testCode = t.code; row.testName = t.name; row.search = ''; row.open = false; }
  clearTest(row: TestRow): void { row.testCode = ''; row.testName = ''; row.search = ''; row.open = true; }
  net(row: TestRow): number { return (Number(row.testFees) || 0) - (Number(row.outsourceFees) || 0); }
  private testsPayload() {
    return this.testRows.filter((r) => r.testCode)
      .map((r) => ({ testCode: r.testCode, testName: r.testName, sampleVolume: r.sampleVolume, testFees: Number(r.testFees) || 0, outsourceFees: Number(r.outsourceFees) || 0 }));
  }
  saveTests(): void {
    const o = this.editingTests();
    if (!o) return;
    this.busy.set(true);
    this.api.put(`/outsource-samples/${o.id}`, { quantity: o.quantity, destinationLab: o.destinationLab, notes: o.notes, tests: this.testsPayload() }).subscribe({
      next: () => { this.toast.success('Tests saved.'); this.busy.set(false); this.editingTests.set(null); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  // ---- Create (popup) ----
  openCreate(): void {
    this.form.reset({ visitDate: localToday(), laboratoryId: '', destinationLab: '', quantity: 1, notes: '' });
    this.testRows = [];
    this.createOpen.set(true);
  }
  add(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.getRawValue();
    this.api.post('/outsource-samples', { ...v, notes: v.notes || null, tests: this.testsPayload() }).subscribe({
      next: () => { this.toast.success('Sample added.'); this.busy.set(false); this.createOpen.set(false); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.get<OutsourceSample[]>('/outsource-samples', { start: this.start, end: this.end }).subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  exportExcel(): void {
    exportXlsx('outsource-samples.xlsx',
      ['Date', 'Source lab', 'Code', 'Samples', 'Status', 'Destination lab', 'Tests', 'Notes'],
      this.filtered().map((o) => [ddmy(o.visitDate), o.labName, o.labDisplayCode, o.quantity, o.status, o.destinationLab, o.tests?.length ?? 0, o.notes]));
  }
  advance(o: OutsourceSample): void {
    const status = this.next(o.status); if (!status) return;
    this.busy.set(true);
    this.api.post(`/outsource-samples/${o.id}/status`, { status }).subscribe({ next: () => { this.toast.success('Status updated.'); this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(o: OutsourceSample): void {
    if (!window.confirm('Delete this outsource sample?')) return;
    this.busy.set(true);
    this.api.delete(`/outsource-samples/${o.id}`).subscribe({ next: () => { this.toast.success('Sample deleted.'); this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }

  // ---- Tracking report ----
  rStart = this.today; rEnd = this.today;
  rBranch = 'All'; rGov: string[] = []; rCity: string[] = []; rArea: string[] = [];
  readonly report = signal<OutsourceTrackingRow[]>([]);

  loadReport(): void {
    this.api.get<OutsourceTrackingRow[]>('/outsource-samples/tracking', { from: this.rStart, to: this.rEnd }).subscribe({ next: (r) => this.report.set(r) });
  }
  rOpts(field: 'branch' | 'governorate' | 'city' | 'area'): string[] {
    return [...new Set(this.report().map((r) => r[field]).filter((x): x is string => !!x))].sort();
  }
  readonly reportF = computed(() => this.report().filter((r) =>
    (this.rBranch === 'All' || r.branch === this.rBranch) &&
    (!this.rGov.length || this.rGov.includes(r.governorate ?? '')) &&
    (!this.rCity.length || this.rCity.includes(r.city ?? '')) &&
    (!this.rArea.length || this.rArea.includes(r.area ?? ''))));

  private sum(rows: OutsourceTrackingRow[]) {
    return {
      testFees: rows.reduce((a, r) => a + r.testFees, 0),
      outsourceFees: rows.reduce((a, r) => a + r.outsourceFees, 0),
      netRevenue: rows.reduce((a, r) => a + r.netRevenue, 0),
    };
  }
  readonly grand = computed(() => this.sum(this.reportF()));
  readonly reportGroups = computed(() => {
    const byDate = new Map<string, OutsourceTrackingRow[]>();
    for (const r of this.reportF()) { const g = byDate.get(r.visitDate) ?? []; g.push(r); byDate.set(r.visitDate, g); }
    return [...byDate.entries()].sort((a, b) => b[0].localeCompare(a[0])).map(([date, rows]) => {
      const byLab = new Map<string, OutsourceTrackingRow[]>();
      for (const r of rows) { const key = r.labDisplayCode + '|' + r.labName; const g = byLab.get(key) ?? []; g.push(r); byLab.set(key, g); }
      const labs = [...byLab.values()].sort((a, b) => a[0].labName.localeCompare(b[0].labName))
        .map((lrows) => ({ labName: lrows[0].labName, labCode: lrows[0].labDisplayCode, rows: lrows, totals: this.sum(lrows) }));
      return { date, labs, totals: this.sum(rows) };
    });
  });

  // Flatten the grouped report into export rows, keeping the on-screen per-lab subtotals + grand total.
  private reportRows(): (string | number | null)[][] {
    const rows: (string | number | null)[][] = [];
    for (const dg of this.reportGroups()) {
      for (const lg of dg.labs) {
        for (const r of lg.rows) {
          rows.push([ddmy(r.visitDate), r.labName, r.labDisplayCode, r.governorate, r.city, r.area,
            r.testName, r.testCode, r.sampleVolume, r.testFees, r.outsourceFees, r.netRevenue]);
        }
        rows.push(['', lg.labName + ' — subtotal', '', '', '', '', '', '', '', lg.totals.testFees, lg.totals.outsourceFees, lg.totals.netRevenue]);
      }
    }
    const g = this.grand();
    rows.push(['', 'GRAND TOTAL', '', '', '', '', '', '', '', g.testFees, g.outsourceFees, g.netRevenue]);
    return rows;
  }
  private static readonly REPORT_HEADER = ['Date', 'Lab', 'Code', 'Governorate', 'City', 'Area', 'Test', 'Test code', 'Sample volume', 'Test fees', 'Outsource fees', 'Net revenue'];
  exportReport(): void { exportXlsx('outsource-tracking.xlsx', OutsourceComponent.REPORT_HEADER, this.reportRows()); }
  printReport(): void { printTable('Outsource Tracking Report', OutsourceComponent.REPORT_HEADER, this.reportRows()); }
}
