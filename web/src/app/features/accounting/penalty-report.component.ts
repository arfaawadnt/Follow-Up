import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ddmy, escHtml, exportXlsx, localToday } from '../../shared/export.util';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { ApiService } from '../../core/api.service';
import { UiService } from '../../core/ui.service';
import { TranslatePipe } from '../../core/i18n';
import { LabLookup, PenaltyDto } from '../../core/models';
import { ACC_STYLES, PENALTY_USERS, dayName, firstOfMonth, ldmStatusClass, ldmStatusLabel, money, penaltyUserLabel } from './accounting.util';

type Opt = { value: string; label: string };
interface Group { userType: string; label: string; rows: PenaltyDto[]; count: number; wrong: number; right: number; penalty: number; }

/**
 * Penalty Report — the penalties that are our side's fault (Rep / Data Entry / Technician), grouped by user type with
 * subtotals and a grand total, filterable by date range, user type and lab. Lab-request penalties are excluded here: they
 * are charged to the lab and appear on the Rep Income sheet. "Print" renders a formal report (header, filters, one
 * section per user type, subtotals, grand total, signature lines) in the browser's print dialog.
 */
@Component({
  selector: 'app-acc-penalty-report',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe, DateInputComponent, FilterSelectComponent, RouterLink],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'accounting' | t : 'Accounting' }} / {{ 'acc_penalty_report' | t : 'Penalty Report' }}</div><h1>{{ 'acc_penalty_report' | t : 'Penalty Report' }}</h1></div>
      <div class="pagehead-actions">
        <a class="btn btn-s" routerLink="/accounting/penalties">{{ 'acc_penalties' | t : 'Penalty Statement' }}</a>
        <button class="btn btn-p" [disabled]="!rows().length" (click)="print()">{{ 'print' | t : 'Print' }}</button>
        <button class="btn btn-s" [disabled]="!rows().length" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(5,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">{{ 'total_penalty' | t : 'Total penalty' }}</div><div class="val">{{ k().penalty | number:'1.2-2' }}</div><div class="sub">EGP · {{ k().count }} {{ 'entries' | t : 'Entries' }}</div></div>
      @for (g of groups(); track g.userType) {
        <div class="kpi kpi-blue"><div class="lbl">{{ g.label }}</div><div class="val">{{ g.penalty | number:'1.2-2' }}</div><div class="sub">{{ g.count }} {{ 'entries' | t : 'Entries' }}</div></div>
      }
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:1fr 1fr 1fr 2fr 1fr;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'user_type' | t : 'User Type' }}</label>
          <select class="select" [(ngModel)]="userType"><option value="">{{ 'all' | t : 'All' }}</option>@for (u of staffUsers; track u) { <option [value]="u">{{ label(u) }}</option> }</select></div>
        <div class="field"><label>{{ 'lab' | t : 'Lab' }}</label><app-filter-select [(ngModel)]="labId" [options]="labOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      <div class="small muted" style="margin-top:8px">{{ 'pr_scope_hint' | t : 'Rep, Data Entry and Technician penalties grouped by user type. Lab-request penalties are charged to the lab and appear on Rep Income.' }}</div>
    </div>

    <div class="card" style="padding:10px 0;overflow-x:auto">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr>
            <th>{{ 'serial' | t : 'Serial' }}</th><th>{{ 'date' | t : 'Date' }}</th><th>{{ 'day' | t : 'Day' }}</th><th>{{ 'lab' | t : 'Lab' }}</th>
            <th>{{ 'acc_no' | t : 'Acc No' }}</th><th>{{ 'patient_name' | t : 'Patient Name' }}</th>
            <th>{{ 'wrong_test' | t : 'Wrong Test' }}</th><th class="r">{{ 'value' | t : 'Value' }}</th>
            <th>{{ 'right_test' | t : 'Right Test' }}</th><th class="r">{{ 'value' | t : 'Value' }}</th>
            <th class="r">{{ 'penalty' | t : 'Penalty' }}</th><th>{{ 'pr_performed_by' | t : 'Performed by' }}</th><th>{{ 'ldm_check' | t : 'LDM check' }}</th>
          </tr></thead>
          <tbody>
            @for (g of groups(); track g.userType) {
              <tr class="grp"><td colspan="13"><b>{{ g.label }}</b> <span class="small muted">· {{ g.count }} {{ 'entries' | t : 'Entries' }}</span></td></tr>
              @for (p of g.rows; track p.id; let i = $index) {
                <tr>
                  <td class="mono">{{ i + 1 }}</td><td>{{ ddmy(p.date) }}</td><td>{{ day(p.date) }}</td>
                  <td><b>{{ p.labName }}</b> <span class="small muted">{{ p.labDisplayCode }}</span></td>
                  <td class="mono">{{ p.accNo }}</td><td>{{ p.patientName }}</td>
                  <td>{{ p.wrongTestName || '—' }} <span class="small muted">({{ p.wrongTestCode }})</span></td><td class="r mono">{{ p.wrongValue | number:'1.2-2' }}</td>
                  <td>{{ p.rightTestName || '—' }} <span class="small muted">({{ p.rightTestCode }})</span></td><td class="r mono">{{ p.rightValue | number:'1.2-2' }}</td>
                  <td class="r mono" [class.neg]="p.penalty > 0" [class.pos]="p.penalty < 0" style="font-weight:700">{{ p.penalty | number:'1.2-2' }}</td>
                  <td>{{ p.performedByName || (p.userType === 'LabRequest' && p.responsibleRepName ? (p.responsibleRepName + ' (' + ('lab_responsible' | t : 'Lab Responsible') + ')') : '—') }}</td>
                  <td><span class="badge" [class]="'badge ' + ldmClass(p.ldmStatus)" [title]="p.ldmNote || ''">{{ ldmLabel(p.ldmStatus) }}</span>@if (p.ldmNote) { <div class="small muted">{{ p.ldmNote }}</div> }</td>
                </tr>
              }
              <tr class="sub"><td colspan="7">{{ 'pr_subtotal' | t : 'Subtotal' }} · {{ g.label }}</td><td class="r mono">{{ g.wrong | number:'1.2-2' }}</td><td></td><td class="r mono">{{ g.right | number:'1.2-2' }}</td><td class="r mono" style="font-weight:700">{{ g.penalty | number:'1.2-2' }}</td><td></td><td></td></tr>
            } @empty { <tr><td colspan="13" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
          @if (rows().length) {
            <tfoot><tr><td colspan="7">{{ 'pr_grand_total' | t : 'Grand total' }}</td><td class="r mono">{{ k().wrong | number:'1.2-2' }}</td><td></td><td class="r mono">{{ k().right | number:'1.2-2' }}</td><td class="r mono">{{ k().penalty | number:'1.2-2' }}</td><td></td><td></td></tr></tfoot>
          }
        </table></div>
      }
    </div>
  `,
  styles: [ACC_STYLES, `tr.grp td{background:var(--slate-100,#f3f2f1)} tr.sub td{background:var(--slate-50,#faf9f8);font-weight:600}`],
})
export class PenaltyReportComponent {
  private readonly api = inject(ApiService);
  private readonly ui = inject(UiService);
  readonly ddmy = ddmy;
  readonly staffUsers = PENALTY_USERS;
  readonly ldmLabel = ldmStatusLabel; readonly ldmClass = ldmStatusClass;

  readonly loading = signal(true);
  readonly all = signal<PenaltyDto[]>([]);
  readonly labs = signal<LabLookup[]>([]);
  from = firstOfMonth(); to = localToday(); userType = ''; labId = '';
  private readonly filterType = signal('');

  readonly labOptions = computed<Opt[]>(() => this.labs().map((l) => ({ value: l.id, label: `${l.displayCode} · ${l.name}` })));
  /** Every user type (Rep / DataEntry / Technician / LabRequest — 2026-09-18), narrowed to the chosen type. */
  readonly rows = computed(() => this.all().filter((p) => !this.filterType() || p.userType === this.filterType()));
  readonly groups = computed<Group[]>(() => PENALTY_USERS
    .map((u) => {
      const rows = this.rows().filter((p) => p.userType === u).sort((a, b) => a.date.localeCompare(b.date) || a.labName.localeCompare(b.labName));
      return { userType: u, label: penaltyUserLabel(u), rows, count: rows.length,
        wrong: money(rows.reduce((s, p) => s + p.wrongValue, 0)), right: money(rows.reduce((s, p) => s + p.rightValue, 0)), penalty: money(rows.reduce((s, p) => s + p.penalty, 0)) };
    })
    .filter((g) => g.count > 0));
  readonly k = computed(() => {
    const r = this.rows();
    return { count: r.length, wrong: money(r.reduce((a, p) => a + p.wrongValue, 0)), right: money(r.reduce((a, p) => a + p.rightValue, 0)), penalty: money(r.reduce((a, p) => a + p.penalty, 0)) };
  });

  constructor() {
    this.api.get<LabLookup[]>('/labs/lookup').subscribe({ next: (r) => this.labs.set(r), error: () => {} });
    this.load();
  }

  day(d: string | null): string { return dayName(d, this.ui.lang()); }
  label(u: string): string { return penaltyUserLabel(u); }

  load(): void {
    this.loading.set(true);
    this.filterType.set(this.userType);
    const params: Record<string, string> = { from: this.from, to: this.to };
    if (this.labId) params['laboratoryId'] = this.labId;
    this.api.get<PenaltyDto[]>('/accounting/penalties', params).subscribe({ next: (r) => { this.all.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }

  private static readonly HEADER = ['User Type', 'Serial', 'Date', 'Day', 'Lab', 'Code', 'Acc No', 'Patient Name', 'Wrong Test', 'Value', 'Right Test', 'Value', 'Penalty', 'Performed by', 'LDM check'];
  exportExcel(): void {
    const rows = this.groups().flatMap((g) => [
      ...g.rows.map((p, i) => [g.label, i + 1, ddmy(p.date), this.day(p.date), p.labName, p.labDisplayCode, p.accNo, p.patientName,
        `${p.wrongTestName ?? ''} (${p.wrongTestCode ?? ''})`, money(p.wrongValue), `${p.rightTestName ?? ''} (${p.rightTestCode ?? ''})`, money(p.rightValue), money(p.penalty),
        p.performedByName ?? (p.userType === 'LabRequest' ? (p.responsibleRepName ?? '') : ''), ldmStatusLabel(p.ldmStatus) + (p.ldmNote ? ` — ${p.ldmNote}` : '')]),
      [`Subtotal · ${g.label}`, '', '', '', '', '', '', '', '', g.wrong, '', g.right, g.penalty, ''],
    ]);
    rows.push(['Grand total', '', '', '', '', '', '', '', '', this.k().wrong, '', this.k().right, this.k().penalty, '']);
    exportXlsx(`penalty-report-${localToday()}.xlsx`, PenaltyReportComponent.HEADER, rows);
  }

  /** Formal printable report: header + filters, one section per user type with its subtotal, grand total, signatures. */
  print(): void {
    const e = escHtml; const n = (v: number) => v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const labName = this.labId ? (this.labs().find((l) => l.id === this.labId)?.name ?? '') : 'All labs';
    const sections = this.groups().map((g) => `
      <h2>${e(g.label)} <span class="cnt">${g.count} record(s)</span></h2>
      <table><thead><tr><th>#</th><th>Date</th><th>Lab</th><th>Acc No</th><th>Patient</th><th>Wrong test</th><th class="r">Value</th><th>Right test</th><th class="r">Value</th><th class="r">Penalty</th><th>Performed by</th></tr></thead>
      <tbody>${g.rows.map((p, i) => `<tr><td>${i + 1}</td><td>${e(ddmy(p.date))}</td><td>${e(p.labName)} <small>${e(p.labDisplayCode)}</small></td><td>${e(p.accNo)}</td><td>${e(p.patientName)}</td>
        <td>${e(p.wrongTestName ?? '')} <small>(${e(p.wrongTestCode ?? '')})</small></td><td class="r">${n(p.wrongValue)}</td><td>${e(p.rightTestName ?? '')} <small>(${e(p.rightTestCode ?? '')})</small></td><td class="r">${n(p.rightValue)}</td>
        <td class="r b">${n(p.penalty)}</td><td>${e(p.performedByName ?? '—')}</td></tr>`).join('')}</tbody>
      <tfoot><tr><td colspan="6">Subtotal · ${e(g.label)}</td><td class="r">${n(g.wrong)}</td><td></td><td class="r">${n(g.right)}</td><td class="r b">${n(g.penalty)}</td><td></td></tr></tfoot></table>`).join('');
    const html = `<!doctype html><html><head><title>Penalty Report</title><style>
      body{font:12px system-ui,sans-serif;padding:20px;color:#111}
      .hdr{display:flex;justify-content:space-between;align-items:flex-end;border-bottom:2px solid #333;padding-bottom:8px;margin-bottom:12px}
      h1{font-size:20px;margin:0}.meta{font-size:11px;color:#444;text-align:right}
      .filters{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:0 0 14px;font-size:11.5px}.filters div{border:1px solid #ddd;padding:6px 8px;border-radius:4px}.filters b{display:block;color:#666;font-weight:600;font-size:10.5px}
      h2{font-size:14px;margin:18px 0 6px;border-left:4px solid #0078D4;padding-left:8px}.cnt{font-size:11px;color:#666;font-weight:400;margin-left:8px}
      table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:5px 7px;text-align:left;vertical-align:top}th{background:#f1f5f9}small{color:#666}
      .r{text-align:right;white-space:nowrap}.b{font-weight:700}tfoot td{background:#fafafa;font-weight:600}
      .grand{margin-top:16px;border-top:2px solid #333;padding-top:8px;display:flex;justify-content:flex-end;gap:32px;font-size:13px}.grand b{font-size:15px}
      .sign{display:grid;grid-template-columns:repeat(3,1fr);gap:24px;margin-top:48px;font-size:11.5px}.sign div{border-top:1px solid #333;padding-top:6px;text-align:center}
      @media print{@page{size:A4 landscape;margin:12mm}}
    </style></head><body>
    <div class="hdr"><div><h1>Penalty Report</h1><div style="font-size:12px;color:#444">Penalties by user type (Rep / Data Entry / Technician)</div></div>
      <div class="meta">Period ${e(ddmy(this.from))} → ${e(ddmy(this.to))}<br>Generated ${e(new Date().toLocaleString('en-GB'))}</div></div>
    <div class="filters"><div><b>User type</b>${e(this.userType ? penaltyUserLabel(this.userType) : 'All')}</div><div><b>Lab</b>${e(labName)}</div><div><b>Records</b>${this.k().count}</div><div><b>Total penalty</b>${n(this.k().penalty)} EGP</div></div>
    ${sections || '<p>No records.</p>'}
    <div class="grand"><span>Wrong value: ${n(this.k().wrong)}</span><span>Right value: ${n(this.k().right)}</span><span>Grand total penalty: <b>${n(this.k().penalty)} EGP</b></span></div>
    <div class="sign"><div>Prepared by</div><div>Reviewed by</div><div>Approved by</div></div>
    </body></html>`;
    const w = window.open('', '_blank'); if (!w) return;
    w.document.write(html); w.document.close(); w.focus(); w.print();
  }
}
