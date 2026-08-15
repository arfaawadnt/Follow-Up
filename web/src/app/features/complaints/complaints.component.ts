import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { environment } from '../../../environments/environment';
import { ComplaintListItem, PagedResult } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';

@Component({
  selector: 'app-complaints',
  standalone: true,
  imports: [StatusBadgePipe, DatePipe],
  template: `
    <h1 class="display page-title">Complaints</h1>
    <div class="dcard">
      <div class="cbody" style="padding:0">
        @if (loading()) { <div class="cbody">Loading…</div> }
        @if (result(); as r) {
          <table class="app">
            <thead><tr><th>Ref</th><th>Lab</th><th>Category</th><th>Stage</th><th>Status</th><th>Logged</th></tr></thead>
            <tbody>
              @for (c of r.items; track c.id) {
                <tr>
                  <td class="mono">{{ c.reference }}</td>
                  <td class="client-code">{{ c.labDisplayCode }}</td>
                  <td>{{ c.category }}</td>
                  <td>{{ c.stage }}</td>
                  <td><span class="badge" [class]="c.status | statusBadge">{{ c.status }}</span></td>
                  <td>{{ c.createdAt | date:'short' }}</td>
                </tr>
              } @empty { <tr><td colspan="6" class="empty">No complaints.</td></tr> }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`.page-title { font-size: 22px; margin: 0 0 16px; } .empty { color: var(--slate-500); text-align: center; padding: 24px; }`],
})
export class ComplaintsComponent {
  private readonly http = inject(HttpClient);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<ComplaintListItem> | null>(null);

  constructor() {
    this.http.get<PagedResult<ComplaintListItem>>(`${environment.apiBase}/complaints`, { params: { pageSize: '50' } }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
