import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { PagedResult, UserListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <h1 class="display page-title">{{ 'users.title' | t }}</h1>
    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">{{ 'common.loading' | t }}</div> }
      @if (result(); as r) {
        <table class="app">
          <thead><tr><th>{{ 'common.username' | t }}</th><th>Role</th><th>Email</th><th>{{ 'labs.status' | t }}</th></tr></thead>
          <tbody>
            @for (u of r.items; track u.id) {
              <tr><td>{{ u.username }}</td><td>{{ u.roleName }}</td><td>{{ u.email ?? '—' }}</td>
                <td>
                  @if (u.isLocked) { <span class="badge b-bad">Locked</span> }
                  @else { <span class="badge" [class]="u.isActive ? 'b-ok' : 'b-neu'">{{ u.isActive ? 'Active' : 'Inactive' }}</span> }
                </td></tr>
            } @empty { <tr><td colspan="4" class="empty">{{ 'common.empty' | t }}</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`.page-title{font-size:22px;margin:0 0 16px}.empty{color:var(--slate-500);text-align:center;padding:24px}`],
})
export class UsersComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<UserListItem> | null>(null);
  constructor() {
    this.api.get<PagedResult<UserListItem>>('/users', { pageSize: 100 }).subscribe({
      next: (r) => { this.result.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
