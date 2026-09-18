import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ddmy, exportXlsx, localToday, printTable } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { UiService } from '../../core/ui.service';
import { TranslatePipe } from '../../core/i18n';
import { LabLookup, PenaltyActorDto, PenaltyDto, TestLookup } from '../../core/models';
import { ACC_STYLES, PENALTY_USERS, dayName, firstOfMonth, money, penaltyUserLabel } from './accounting.util';

type Opt = { value: string; label: string };

/**
 * Penalty Statement — penalties recorded per lab (a wrong test booked instead of the right one). The penalty amount is
 * right − wrong for every user type, computed server-side: on the rep statement the right test is a debit and the wrong test a
 * credit (a Rep penalty follows the representative who made it; the other types follow the lab's Lab Responsible), and the
 * same net figure is the Penalty column of the Rep Income sheet.
 */
@Component({
  selector: 'app-acc-penalties',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent, RouterLink],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_penalties' | t : 'Penalty Statement' }}</div><h1>{{ 'acc_penalties' | t : 'Penalty Statement' }}</h1></div>
      <div class="pagehead-actions">
        @if (canManage()) { <button class="btn btn-p" (click)="openNew()">{{ 'record_penalty' | t : 'Record penalty' }}</button> }
        <a class="btn btn-s" routerLink="/accounting/penalty-report">{{ 'acc_penalty_report' | t : 'Penalty Report' }}</a>
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
      <div class="frm-grid" style="grid-template-columns:1fr 1fr 1.5fr 2fr auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [ngModel]="areaId" (ngModelChange)="pickArea($event)" [options]="areaOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'lab' | t : 'Lab' }}</label><app-filter-select [(ngModel)]="labId" [options]="filteredLabOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
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
            <th class="r">{{ 'penalty' | t : 'Penalty' }}</th><th>{{ 'user_type_user' | t : 'User Type / User' }}</th>
            @if (canManage()) { <th class="ar">{{ 'actions' | t : 'Actions' }}</th> }
          </tr></thead>
          <tbody>
            @for (p of rows(); track p.id; let i = $index) {
              <tr>
                <td class="mono">{{ i + 1 }}</td><td>{{ day(p.date) }}</td><td>{{ ddmy(p.date) }}</td>
                <td><b>{{ p.labName }}</b> <span class="small muted">{{ p.labDisplayCode }}</span></td>
                <td>{{ p.accNo }}</td><td>{{ p.patientName }}</td>
                <td>{{ p.wrongTestName || '—' }} <span class="small muted">{{ p.wrongTestCode }}</span></td><td class="r mono">{{ p.wrongValue | number:'1.2-2' }}</td>
                <td>{{ p.rightTestName || '—' }} <span class="small muted">{{ p.rightTestCode }}</span></td><td class="r mono">{{ p.rightValue | number:'1.2-2' }}</td>
                <td class="r mono" [class.pos]="p.penalty > 0" [class.neg]="p.penalty < 0">{{ p.penalty | number:'1.2-2' }}</td>
                <td>{{ userLabel(p.userType) }} / {{ p.performedByName || '—' }}</td>
                @if (canManage()) {
                  <td class="ar actions">
                    <button class="icon-btn" title="Edit" (click)="openEdit(p)">✎</button>
                    <button class="icon-btn del" title="Delete" (click)="remove(p, i + 1)">🗑</button>
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
              <div class="field"><label>{{ 'area_2' | t : 'Area' }}</label><app-filter-select [ngModel]="f.areaId" (ngModelChange)="pickDialogArea($event)" [options]="areaOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'" [disabled]="!!editId"></app-filter-select></div>
              <div class="field"><label>{{ 'lab' | t : 'Lab' }} *</label><app-filter-select [(ngModel)]="f.laboratoryId" [options]="dialogLabOptions()" [clearable]="true" placeholder="—" [disabled]="!!editId"></app-filter-select></div>
              <div class="field"><label>{{ 'acc_no' | t : 'Acc No' }} *</label><input class="input" [(ngModel)]="f.accNo" maxlength="50"></div>
              <div class="field"><label>{{ 'patient_name' | t : 'Patient Name' }} *</label><input class="input" [(ngModel)]="f.patientName" maxlength="200"></div>
              <div class="field"><label>{{ 'wrong_test' | t : 'Wrong Test' }}{{ f.userType === 'LabRequest' ? '' : ' *' }}</label><app-filter-select [ngModel]="f.wrongKey" (ngModelChange)="pickTest('wrong', $event)" [options]="testOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
              <div class="field"><label>{{ 'value' | t : 'Value' }} *</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.wrongValue"></div>
              <div class="field"><label>{{ 'right_test' | t : 'Right Test' }}{{ f.userType === 'LabRequest' ? '' : ' *' }}</label><app-filter-select [ngModel]="f.rightKey" (ngModelChange)="pickTest('right', $event)" [options]="testOptions()" [clearable]="true" placeholder="—"></app-filter-select></div>
              <div class="field"><label>{{ 'value' | t : 'Value' }} *</label><input class="input" type="number" min="0" step="0.01" [(ngModel)]="f.rightValue"></div>
              <div class="field"><label>{{ 'user_type' | t : 'User Type' }} *</label>
                <select class="select" [ngModel]="f.userType" (ngModelChange)="pickUserType($event)">@for (u of users; track u) { <option [value]="u">{{ userLabel(u) }}</option> }</select></div>
              @if (f.userType === 'LabRequest') {
                <div class="field"><label>{{ 'user' | t : 'User' }}</label><div class="small muted" style="padding-top:8px">{{ 'lab_request_hint' | t : 'The lab asked for the wrong test: the record is assigned to the lab Lab Responsible (right test = debit, wrong test = credit on the statement).' }}</div></div>
              } @else {
                <div class="field"><label>{{ 'user' | t : 'User' }} *</label>
                  <app-filter-select [(ngModel)]="f.performedById" [options]="actorOptions()" [clearable]="true" [placeholder]="'select_user' | t : 'Select…'"></app-filter-select></div>
              }
              <div class="field"><label>{{ 'penalty' | t : 'Penalty' }} <span class="small muted">right − wrong</span></label><input class="input" [value]="penaltyPreview() | number:'1.2-2'" disabled></div>
              <div class="field" style="grid-column:1/-1"><div class="small muted">{{ 'penalty_statement_hint' | t : 'On the rep statement the right test value is a debit and the wrong test value a credit, each noted with this record.' }}@if (f.userType === 'LabRequest') { {{ 'lab_request_tests_hint' | t : 'A lab request needs at least one test (wrong or right).' }} }</div></div>
            </div>
          </div>
          <div class="as-dlg-foot" style="align-items:center;gap:12px">
            @if (missing().length) { <span class="small" style="color:var(--red-600,#dc2626);margin-inline-end:auto">{{ 'missing_fields' | t : 'Missing' }}: {{ missing().join(', ') }}</span> }
            <button class="btn btn-s" (click)="dlg.set(false)">{{ 'cancel' | t : 'Cancel' }}</button>
            <button class="btn btn-p" [disabled]="busy()" (click)="save()">{{ 'save' | t : 'Save' }}</button>
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
  /** The people the "User" picker offers for the selected user type (reps for Rep, active system users otherwise). */
  readonly actors = signal<PenaltyActorDto[]>([]);
  readonly actorOptions = computed<Opt[]>(() => this.actors().map((a) => ({ value: a.id, label: a.detail ? `${a.name} (${a.detail})` : a.name })));

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly dlg = signal(false);
  readonly rows = signal<PenaltyDto[]>([]);
  readonly labs = signal<LabLookup[]>([]);
  readonly tests = signal<TestLookup[]>([]);
  from = firstOfMonth(); to = localToday(); labId = ''; areaId = '';
  editId: string | null = null;
  f = this.blank();

  readonly labOptions = computed<Opt[]>(() => this.labs().map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` })));
  readonly areas = signal<{ id: string; name: string }[]>([]);
  readonly areaOptions = computed<Opt[]>(() => this.areas().map((a) => ({ value: a.id, label: a.name })));
  private readonly pageArea = signal('');
  private readonly dialogArea = signal('');
  /** Labs carry their area by name; an area pick narrows the lab pickers (page filter and record dialog) to that area. */
  private labsOf(areaId: string): Opt[] {
    const name = this.areas().find((a) => a.id === areaId)?.name;
    return this.labs().filter((l) => !name || l.area === name).map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` }));
  }
  readonly filteredLabOptions = computed<Opt[]>(() => this.labsOf(this.pageArea()));
  readonly dialogLabOptions = computed<Opt[]>(() => this.labsOf(this.dialogArea()));
  // Test codes repeat across test types in the catalogue, so the option key carries both.
  readonly testOptions = computed<Opt[]>(() => this.tests().map((t) => ({ value: `${t.code}|${t.testType}`, label: `${t.code} · ${t.name}` })));
  readonly k = computed(() => {
    const r = this.rows();
    return { count: r.length, wrong: r.reduce((a, p) => a + p.wrongValue, 0), right: r.reduce((a, p) => a + p.rightValue, 0), penalty: r.reduce((a, p) => a + p.penalty, 0) };
  });

  constructor() {
    this.api.get<LabLookup[]>('/labs/lookup').subscribe({ next: (r) => this.labs.set(r), error: () => {} });
    this.api.get<TestLookup[]>('/test-lookup').subscribe({ next: (r) => this.tests.set(r), error: () => {} });
    this.api.get<{ id: string; name: string }[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r), error: () => {} });
    this.load();
  }

  canManage(): boolean { return this.auth.has('ManageAccounting'); }
  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  userLabel(u: string): string { return penaltyUserLabel(u); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.labId) params['laboratoryId'] = this.labId;
    if (this.areaId) params['areaId'] = this.areaId;
    this.api.get<PenaltyDto[]>('/accounting/penalties', params).subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private blank() {
    return { date: localToday(), areaId: '', laboratoryId: '', accNo: '', patientName: '', wrongKey: '', wrongTestCode: '', wrongTestName: '', wrongValue: null as number | null,
      rightKey: '', rightTestCode: '', rightTestName: '', rightValue: null as number | null, userType: 'Rep', performedById: '' };
  }
  /** Loads the picker for a user type; a type change clears the chosen person since the lists are disjoint. */
  loadActors(userType: string): void {
    this.actors.set([]);
    this.api.get<PenaltyActorDto[]>('/accounting/penalty-actors', { userType }).subscribe({ next: (r) => this.actors.set(r), error: () => {} });
  }
  pickArea(areaId: string): void { this.areaId = areaId ?? ''; this.pageArea.set(this.areaId); if (this.labId && !this.filteredLabOptions().some((o) => o.value === this.labId)) this.labId = ''; }
  pickDialogArea(areaId: string): void { this.f.areaId = areaId ?? ''; this.dialogArea.set(this.f.areaId); if (this.f.laboratoryId && !this.dialogLabOptions().some((o) => o.value === this.f.laboratoryId)) this.f.laboratoryId = ''; }
  pickUserType(userType: string): void { this.f.userType = userType; this.f.performedById = ''; this.loadActors(userType); }
  openNew(): void { this.editId = null; this.f = this.blank(); this.f.areaId = this.areaId; this.dialogArea.set(this.areaId); this.loadActors(this.f.userType); this.dlg.set(true); }
  openEdit(p: PenaltyDto): void {
    this.editId = p.id;
    const key = (code: string | null) => this.tests().find((t) => t.code === code)?.testType;
    this.dialogArea.set('');
    this.f = { date: p.date, areaId: '', laboratoryId: p.laboratoryId, accNo: p.accNo, patientName: p.patientName,
      wrongKey: p.wrongTestCode ? `${p.wrongTestCode}|${key(p.wrongTestCode) ?? ''}` : '', wrongTestCode: p.wrongTestCode ?? '', wrongTestName: p.wrongTestName ?? '', wrongValue: p.wrongTestCode ? p.wrongValue : null,
      rightKey: p.rightTestCode ? `${p.rightTestCode}|${key(p.rightTestCode) ?? ''}` : '', rightTestCode: p.rightTestCode ?? '', rightTestName: p.rightTestName ?? '', rightValue: p.rightTestCode ? p.rightValue : null,
      userType: p.userType, performedById: p.performedById ?? '' };
    this.loadActors(p.userType);
    this.dlg.set(true);
  }
  pickTest(side: 'wrong' | 'right', key: string): void {
    const [code, type] = (key ?? '').split('|');
    const t = this.tests().find((x) => x.code === code && String(x.testType) === type) ?? this.tests().find((x) => x.code === code);
    if (side === 'wrong') { this.f.wrongKey = key; this.f.wrongTestCode = t?.code ?? ''; this.f.wrongTestName = t?.name ?? ''; }
    else { this.f.rightKey = key; this.f.rightTestCode = t?.code ?? ''; this.f.rightTestName = t?.name ?? ''; }
  }
  /** The starred fields still empty or invalid — shown beside Save so a disabled save is never a mystery. */
  missing(): string[] {
    const f = this.f; const m: string[] = [];
    if (!f.date) m.push('Date');
    if (!f.laboratoryId) m.push('Lab');
    if (!f.accNo.trim()) m.push('Acc No');
    if (!f.patientName.trim()) m.push('Patient Name');
    const labRequest = f.userType === 'LabRequest';
    const badValue = (v: number | null) => v === null || v === undefined || Number(v) < 0 || isNaN(Number(v));
    if (labRequest) {
      if (!f.wrongTestCode && !f.rightTestCode) m.push('Wrong or Right Test');
      if (f.wrongTestCode && badValue(f.wrongValue)) m.push('Wrong Test value');
      if (f.rightTestCode && badValue(f.rightValue)) m.push('Right Test value');
    } else {
      if (!f.wrongTestCode) m.push('Wrong Test');
      if (badValue(f.wrongValue)) m.push('Wrong Test value');
      if (!f.rightTestCode) m.push('Right Test');
      if (badValue(f.rightValue)) m.push('Right Test value');
    }
    if (!f.userType) m.push('User Type');
    if (f.userType !== 'LabRequest' && !f.performedById) m.push('User');
    return m;
  }
  valid(): boolean { return this.missing().length === 0; }
  /** right − wrong for every user type (a missing test counts 0). */
  penaltyPreview(): number {
    const w = this.f.wrongTestCode ? (this.f.wrongValue ?? 0) : 0; const r = this.f.rightTestCode ? (this.f.rightValue ?? 0) : 0;
    return r - w;
  }
  save(): void {
    const missing = this.missing();
    if (missing.length) { this.toast.warning(`Please fill: ${missing.join(', ')}`); return; }
    this.busy.set(true);
    const body = { date: this.f.date, laboratoryId: this.f.laboratoryId, accNo: this.f.accNo.trim(), patientName: this.f.patientName.trim(),
      // A test left empty (allowed for a lab request) goes as null with a zero value.
      wrongTestCode: this.f.wrongTestCode || null, wrongTestName: this.f.wrongTestCode ? this.f.wrongTestName : null, wrongValue: this.f.wrongTestCode ? (this.f.wrongValue ?? 0) : 0,
      rightTestCode: this.f.rightTestCode || null, rightTestName: this.f.rightTestCode ? this.f.rightTestName : null, rightValue: this.f.rightTestCode ? (this.f.rightValue ?? 0) : 0, userType: this.f.userType,
      // Exactly one of the two, matching the type (none for a lab request) — the server enforces the same rule.
      performedByRepId: this.f.userType === 'Rep' ? this.f.performedById : null,
      performedByUserId: this.f.userType === 'Rep' || this.f.userType === 'LabRequest' ? null : this.f.performedById || null };
    const req = this.editId ? this.api.put(`/accounting/penalties/${this.editId}`, body) : this.api.post('/accounting/penalties', body);
    req.subscribe({ next: () => { this.busy.set(false); this.dlg.set(false); this.toast.success('Penalty saved.'); this.load(); }, error: () => this.busy.set(false) });
  }
  remove(p: PenaltyDto, rowNo: number): void {
    if (!confirm(`Delete penalty #${rowNo} (${ddmy(p.date)} · ${p.labName} · Acc ${p.accNo})?`)) return;
    this.api.delete(`/accounting/penalties/${p.id}`).subscribe({ next: () => { this.toast.success('Penalty deleted.'); this.load(); } });
  }

  private static readonly HEADER = ['Serial', 'Day', 'Date', 'Lab', 'Code', 'Acc No', 'Patient Name', 'Wrong Test', 'Value', 'Right Test', 'Value', 'Penalty', 'User Type / User'];
  private exportRows() {
    return this.rows().map((p, i) => [i + 1, this.day(p.date), ddmy(p.date), p.labName, p.labDisplayCode, p.accNo, p.patientName,
      p.wrongTestCode ? `${p.wrongTestName} (${p.wrongTestCode})` : '', money(p.wrongValue), p.rightTestCode ? `${p.rightTestName} (${p.rightTestCode})` : '', money(p.rightValue), money(p.penalty), `${this.userLabel(p.userType)} / ${p.performedByName ?? '—'}`]);
  }
  exportExcel(): void { exportXlsx(`penalty-statement-${localToday()}.xlsx`, PenaltiesComponent.HEADER, this.exportRows()); }
  exportPdf(): void { printTable(`Penalty Statement (${ddmy(this.from)} → ${ddmy(this.to)})`, PenaltiesComponent.HEADER, this.exportRows()); }
}
