import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { MarketingVisit, PagedResult } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-marketing',
  standalone: true,
  imports: [DatePipe, StatusBadgePipe, TranslatePipe],
  template: `
    <h1 class="display page-title">{{ 'marketing.title' | t }}</h1>
    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">{{ 'common.loading' | t }}</div> }
      @if (result(); as r) {
        <table class="app">
          <thead><tr><th>Lab</th><th>Purpose</th><th>Date</th><th>{{ 'labs.status' | t }}</th><th>Outcome</th></tr></thead>
          <tbody>
            @for (v of r.items; track v.id) {
              <tr><td class="client-code">{{ v.labDisplayCode }}</td><td>{{ v.purpose }}</td>
                <td>{{ v.scheduledDate | date:'mediumDate' }}</td>
                <td><span class="badge" [class]="v.status | statusBadge">{{ v.status }}</span></td>
                <td>{{ v.outcome ?? '—' }}</td></tr>
            } @empty { <tr><td colspan="5" class="empty">{{ 'common.empty' | t }}</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`.page-title{font-size:22px;margin:0 0 16px}.empty{color:var(--slate-500);text-align:center;padding:24px}`],
})
export class MarketingComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<MarketingVisit> | null>(null);
  constructor() {
    this.api.get<PagedResult<MarketingVisit>>('/marketing', { pageSize: 100 }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
