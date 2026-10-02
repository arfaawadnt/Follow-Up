import { Component, computed, inject, input, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { exportXlsx } from './export.util';
import { I18nService, TranslatePipe } from '../core/i18n';
import { StatementLdmDetail } from '../core/models';
import { money } from '../features/accounting/accounting.util';

/** The details dialog grouped by lab: one header per lab with its line count, fee subtotal and flag counts. */
interface DetailGroup { key: string; labName: string; labDisplayCode: string; rows: StatementLdmDetail[]; fee: number; unverified: number; within: number; over: number; }

/**
 * "LDM registrations" dialog (2026-09-30, shared): the synced registration lines behind a figure, grouped by lab, each
 * test with its sample / test status and when it was added to its registration (Within time / Within 3 Hours / After
 * 3 Hours), the not-verified and late-addition counts in the header and per lab, and an Excel export. The host passes the
 * rows (GET /accounting/statement/ldm-details) and closes it through the `closed` output.
 */
@Component({
  selector: 'app-ldm-details-dialog',
  standalone: true,
  imports: [DecimalPipe, TranslatePipe],
  template: `
    <div class="ld-overlay" (click)="closed.emit()">
      <div class="ld-dlg" (click)="$event.stopPropagation()">
        <div class="ld-head"><div><h2>{{ 'ldm_details_title' | t : 'LDM registrations' }} · {{ title() }}</h2><div class="small muted">{{ subtitle() }} · {{ groups().length }} {{ 'labs' | t : 'labs' }} · {{ rows().length }} {{ 'rows_2' | t : 'row(s)' }} · {{ total() | number:'1.2-2' }} EGP@if (unverified()) { · <span class="nv-note">{{ unverified() }} {{ 'not_verified' | t : 'not verified' }}</span> }@if (within()) { · <span class="late-note">{{ within() }} {{ 'add_within_3h' | t : 'Within 3 Hours' }}</span> }@if (over()) { · <span class="nv-note">{{ over() }} {{ 'add_after_3h' | t : 'After 3 Hours' }}</span> }</div></div><button class="btn btn-mini btn-s" (click)="closed.emit()">✕</button></div>
        <div class="ld-body">
          @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
          @else {
            <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
              <thead><tr><th>{{ 'lab' | t : 'Lab' }}</th><th>{{ 'acc_no' | t : 'Acc No' }}</th><th>{{ 'patient_name' | t : 'Patient Name' }}</th><th>{{ 'test_name_2' | t : 'Test' }}</th><th class="r">{{ 'test_fee' | t : 'Test Fee' }}</th><th>{{ 'sample_status' | t : 'Sample Status' }}</th><th>{{ 'test_status' | t : 'Test Status' }}</th><th>{{ 'test_addition' | t : 'Test Addition' }}</th></tr></thead>
              <tbody>
                @for (g of groups(); track g.key) {
                  <tr class="grp"><td colspan="4"><b>{{ g.labName }}</b> <span class="small muted">{{ g.labDisplayCode }} · {{ g.rows.length }} {{ 'rows_2' | t : 'row(s)' }}@if (g.unverified) { · <span class="nv-note">{{ g.unverified }} {{ 'not_verified' | t : 'not verified' }}</span> }@if (g.within) { · <span class="late-note">{{ g.within }} {{ 'add_within_3h' | t : 'Within 3 Hours' }}</span> }@if (g.over) { · <span class="nv-note">{{ g.over }} {{ 'add_after_3h' | t : 'After 3 Hours' }}</span> }</span></td><td class="r mono" style="font-weight:700">{{ g.fee | number:'1.2-2' }}</td><td></td><td></td><td></td></tr>
                  @for (d of g.rows; track $index) {
                    <tr [class.nv]="!isVerified(d)"><td class="small muted">{{ g.labDisplayCode }}</td><td class="mono">{{ d.accNo }}</td><td>{{ d.patientName }}</td><td>{{ d.testName || d.testCode }} <span class="small muted">{{ d.testCode }}</span></td><td class="r mono">{{ d.fee | number:'1.2-2' }}</td><td>{{ sampleLabel(d.sampleStatus) }}</td><td><span class="badge" [class]="'badge ' + (isVerified(d) ? 'b-ok' : 'b-bad')">{{ testLabel(d.testStatus) }}</span></td><td><span class="badge" [class]="'badge ' + additionClass(d.testAddition)">{{ additionLabel(d.testAddition) }}</span></td></tr>
                  }
                } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
              </tbody>
              @if (rows().length) { <tfoot><tr><td colspan="4">{{ 'total' | t : 'Total' }}</td><td class="r mono">{{ total() | number:'1.2-2' }}</td><td></td><td></td><td></td></tr></tfoot> }
            </table></div>
          }
        </div>
        <div class="ld-foot"><button class="btn btn-s" (click)="exportExcel()" [disabled]="!rows().length">{{ 'export_excel' | t : 'Export Excel' }}</button><button class="btn btn-p" (click)="closed.emit()">{{ 'close' | t : 'Close' }}</button></div>
      </div>
    </div>
  `,
  styles: [`
    .ld-overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1000}
    .ld-dlg{background:var(--white,#fff);border-radius:12px;box-shadow:0 16px 48px rgba(0,0,0,.25);width:min(96vw,1100px);max-height:92vh;display:flex;flex-direction:column}
    .ld-head{display:flex;justify-content:space-between;align-items:flex-start;gap:12px;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
    .ld-head h2{font-size:15px;margin:0 0 2px}
    .ld-body{overflow:auto;padding:0 0 8px}
    .ld-foot{display:flex;justify-content:flex-end;gap:8px;padding:12px 16px;border-top:1px solid var(--slate-150,#edebe9)}
    th.r,td.r{text-align:right}
    tr.grp td{background:var(--slate-100,#f3f2f1)} tr.nv td{background:#fef2f2;color:#b91c1c}
    .nv-note{color:#b91c1c;font-weight:600} .late-note{color:#1d4ed8;font-weight:600}
  `],
})
export class LdmDetailsDialogComponent {
  private readonly i18n = inject(I18nService);
  /** Dialog title suffix (e.g. the date) and a subtitle line (e.g. the subject); the rows to show; loading state. */
  readonly title = input('');
  readonly subtitle = input('');
  readonly rows = input<StatementLdmDetail[]>([]);
  readonly loading = input(false);
  /** Export file stem (without extension). */
  readonly fileStem = input('ldm-registrations');
  readonly closed = output<void>();

  readonly total = computed(() => money(this.rows().reduce((a, d) => a + d.fee, 0)));
  readonly groups = computed<DetailGroup[]>(() => {
    const map = new Map<string, DetailGroup>();
    for (const d of this.rows()) {
      const key = d.labDisplayCode + '|' + d.labName;
      let g = map.get(key);
      if (!g) { g = { key, labName: d.labName, labDisplayCode: d.labDisplayCode, rows: [], fee: 0, unverified: 0, within: 0, over: 0 }; map.set(key, g); }
      g.rows.push(d); g.fee = money(g.fee + d.fee); if (!this.isVerified(d)) g.unverified++;
      if (d.testAddition === 'Within3Hours') g.within++; else if (d.testAddition === 'Over3Hours') g.over++;
    }
    return [...map.values()].sort((a, b) => a.labName.localeCompare(b.labName));
  });
  readonly unverified = computed(() => this.rows().filter((d) => !this.isVerified(d)).length);
  readonly within = computed(() => this.rows().filter((d) => d.testAddition === 'Within3Hours').length);
  readonly over = computed(() => this.rows().filter((d) => d.testAddition === 'Over3Hours').length);

  /** LDM test status 5 = verified; anything else (ordered, completed, reviewed, unknown) is flagged. */
  isVerified(d: StatementLdmDetail): boolean { return Number.parseInt(d.testStatus ?? '', 10) === 5; }
  /** LDM status codes (same mapping as Detailed Statistics): sample 1 ordered / 2 collected / 3 received; test 1-2 ordered / 3 completed / 4 reviewed / 5 verified. */
  sampleLabel(code: string | null): string {
    switch (Number.parseInt(code ?? '', 10)) {
      case 1: return this.i18n.t('st_ordered', 'Ordered'); case 2: return this.i18n.t('st_collected', 'Collected'); case 3: return this.i18n.t('st_received', 'Received');
      default: return code?.trim() || '—';
    }
  }
  testLabel(code: string | null): string {
    switch (Number.parseInt(code ?? '', 10)) {
      case 1: case 2: return this.i18n.t('st_ordered', 'Ordered'); case 3: return this.i18n.t('st_completed', 'Completed');
      case 4: return this.i18n.t('st_reviewed', 'Reviewed'); case 5: return this.i18n.t('st_verified', 'Verified');
      default: return code?.trim() || '—';
    }
  }
  additionLabel(status: string | null | undefined): string {
    return status === 'Within3Hours' ? this.i18n.t('add_within_3h', 'Within 3 Hours')
      : status === 'Over3Hours' ? this.i18n.t('add_after_3h', 'After 3 Hours') : this.i18n.t('add_within_time', 'Within time');
  }
  additionClass(status: string | null | undefined): string { return status === 'Within3Hours' ? 'b-info' : status === 'Over3Hours' ? 'b-bad' : 'b-ok'; }
  exportExcel(): void {
    exportXlsx(`${this.fileStem()}.xlsx`, ['Lab', 'Code', 'Acc No', 'Patient', 'Test', 'Test code', 'Fee', 'Sample status', 'Test status', 'Verified', 'Test addition'],
      this.groups().flatMap((g) => g.rows.map((x) => [x.labName, x.labDisplayCode, x.accNo, x.patientName, x.testName ?? x.testCode, x.testCode, money(x.fee), this.sampleLabel(x.sampleStatus), this.testLabel(x.testStatus), this.isVerified(x) ? 'Yes' : 'NO', this.additionLabel(x.testAddition)])));
  }
}
