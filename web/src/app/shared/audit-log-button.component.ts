import { Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { TranslatePipe } from '../core/i18n';
import { AuditChange, AuditRow } from '../core/models';

/**
 * The per-record "log" button (2026-09-20, audit trail module): an icon button next to a row's actions that opens the
 * record's full history — who created it, who changed it, when, and exactly which fields changed (before → after) —
 * straight from the immutable audit trail. Rendered only for users holding ViewAuditTrail; `entity` is the aggregate's
 * class name (the audit trail's entity key) and `id` the record id.
 */
@Component({
  selector: 'app-audit-log',
  standalone: true,
  imports: [DatePipe, TranslatePipe],
  styles: [`
    .overlay{position:fixed;inset:0;background:rgba(15,23,42,.45);display:flex;align-items:center;justify-content:center;z-index:1100;padding:16px}
    .dlg{background:var(--white,#fff);border-radius:12px;width:min(94vw,760px);max-height:90vh;display:flex;flex-direction:column;box-shadow:0 16px 48px rgba(0,0,0,.25);border:1px solid var(--slate-150,#edebe9)}
    .head{display:flex;justify-content:space-between;align-items:center;padding:14px 16px;border-bottom:1px solid var(--slate-150,#edebe9)}
    .head h2{font-size:15px;margin:0}.head .sub{font-size:12px;color:var(--slate-500,#8a8886);margin-top:2px}
    .body{padding:12px 16px;overflow:auto}
    .item{display:grid;grid-template-columns:22px 1fr;gap:10px;padding:10px 0;border-bottom:1px solid var(--slate-100,#f3f2f1)}
    .item:last-child{border-bottom:none}
    .dot{width:12px;height:12px;border-radius:50%;margin-top:4px;background:var(--slate-300,#c8c6c4)}
    .dot.c{background:#15803d}.dot.u{background:#0078D4}.dot.d{background:#b91c1c}
    .meta{display:flex;flex-wrap:wrap;gap:8px;align-items:center;font-size:13px}
    .meta b{font-weight:700}.meta .when{color:var(--slate-500,#8a8886);font-variant-numeric:tabular-nums}
    .badge{border-radius:10px;padding:1px 8px;font-size:11px;font-weight:600;background:var(--slate-100,#f3f2f1)}
    .badge.c{background:#dcfce7;color:#15803d}.badge.u{background:#e0efff;color:#005a9e}.badge.d{background:#fee2e2;color:#b91c1c}
    table{border-collapse:collapse;width:100%;margin-top:6px;font-size:12px}
    th,td{border:1px solid var(--slate-150,#edebe9);padding:4px 7px;text-align:left;vertical-align:top;word-break:break-word}
    th{background:var(--slate-50,#faf9f8);color:var(--slate-700,#605e5c);font-weight:600}
    td.old{color:#b91c1c}td.new{color:#15803d}
    .empty{padding:24px;text-align:center;color:var(--slate-500,#8a8886)}
    .log-btn{display:inline-flex;align-items:center;gap:4px;border:1px solid var(--slate-300,#c8c6c4);background:var(--white,#fff);color:var(--slate-700,#605e5c);border-radius:6px;padding:2px 8px;font-size:11.5px;font-weight:600;cursor:pointer;white-space:nowrap;line-height:1.4;vertical-align:middle}
    .log-btn:hover{border-color:var(--primary-blue,#0078D4);color:var(--primary-blue,#0078D4)}
  `],
  template: `
    @if (visible()) {
      <button class="log-btn" type="button" [title]="'audit_log' | t : 'Change log'" (click)="open($event)">🕓 {{ 'log_short' | t : 'Log' }}</button>
    }
    @if (dlg()) {
      <div class="overlay" (click)="dlg.set(false)">
        <div class="dlg" (click)="$event.stopPropagation()">
          <div class="head">
            <div><h2>{{ 'audit_log' | t : 'Change log' }}</h2><div class="sub">{{ label() || entityLabel() }} · {{ rows().length }} {{ 'entries' | t : 'Entries' }}</div></div>
            <button class="btn btn-mini btn-s" type="button" (click)="dlg.set(false)">✕</button>
          </div>
          <div class="body">
            @if (loading()) { <div class="empty">{{ 'loading' | t : 'Loading…' }}</div> }
            @else {
              @for (r of rows(); track r.id) {
                <div class="item">
                  <div class="dot" [class]="'dot ' + cls(r.action)"></div>
                  <div>
                    <div class="meta">
                      <span class="badge" [class]="'badge ' + cls(r.action)">{{ actionLabel(r.action) }}</span>
                      <b>{{ r.actor }}</b>
                      <span class="when">{{ r.occurredAt | date:'dd/MM/yyyy HH:mm:ss' }}</span>
                    </div>
                    @if (r.changes?.length) {
                      <table>
                        <thead><tr><th style="width:28%">{{ 'field' | t : 'Field' }}</th><th>{{ 'before' | t : 'Before' }}</th><th>{{ 'after' | t : 'After' }}</th></tr></thead>
                        <tbody>
                          @for (c of r.changes; track c.field) {
                            <tr><td>{{ fieldLabel(c.field) }}</td><td class="old">{{ c.before ?? '—' }}</td><td class="new">{{ c.after ?? '—' }}</td></tr>
                          }
                        </tbody>
                      </table>
                    } @else { <div class="small muted" style="margin-top:4px">{{ 'no_field_changes' | t : 'No field changes recorded.' }}</div> }
                  </div>
                </div>
              } @empty { <div class="empty">{{ 'no_audit_trail_logs' | t : 'No log entries for this record yet.' }}</div> }
            }
          </div>
        </div>
      </div>
    }
  `,
})
export class AuditLogButtonComponent {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  /** The aggregate's class name — the audit trail's entity key (e.g. 'PenaltyRecord', 'Laboratory'). */
  readonly entity = input.required<string>();
  readonly id = input.required<string>();
  /** Optional caption for the dialog (the record's name / number). */
  readonly label = input<string>('');

  readonly dlg = signal(false);
  readonly loading = signal(false);
  readonly rows = signal<AuditRow[]>([]);
  readonly visible = computed(() => this.auth.has('ViewAuditTrail'));
  readonly entityLabel = computed(() => AuditLogButtonComponent.prettify(this.entity()));

  open(ev: Event): void {
    ev.stopPropagation();
    this.dlg.set(true); this.loading.set(true); this.rows.set([]);
    this.api.get<AuditRow[]>('/audit/entity', { entity: this.entity(), entityId: this.id() })
      .subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  cls(action: string): string { return action === 'Create' ? 'c' : action === 'Update' ? 'u' : action === 'Delete' ? 'd' : ''; }
  actionLabel(action: string): string { return action === 'Create' ? 'Created' : action === 'Update' ? 'Updated' : action === 'Delete' ? 'Deleted' : action; }
  fieldLabel(field: string): string { return AuditLogButtonComponent.prettify(field); }
  /** "PerformedByRepId" → "Performed By Rep"; "AccNo" → "Acc No". */
  static prettify(name: string): string {
    return name.replace(/Id$/, '').replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2').trim();
  }
}

export type { AuditChange };
