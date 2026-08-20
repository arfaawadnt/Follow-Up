import { Component, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { PagedResult } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface AuditRow { id: string; occurredAt: string; actor: string; entity: string; entityId: string; action: string; before: string | null; after: string | null; correlationId: string | null; }

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [FormsModule, DatePipe, SlicePipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'audit_trail' | t : 'Audit trail' }}</div><h1>{{ 'audit_trail' | t : 'Audit trail' }}</h1></div></div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'entity' | t : 'Entity' }}</label><input class="input" [(ngModel)]="entity" (keyup.enter)="load()"></div>
        <div class="field"><label>{{ 'action' | t : 'Action' }}</label><input class="input" [(ngModel)]="action" (keyup.enter)="load()"></div>
        <div class="field"><label>{{ 'representative' | t : 'Actor' }}</label><input class="input" [(ngModel)]="actor" (keyup.enter)="load()"></div>
        <div class="field"><button class="btn btn-p" (click)="load()" style="height:36px">{{ 'apply' | t : 'Apply' }}</button></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @if (!loading() && result(); as r) {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'date' | t }}</th><th>{{ 'representative' | t : 'Actor' }}</th><th>{{ 'entity' | t : 'Entity' }}</th><th>{{ 'action' | t : 'Action' }}</th><th>{{ 'details' | t : 'Change' }}</th></tr></thead>
          <tbody>
            @for (a of r.items; track a.id) {
              <tr>
                <td class="mono small">{{ a.occurredAt | date:'short' }}</td>
                <td>{{ a.actor }}</td>
                <td>{{ a.entity }}<div class="small muted mono">{{ a.entityId | slice:0:8 }}</div></td>
                <td><span class="badge b-neu">{{ a.action }}</span></td>
                <td class="small">
                  @if (a.before) { <span class="muted">was:</span> <span class="mono">{{ a.before | slice:0:60 }}</span><br> }
                  @if (a.after) { <span class="muted">now:</span> <span class="mono">{{ a.after | slice:0:60 }}</span> }
                  @if (!a.before && !a.after) { — }
                </td>
              </tr>
            } @empty { <tr><td colspan="5" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
        <div class="foot" style="padding:10px 14px;font-size:12px;color:var(--slate-500);border-top:1px solid var(--slate-150)">{{ r.total }} {{ 'total' | t }}</div>
      }
    </div>
  `,
})
export class AuditComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<AuditRow> | null>(null);
  entity = ''; action = ''; actor = '';

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 200 };
    if (this.entity.trim()) params['entity'] = this.entity.trim();
    if (this.action.trim()) params['action'] = this.action.trim();
    if (this.actor.trim()) params['actor'] = this.actor.trim();
    this.api.get<PagedResult<AuditRow>>('/audit', params).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
