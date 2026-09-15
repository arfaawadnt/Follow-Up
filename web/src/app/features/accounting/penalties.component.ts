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
import { LabListItem, PagedResult, PenaltyDto, TestLookup } from '../../core/models';
import { ACC_STYLES, PENALTY_USERS, dayName, firstOfMonth, money } from './accounting.util';

type Opt = { value: string; label: string };

/**
 * Penalty Statement — penalties recorded per lab (a wrong test booked instead of the right one). The penalty amount is
 * wrong − right, computed server-side; the Deductions page sums it per area.
 */
@Component({
  selector: 'app-acc-penalties',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_penalties' | t : 'Penalty Statement' }}</div><h1>{{ 'acc_penalties' | t : 'Penalty Statement' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'record_penalty' | t : 'Record penalty' }}</button> }
        <button class="btn btn-s" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()">{{ 'export_pdf' | t : 'Export PDF' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ k().count }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'wrong_test' | t : 'Wrong Test' }} · {{ 'value' | t : 'Value' }}</div><div class="val">{{ k().wrong | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'right_test' | t : 'Right Test' }} · {{ 'value' | t : 'Value' }}</div><div class="val">{{ k().right | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_penalty' | t : 'Total penalty' }}</div><div class="val">{{ k().penalty | number:'1.2-2' }}</div><div class="sub">EGP</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'lab' | t : 'Lab' }}</label><app-filter-select [(ngModel)]="labId" [options]="labOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'serial' | t : 'Serial' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'lab' | t : 'Lab' }}</th>
            <th>{{ 'acc_no' | t : 'Acc No' }}</th><th>{{ 'patient_name' | t : 'Patient Name' }}</th>
            <th>{{ 'wrong_test' | t : 'Wrong Test' }}</th><th class="r">{{ 'value' | t : 'Value' }}</th>
            <th>{{ 'right_test' | t : 'Right Test' }}</th><th class="r">{{ 'value' | t : 'Value' }}</th>
            <th class="r">{{ 'penalty' | t : 'Penalty' }}</th><th>{{ 'user_role' | t : 'User' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (p of rows(); track p.id) {
              <tr>
                <td class="mono">{{ p.serial }}</td><td>{{ day(p.date) }}</td><td>{{ ddmy(p.date) }}</td>
                <td><b>{{ p.labName }}</b> <span class="small muted">{{ p.labDisplayCode }}</span></td>
                <td>{{ p.accNo }}</td><td>{{ p.patientName }}</td>
                <td>{{ p.wrongTestName }} <span class="small muted">{{ p.wrongTestCode }}</span></td><td class="r mono">{{ p.wrongValue | number:'1.2-2' }}</td>
                <td>{{ p.rightTestName }} <span class="small muted">{{ p.rightTestCode }}</span></td><td class="r mono">{{ p.rightValue | number:'1.2-2' }}</td>
                <td class="r mono" [class.pos]="p.penalty > 0" [class.neg]="p.penalty < 0">{{ p.penalty | number:'1.2-2' }}</td>
                <td>{{ userLabel(p.user) }}</td>
                @if (canManage()) {
                  <td class="ar actions">
                    <button class="icon-btn" title="Edit" (click)="openEdit(p)">✎</button>
                    <button class="icon-btn del" title="Delete" (click)="remove(p)">🗑</button>
                  </td>
                }
              </tr>
            } @empty { <tr><td colspan="13" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) {
            <tfoot><tr>
              <td colspan="7">{{ 'total' | t : 'Total' }}</td>
              <td class="r mono">{{ k().wrong | number:'1.2-2' }}</td><td></td>
              <td class="r mono">{{ k().right | number:'1.2-2' }}</td>
              <td class="r mono">{{ k().penalty | number:'1.2-2' }}</td><td></td>
              @if (canManage()) { <td></td> }
            </tr></tfoot>
          }
        </table></div>
      }
    </div>

    @if (dlg()) {
      <div class="as-overlay" (click)="dlg.set(false)">
        <div class="as-dlg" (click)="$event.stopPropagation()">
          <div class="as-dlg-head"><h2>{{ editId ? ('edit' | t : 'Edit') : ('record_penalty' | t : 'Record penalty') }}</h2><button class="btn btn-mini btn-s" (click)="dlg.set(false)">✕</button></div>
          <div class="as-dlg-body">
            <div class="frm-grid" style="grid-template-columns:1fr 1fr;gap:12px">
              <div class="field"><label>{{ 'date' | t : 'Date' }} *</label><app-date-input [(ngModel)]="f.date"></app-date-input></div>
              <div class="field"><label>{{ 'day' | t : 'Day' }}</label><input class="input" [value]="day(f.date)" disabled></div>
              <div class="field" style="grid-column:1/-1"><label>{{ 'lab' | t : 'Lab' }} *</label><app-filter-select [(ngModel)]="f.laboratoryId" [options]="labOptions()" [clearable]="true" placeholder="—" [disabled]="!!editId"></app-filter-select></div>
              <div class="field"><label>{{ 'acc_no' | t : 'Acc No' }} *</label><input class="input" [(ngModel)]="f.accNo" maxlength="50"></div>
              <div class="field"><label>{{ 'patient_name' | t : 'Patient Name' }} *</label><input class="input" [(ngModel)]="f.patientName" maxlength="200"></div>
              <div class="field"><label>{{ 'wrong_test' | t : 'Wrong Test' }} *</label><app-filter-select [ngModel]="f.wrongKey" (ngModelChange)="pickTest('wrong', $event)" [options]="testOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
              <div class="field"><label>{{ 'value' | t : 'Value' }} *</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.wrongValue"></div>
              <div class="field"><label>{{ 'right_test' | t : 'Right Test' }} *</label><app-filter-select [ngModel]="f.rightKey" (ngModelChange)="pickTest('right', $event)" [options]="testOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
              <div class="field"><label>{{ 'value' | t : 'Value' }} *</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.rightValue"></div>
              <div class="field"><label>{{ 'user_role' | t : 'User' }} *</label>
                <select class="select" [(ngModel)]="f.user">@for (u of users; track u) { <option [value]="u">{{ userLabel(u) }}</option> }</select></div>
              <div class="field"><label>{{ 'penalty' | t : 'Penalty' }}</label><input class="input" [value]="(f.wrongValue ?? 0) - (f.rightValue ?? 0) | number:'1.2-2'" disabled></div>
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
export class PenaltiesComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;
  readonly users = PENALTY_USERS;

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal(false);
  readonly rows = signal<PenaltyDto[]>([]);
  readonly labs = signal<LabListItem[]>([]);
  readonly tests = signal<TestLookup[]>([]);
  from = firstOfMonth(); to = localToday(); labId = '';
  editId: string | null = null;
  f = this.blank();

  readonly labOptions = computed<Opt[]>(() => this.labs().map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` })));
  // Test codes repeat across test types in the catalogue, so the option key carries both.
  readonly testOptions = computed<Opt[]>(() => this.tests().map((t) => ({ value: `${t.code}|${t.testType}`, label: `${t.code} · ${t.name}` })));
  readonly k = computed(() => {
    const r = this.rows();
    return { count: r.length, wrong: r.reduce((a, p) => a + p.wrongValue, 0), right: r.reduce((a, p) => a + p.rightValue, 0), penalty: r.reduce((a, p) => a + p.penalty, 0) };
  });

  constructor() {
    this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({ next: (r) => this.labs.set(r.items), error: () => {} });
    this.api.get<TestLookup[]>('/test-lookup').subscribe({ next: (r) => this.tests.set(r), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  userLabel(u: string): string { return u === 'DataEntry' ? 'Data Entry' : u; }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.labId) params['laboratoryId'] = this.labId;
    this.api.get<PenaltyDto[]>('/accounting/penalties', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private blank() {
    return { date: localToday(), laboratoryId: '', accNo: '', patientName: '', wrongKey: '', wrongTestCode: '', wrongTestName: '', wrongValue: null as number | null,
      rightKey: '', rightTestCode: '', rightTestName: '', rightValue: null as number | null, user: 'Rep' };
  }
  openNew(): void { this.editId = null; this.f = this.blank(); this.dlg.set(true); }
  openEdit(p: PenaltyDto): void {
    this.editId = p.id;
    const key = (code: string) => this.tests().find((t) => t.code === code)?.testType;
    this.f = { date: p.date, laboratoryId: p.laboratoryId, accNo: p.accNo, patientName: p.patientName,
      wrongKey: `${p.wrongTestCode}|${key(p.wrongTestCode) ?? ''}`, wrongTestCode: p.wrongTestCode, wrongTestName: p.wrongTestName, wrongValue: p.wrongValue,
      rightKey: `${p.rightTestCode}|${key(p.rightTestCode) ?? ''}`, rightTestCode: p.rightTestCode, rightTestName: p.rightTestName, rightValue: p.rightValue, user: p.user };
    this.dlg.set(true);
  }
  pickTest(side: 'wrong' | 'right', key: string): void {
    const [code, type] = (key ?? '').split('|');
    const t = this.tests().find((x) => x.code === code && String(x.testType) === type) ?? this.tests().find((x) => x.code === code);
    if (side === 'wrong') { this.f.wrongKey = key; this.f.wrongTestCode = t?.code ?? ''; this.f.wrongTestName = t?.name ?? ''; }
    else { this.f.rightKey = key; this.f.rightTestCode = t?.code ?? ''; this.f.rightTestName = t?.name ?? ''; }
  }
  valid(): boolean {
    const f = this.f;
    return !!f.date && !!f.laboratoryId && !!f.accNo.trim() && !!f.patientName.trim() && !!f.wrongTestCode && !!f.rightTestCode
      && f.wrongValue !== null && f.wrongValue >= 0 && f.rightValue !== null && f.rightValue >= 0 && !!f.user;
  }
  save(): void {
    if (!this.valid()) return;
    this.busy.set(true);
    const body = { date: this.f.date, laboratoryId: this.f.laboratoryId, accNo: this.f.accNo.trim(), patientName: this.f.patientName.trim(),
      wrongTestCode: this.f.wrongTestCode, wrongTestName: this.f.wrongTestName, wrongValue: this.f.wrongValue,
      rightTestCode: this.f.rightTestCode, rightTestName: this.f.rightTestName, rightValue: this.f.rightValue, user: this.f.user };
    const req = this.editId ? this.api.put(`/accounting/penalties/${this.editId}`, body) : this.api.post('/accounting/penalties', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Penalty saved.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(p: PenaltyDto): void {
    if (!confirm(`Delete penalty #${p.serial}?`)) return;
    this.api.delete(`/accounting/penalties/${p.id}`).subscribe({ next: () => { this.toast.success('Penalty deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Serial', 'Day', 'Date', 'Lab', 'Code', 'Acc No', 'Patient Name', 'Wrong Test', 'Value', 'Right Test', 'Value', 'Penalty', 'User'];
  private exportRows() {
    return this.rows().map((p) => [p.serial, this.day(p.date), ddmy(p.date), p.labName, p.labDisplayCode, p.accNo, p.patientName,
      `${p.wrongTestName} (${p.wrongTestCode})`, money(p.wrongValue), `${p.rightTestName} (${p.rightTestCode})`, money(p.rightValue), money(p.penalty), this.userLabel(p.user)]);
  }
  exportExcel(): void { exportXlsx(`penalty-statement-${localToday()}.xlsx`, PenaltiesComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Penalty Statement (${ddmy(this.from)} → ${ddmy(this.to)})`, PenaltiesComponent.HEADER, this.exportRows()); }
}
