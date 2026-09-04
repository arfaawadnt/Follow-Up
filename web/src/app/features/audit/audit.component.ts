import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { PagedResult } from '../../core/models';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { TranslatePipe } from '../../core/i18n';

interface AuditRow { id: string; occurredAt: string; actor: string; entity: string; entityId: string; action: string; before: string | null; after: string | null; correlationId: string | null; }

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [FormsModule, DatePipe, SlicePipe, TranslatePipe, FilterSelectComponent],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'audit_trail' | t : 'Audit trail' }}</div><h1>{{ 'audit_trail' | t : 'Audit trail' }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'logs_today' | t : 'Logs Today' }}</div><div class="val">{{ logsToday() }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'active_users_today' | t : 'Active Users Today' }}</div><div class="val">{{ activeUsersToday() }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'modifications_today' | t : 'Modifications Today' }}</div><div class="val">{{ modificationsToday() }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(2,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'entity' | t : 'Entity' }}</label>
          <app-filter-select [options]="entities()" [ngModel]="entity" (ngModelChange)="entity = $event; load()" [allValue]="'All'" [placeholder]="'all' | t : 'All'"></app-filter-select>
        </div>
        <div class="field"><label>{{ 'user' | t : 'User' }}</label>
          <app-filter-select [options]="actors()" [ngModel]="actor" (ngModelChange)="actor = $event; load()" [allValue]="'All'" [placeholder]="'all' | t : 'All'"></app-filter-select>
        </div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @if (!loading() && result(); as r) {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'action_time' | t : 'Action Time' }}</th><th>{{ 'action_by' | t : 'Action By' }}</th><th>{{ 'entity' | t : 'Entity' }}</th><th>{{ 'action' | t : 'Action' }}</th><th>{{ 'valuable_info' | t : 'Valuable Info' }}</th></tr></thead>
          <tbody>
            @for (a of r.items; track a.id) {
              <tr>
                <td class="mono small">{{ a.occurredAt | date:'dd/MM/yyyy HH:mm' }}</td>
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
  readonly entities = signal<string[]>([]);
  readonly actors = signal<string[]>([]);
  entity = 'All'; actor = 'All';

  private readonly todayRows = computed(() => (this.result()?.items ?? []).filter((a) => this.isToday(a.occurredAt)));
  readonly logsToday = computed(() => this.todayRows().length);
  readonly activeUsersToday = computed(() => new Set(this.todayRows().map((a) => a.actor)).size);
  readonly modificationsToday = computed(() => this.todayRows().filter((a) => !/read|view|login/i.test(a.action)).length);

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 200 };
    if (this.entity !== 'All') params['entity'] = this.entity;
    if (this.actor !== 'All') params['actor'] = this.actor;
    this.api.get<PagedResult<AuditRow>>('/audit', params).subscribe({
      next: (r) => {
        this.result.set(r);
        this.entities.set(this.mergeOptions(this.entities(), r.items.map((i) => i.entity)));
        this.actors.set(this.mergeOptions(this.actors(), r.items.map((i) => i.actor)));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private mergeOptions(current: string[], incoming: string[]): string[] {
    return [...new Set([...current, ...incoming])].filter((v) => !!v).sort();
  }

  private isToday(iso: string): boolean {
    const d = new Date(iso); const n = new Date();
    return d.getFullYear() === n.getFullYear() && d.getMonth() === n.getMonth() && d.getDate() === n.getDate();
  }
}
