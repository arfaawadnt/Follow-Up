import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuditFacets, AuditRow, PagedResult } from '../../core/models';
import { DateInputComponent } from '../../shared/date-input.component';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { AuditLogButtonComponent } from '../../shared/audit-log-button.component';
import { ddmy, exportXlsx, localToday } from '../../shared/export.util';
import { TranslatePipe } from '../../core/i18n';

type Opt = { value: string; label: string };

/**
 * Audit Trail (SRS FR-20; 2026-09-20 full module): the immutable log of every create / update / delete of a business
 * record — written by the server inside the same transaction, automated jobs included — searchable by date range, record
 * type, user, action and record id, with the changed fields (before → after) expanded per row. The same history is
 * reachable per record through the 🕓 button beside any row on any page.
 */
@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [FormsModule, DatePipe, TranslatePipe, DateInputComponent, FilterSelectComponent],
  styles: [`
    tr.exp td{background:var(--slate-50,#faf9f8)}
    table.diff{border-collapse:collapse;width:100%;font-size:12px;margin:2px 0}
    table.diff th,table.diff td{border:1px solid var(--slate-150,#edebe9);padding:3px 7px;text-align:left;vertical-align:top;word-break:break-word}
    table.diff th{background:var(--white,#fff);color:var(--slate-700,#605e5c);font-weight:600}
    td.old{color:#b91c1c}td.new{color:#15803d}
    .badge.c{background:#dcfce7;color:#15803d}.badge.u{background:#e0efff;color:#005a9e}.badge.d{background:#fee2e2;color:#b91c1c}
    .pager{display:flex;gap:8px;align-items:center;justify-content:flex-end;padding:10px 14px;border-top:1px solid var(--slate-150,#edebe9);font-size:12px}
  `],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'audit_trail' | t : 'Audit trail' }}</div><h1>{{ 'audit_trail' | t : 'Audit trail' }}</h1></div>
      <div class="pagehead-actions"><button class="btn btn-s" [disabled]="!rows().length" (click)="exportExcel()">{{ 'export_excel' | t : 'Export Excel' }}</button></div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'entries' | t : 'Entries' }}</div><div class="val">{{ total() }}</div><div class="sub">{{ 'in_range' | t : 'in the selected range' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'audit_created' | t : 'Created' }}</div><div class="val">{{ k().c }}</div><div class="sub">{{ 'this_page' | t : 'this page' }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">{{ 'audit_updated' | t : 'Updated' }}</div><div class="val">{{ k().u }}</div><div class="sub">{{ 'this_page' | t : 'this page' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'audit_deleted' | t : 'Deleted' }}</div><div class="val">{{ k().d }}</div><div class="sub">{{ 'this_page' | t : 'this page' }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:1fr 1fr 1.4fr 1.2fr 1fr 1.2fr auto;gap:12px;align-items:end">
        <div class="field"><label>{{ 'start_date' | t }}</label><app-date-input [(ngModel)]="from"></app-date-input></div>
        <div class="field"><label>{{ 'end_date' | t }}</label><app-date-input [(ngModel)]="to"></app-date-input></div>
        <div class="field"><label>{{ 'entity' | t : 'Entity' }}</label><app-filter-select [(ngModel)]="entity" [options]="entityOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'user' | t : 'User' }}</label><app-filter-select [(ngModel)]="actor" [options]="actorOptions()" [clearable]="true" [placeholder]="'all' | t : 'All'"></app-filter-select></div>
        <div class="field"><label>{{ 'action' | t : 'Action' }}</label>
          <select class="select" [(ngModel)]="action"><option value="">{{ 'all' | t : 'All' }}</option><option value="Create">{{ 'audit_created' | t : 'Created' }}</option><option value="Update">{{ 'audit_updated' | t : 'Updated' }}</option><option value="Delete">{{ 'audit_deleted' | t : 'Deleted' }}</option></select></div>
        <div class="field"><label>{{ 'audit_entity_id' | t : 'Record ID' }}</label><input class="input mono" [(ngModel)]="entityId" [placeholder]="'search' | t : 'Search…'"></div>
        <div class="field"><button class="btn btn-p" (click)="load(1)" style="height:36px">{{ 'apply_filters' | t : 'Apply Filters' }}</button></div>
      </div>
      <div class="small muted" style="margin-top:8px">{{ 'audit_hint' | t : 'Every create, update and delete of a business record is written to the immutable audit trail by the server itself, including automated jobs. Use the per-record 🕓 button on any page for one record\\'s full history.' }}</div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div class="grid-scroll"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th style="width:28px"></th><th>{{ 'action_time' | t : 'Action Time' }}</th><th>{{ 'action_by' | t : 'Action By' }}</th><th>{{ 'entity' | t : 'Entity' }}</th><th>{{ 'audit_entity_id' | t : 'Record ID' }}</th><th>{{ 'action' | t : 'Action' }}</th><th>{{ 'audit_changes' | t : 'Changes' }}</th></tr></thead>
          <tbody>
            @for (a of rows(); track a.id) {
              <tr class="clickable" [class.exp]="expanded() === a.id" (click)="toggle(a.id)">
                <td class="small muted">{{ expanded() === a.id ? '▾' : '▸' }}</td>
                <td class="mono small">{{ a.occurredAt | date:'dd/MM/yyyy HH:mm:ss' }}</td>
                <td>{{ a.actor }}</td>
                <td>{{ pretty(a.entity) }}</td>
                <td class="mono small" [title]="a.entityId">{{ a.entityId.slice(0, 8) }}</td>
                <td><span class="badge" [class]="'badge ' + cls(a.action)">{{ actionLabel(a.action) }}</span></td>
                <td class="small">{{ summary(a) }}</td>
              </tr>
              @if (expanded() === a.id) {
                <tr class="exp"><td></td><td colspan="6">
                  @if (a.changes?.length) {
                    <div class="grid-scroll"><table class="diff"><thead><tr><th style="width:26%">{{ 'field' | t : 'Field' }}</th><th>{{ 'before' | t : 'Before' }}</th><th>{{ 'after' | t : 'After' }}</th></tr></thead>
                    <tbody>@for (c of a.changes; track c.field) { <tr><td>{{ pretty(c.field) }}</td><td class="old">{{ c.before ?? '—' }}</td><td class="new">{{ c.after ?? '—' }}</td></tr> }</tbody></table></div>
                  } @else { <span class="small muted">{{ 'no_field_changes' | t : 'No field changes recorded.' }}</span> }
                  @if (a.correlationId) { <div class="small muted mono" style="margin-top:4px">corr {{ a.correlationId }}</div> }
                </td></tr>
              }
            } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:24px">{{ 'no_records_found' | t : 'No records.' }}</td></tr> }
          </tbody>
        </table></div>
        <div class="pager">
          <span>{{ total() }} {{ 'total' | t }} · {{ 'page' | t : 'Page' }} {{ page() }} / {{ pages() }}</span>
          <button class="btn btn-mini btn-s" [disabled]="page() <= 1" (click)="load(page() - 1)">‹</button>
          <button class="btn btn-mini btn-s" [disabled]="page() >= pages()" (click)="load(page() + 1)">›</button>
        </div>
      }
    </div>
  `,
})
export class AuditComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<AuditRow[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = 50;
  readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize)));
  readonly expanded = signal<string | null>(null);
  readonly facets = signal<AuditFacets>({ entities: [], actors: [] });
  readonly entityOptions = computed<Opt[]>(() => this.facets().entities.map((e) => ({ value: e, label: this.pretty(e) })));
  readonly actorOptions = computed<Opt[]>(() => this.facets().actors.map((a) => ({ value: a, label: a })));
  readonly k = computed(() => {
    const r = this.rows();
    return { c: r.filter((a) => a.action === 'Create').length, u: r.filter((a) => a.action === 'Update').length, d: r.filter((a) => a.action === 'Delete').length };
  });
  from = (() => { const d = new Date(); d.setDate(d.getDate() - 7); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 10); })();
  to = localToday(); entity = ''; actor = ''; action = ''; entityId = '';

  constructor() {
    this.api.get<AuditFacets>('/audit/facets').subscribe({ next: (f) => this.facets.set(f), error: () => {} });
    this.load(1);
  }

  load(page: number): void {
    this.loading.set(true); this.page.set(page); this.expanded.set(null);
    const params: Record<string, string | number> = { page, pageSize: this.pageSize, from: this.from, to: this.to };
    if (this.entity) params['entity'] = this.entity;
    if (this.actor) params['actor'] = this.actor;
    if (this.action) params['action'] = this.action;
    if (this.entityId.trim()) params['entityId'] = this.entityId.trim();
    this.api.get<PagedResult<AuditRow>>('/audit', params).subscribe({
      next: (r) => { this.rows.set(r.items); this.total.set(r.total); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
  toggle(id: string): void { this.expanded.set(this.expanded() === id ? null : id); }
  pretty(name: string): string { return AuditLogButtonComponent.prettify(name); }
  cls(action: string): string { return action === 'Create' ? 'c' : action === 'Update' ? 'u' : action === 'Delete' ? 'd' : 'b-neu'; }
  actionLabel(action: string): string { return action === 'Create' ? 'Created' : action === 'Update' ? 'Updated' : action === 'Delete' ? 'Deleted' : action; }
  /** One line per row: the first changed fields ("Status: Pending → Visited · Samples: — → 12") and how many more. */
  summary(a: AuditRow): string {
    const ch = a.changes ?? [];
    if (!ch.length) return '—';
    const shown = ch.slice(0, 3).map((c) => a.action === 'Create' ? `${this.pretty(c.field)}: ${c.after ?? '—'}` : a.action === 'Delete' ? `${this.pretty(c.field)}: ${c.before ?? '—'}` : `${this.pretty(c.field)}: ${c.before ?? '—'} → ${c.after ?? '—'}`);
    return shown.join(' · ') + (ch.length > 3 ? ` · +${ch.length - 3}` : '');
  }
  exportExcel(): void {
    const header = ['Time', 'User', 'Entity', 'Record ID', 'Action', 'Field', 'Before', 'After'];
    const rows = this.rows().flatMap((a) => (a.changes?.length ? a.changes : [{ field: '', before: null, after: null }])
      .map((c) => [a.occurredAt.replace('T', ' ').slice(0, 19), a.actor, this.pretty(a.entity), a.entityId, a.action, this.pretty(c.field), c.before ?? '', c.after ?? '']));
    exportXlsx(`audit-trail-${ddmy(this.from)}-${ddmy(this.to)}.xlsx`, header, rows);
  }
}
