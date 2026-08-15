import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { environment } from '../../../environments/environment';
import { LabListItem, PagedResult } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';

@Component({
  selector: 'app-labs',
  standalone: true,
  imports: [FormsModule, StatusBadgePipe],
  template: `
    <div class="head">
      <h1 class="display page-title">Laboratories</h1>
      <input class="search" placeholder="Search by name…" [(ngModel)]="search" (keyup.enter)="load()">
    </div>

    <div class="dcard">
      <div class="cbody" style="padding:0">
        @if (loading()) { <div class="cbody">Loading…</div> }
        @if (result(); as r) {
          <table class="app">
            <thead><tr><th>Code</th><th>Name</th><th>Segment</th><th>Governorate</th><th>Status</th></tr></thead>
            <tbody>
              @for (lab of r.items; track lab.id) {
                <tr>
                  <td class="client-code mono">{{ lab.displayCode }}</td>
                  <td>{{ lab.name }}</td>
                  <td>{{ lab.segment }}</td>
                  <td>{{ lab.governorate ?? '—' }}</td>
                  <td><span class="badge" [class]="lab.status | statusBadge">{{ lab.status }}</span></td>
                </tr>
              } @empty {
                <tr><td colspan="5" class="empty">No laboratories match.</td></tr>
              }
            </tbody>
          </table>
          <div class="foot">{{ r.total }} total@if (r.truncated) { <span> · showing first {{ r.pageSize }}</span> }</div>
        }
      </div>
    </div>
  `,
  styles: [`
    .head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; gap: 16px; }
    .page-title { font-size: 22px; margin: 0; }
    .search { border: 1px solid var(--slate-300); border-radius: var(--r-input); padding: 8px 12px; font-size: 13px; min-width: 260px; background: var(--white); color: var(--slate-900); }
    .foot { padding: 10px 14px; font-size: 12px; color: var(--slate-500); border-top: 1px solid var(--slate-150); }
    .empty { color: var(--slate-500); text-align: center; padding: 24px; }
  `],
})
export class LabsComponent {
  private readonly http = inject(HttpClient);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<LabListItem> | null>(null);
  search = '';

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string> = { pageSize: '50' };
    if (this.search.trim()) params['search'] = this.search.trim();
    this.http.get<PagedResult<LabListItem>>(`${environment.apiBase}/labs`, { params }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
