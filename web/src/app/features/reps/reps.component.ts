import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-reps',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <h1 class="display page-title">{{ 'reps.title' | t }}</h1>
    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">{{ 'common.loading' | t }}</div> }
      @if (result(); as r) {
        <table class="app">
          <thead><tr><th>{{ 'labs.name' | t }}</th><th>Type</th><th>Duration</th><th>{{ 'labs.governorate' | t }}</th><th>{{ 'labs.status' | t }}</th></tr></thead>
          <tbody>
            @for (rep of r.items; track rep.id) {
              <tr><td>{{ rep.fullName }}</td><td>{{ rep.type }}</td><td>{{ rep.goalDuration }}</td>
                <td>{{ rep.governorate ?? '—' }}</td>
                <td><span class="badge" [class]="rep.isActive ? 'b-ok' : 'b-neu'">{{ rep.isActive ? 'Active' : 'Inactive' }}</span></td></tr>
            } @empty { <tr><td colspan="5" class="empty">{{ 'common.empty' | t }}</td></tr> }
          </tbody>
        </table>
        <div class="foot">{{ r.total }} {{ 'common.total' | t }}</div>
      }
    </div></div>
  `,
  styles: [`.page-title{font-size:22px;margin:0 0 16px}.foot{padding:10px 14px;font-size:12px;color:var(--slate-500);border-top:1px solid var(--slate-150)}.empty{color:var(--slate-500);text-align:center;padding:24px}`],
})
export class RepsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<RepListItem> | null>(null);
  constructor() {
    this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 100 }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
