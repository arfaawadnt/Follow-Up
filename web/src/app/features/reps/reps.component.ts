import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const TYPES = ['Collector', 'Marketing', 'Transfer', 'Scanning'];

@Component({
  selector: 'app-reps',
  standalone: true,
  imports: [DecimalPipe, TranslatePipe, FormsModule],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center;gap:12px">
      <div><div class="breadcrumbs">Home / {{ 'reps_2' | t : 'Representative Profiles' }}</div><h1>{{ 'representative_profiles' | t : 'Representative Profiles' }}</h1></div>
      <div style="display:flex;gap:8px">
        @if (auth.has('OracleIntegration')) { <button class="btn btn-s" style="height:38px" [disabled]="syncing()" (click)="sync()">{{ syncing() ? 'Syncing…' : 'Sync from Oracle' }}</button> }
        @if (canEdit()) { <button class="btn btn-p" (click)="openNew()" style="height:38px">+ {{ 'new_representative' | t : 'New representative' }}</button> }
      </div>
    </div>
    @if (notice()) { <div class="inline-banner" style="background:var(--ok-bg,#dff6dd);color:var(--ok-ink,#107c41);padding:10px 14px;border-radius:8px;margin-bottom:12px">{{ notice() }}</div> }

    <div class="card" style="padding:12px;margin-bottom:14px;display:flex;gap:10px;align-items:center;flex-wrap:wrap">
      <div style="display:flex;gap:6px;flex-wrap:wrap;flex:1">
        @for (t of typeFilters; track t) { <span class="pill" [class.on]="type() === t" (click)="setType(t)">{{ t }}</span> }
      </div>
      <input class="input" style="max-width:280px" [ngModel]="query()" (ngModelChange)="query.set($event); page.set(1)" placeholder="Search by name, phone, or location…">
      <span class="small muted">{{ filtered().length }} / {{ items().length }}</span>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">Loading…</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>Representative</th><th>Type</th><th>Phone</th><th>Goal</th><th>Target</th><th>Duration</th><th>Salary</th><th>{{ 'assigned_labs' | t : 'Assigned Labs' }}</th><th style="width:80px">Source</th><th></th></tr></thead>
          <tbody>
            @for (r of paged(); track r.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ r.fullName }}</b><div class="small muted">{{ sub(r) }}</div></td>
                <td><span class="badge" [class]="r.type === 'Collector' ? 'b-info' : 'b-pur'">{{ r.type }}</span></td>
                <td class="mono">{{ r.phone ?? '—' }}</td>
                <td>{{ r.goalType ?? '—' }}</td>
                <td class="mono">{{ r.target | number:'1.0-0' }}<span class="small muted"> {{ r.metric }}</span></td>
                <td>{{ r.goalDuration }}</td>
                <td class="mono">EGP {{ r.salary | number:'1.0-0' }}</td>
                <td class="mono" style="font-weight:700">{{ r.assignedCount }}</td>
                <td>@if (r.source === 'Oracle') { <span class="src-b src-o">Oracle</span> } @else { <span class="src-b src-m">Manual</span> }</td>
                <td class="actions">@if (canEdit()) { <button class="btn-ghost" (click)="openEdit(r.id)">{{ 'edit' | t : 'Edit' }}</button> }</td>
              </tr>
            } @empty { <tr><td colspan="10" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
        <div class="fu-pager">
          <button class="btn-ghost" [disabled]="page() <= 1" (click)="page.set(page() - 1)">‹ Prev</button>
          <span>Page {{ page() }} / {{ pageCount() }} · {{ filtered().length }} items</span>
          <button class="btn-ghost" [disabled]="page() >= pageCount()" (click)="page.set(page() + 1)">Next ›</button>
          <select class="select" [ngModel]="pageSize()" (ngModelChange)="pageSize.set(+$event); page.set(1)" style="max-width:90px;margin-inline-start:auto">
            <option [ngValue]="25">25</option><option [ngValue]="50">50</option><option [ngValue]="100">100</option>
          </select>
        </div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.pill{cursor:pointer}
    .src-b{font-size:11px;padding:2px 8px;border-radius:10px;font-weight:600}
    .src-o{background:#eaf2fa;color:#2f7bd2}.src-m{background:#eceff2;color:#6b7480}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150);font-size:12.5px;color:var(--slate-600)}`],
})
export class RepsComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly items = signal<RepListItem[]>([]);
  readonly typeFilters = ['All', ...TYPES];
  readonly type = signal('All');
  readonly query = signal('');
  readonly syncing = signal(false);
  readonly notice = signal<string | null>(null);

  readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.items();
    return this.items().filter((r) =>
      [r.fullName, r.phone, r.governorate, r.city, r.area].some((f) => (f ?? '').toLowerCase().includes(q)));
  });
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize())));
  readonly paged = computed(() => {
    const p = Math.min(this.page(), this.pageCount());
    const start = (p - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });

  constructor() { this.load(); }

  canEdit(): boolean { return this.auth.has('AddReps') || this.auth.has('ManageReps') || this.auth.has('UpdateReps'); }
  sync(): void {
    this.syncing.set(true); this.notice.set(null);
    this.api.post('/integration/sync-now').subscribe({
      next: () => { this.syncing.set(false); this.notice.set('Synced from Oracle.'); this.load(); },
      error: () => { this.syncing.set(false); this.notice.set('Oracle sync failed.'); },
    });
  }
  sub(r: RepListItem): string { return [r.governorate, r.area, r.employmentType].filter(Boolean).join(' · '); }
  setType(t: string): void { this.type.set(t); this.page.set(1); this.load(); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 500 };
    if (this.type() !== 'All') params['type'] = this.type();
    this.api.get<PagedResult<RepListItem>>('/reps', params).subscribe({
      next: (r) => { this.items.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  openNew(): void { void this.router.navigate(['/reps/new']); }
  openEdit(id: string): void { void this.router.navigate(['/reps', id]); }
}
