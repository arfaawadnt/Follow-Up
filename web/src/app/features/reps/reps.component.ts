import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const TYPES = ['Collector', 'Marketing', 'Transfer', 'Scanning'];

@Component({
  selector: 'app-reps',
  standalone: true,
  imports: [DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'reps_2' | t : 'Representative Profiles' }}</div><h1>{{ 'representative_profiles' | t : 'Representative Profiles' }}</h1></div>
      @if (canEdit()) { <button class="btn btn-p" (click)="openNew()" style="height:38px">+ {{ 'new_representative' | t : 'New representative' }}</button> }
    </div>

    <div class="card" style="padding:12px;margin-bottom:14px">
      <div style="display:flex;gap:6px;flex-wrap:wrap">
        @for (t of typeFilters; track t) { <span class="pill" [class.on]="type() === t" (click)="setType(t)">{{ t }}</span> }
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">Loading…</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>Representative</th><th>Type</th><th>Phone</th><th>Goal</th><th>Target</th><th>Duration</th><th>Salary</th><th>{{ 'assigned_labs' | t : 'Assigned Labs' }}</th><th></th></tr></thead>
          <tbody>
            @for (r of filtered(); track r.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ r.fullName }}</b><div class="small muted">{{ sub(r) }}</div></td>
                <td><span class="badge" [class]="r.type === 'Collector' ? 'b-info' : 'b-pur'">{{ r.type }}</span></td>
                <td class="mono">{{ r.phone ?? '—' }}</td>
                <td>{{ r.goalType ?? '—' }}</td>
                <td class="mono">{{ r.target | number:'1.0-0' }}<span class="small muted"> {{ r.metric }}</span></td>
                <td>{{ r.goalDuration }}</td>
                <td class="mono">EGP {{ r.salary | number:'1.0-0' }}</td>
                <td class="mono" style="font-weight:700">{{ r.assignedCount }}</td>
                <td class="actions">@if (canEdit()) { <button class="btn-ghost" (click)="openEdit(r.id)">{{ 'edit' | t : 'Edit' }}</button> }</td>
              </tr>
            } @empty { <tr><td colspan="9" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.pill{cursor:pointer}`],
})
export class RepsComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly items = signal<RepListItem[]>([]);
  readonly typeFilters = ['All', ...TYPES];
  readonly type = signal('All');

  readonly filtered = computed(() => this.items());

  constructor() { this.load(); }

  canEdit(): boolean { return this.auth.has('AddReps') || this.auth.has('ManageReps') || this.auth.has('UpdateReps'); }
  sub(r: RepListItem): string { return [r.governorate, r.area, r.employmentType].filter(Boolean).join(' · '); }
  setType(t: string): void { this.type.set(t); this.load(); }

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
