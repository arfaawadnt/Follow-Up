import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface Sess { id: string; username: string; ipAddress: string | null; terminal: string | null; loginAt: string; logoutAt: string | null; lastSeenAt: string; durationSec: number; status: string; }

@Component({
  selector: 'app-sessions',
  standalone: true,
  imports: [FormsModule, DatePipe, SlicePipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'active_sessions' | t : 'Sessions' }}</div><h1>{{ 'active_sessions' | t : 'Sessions' }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total_sessions' | t : 'Total Sessions' }}</div><div class="val">{{ rows().length }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'active_sessions_count' | t : 'Active Sessions' }}</div><div class="val">{{ activeCount() }}</div></div>
      <div class="kpi kpi-orange"><div class="lbl">{{ 'avg_duration' | t : 'Avg Duration (Min)' }}</div><div class="val">{{ avgDurationMin() }}</div></div>
    </div>

    <div class="card" style="padding:12px;margin-bottom:16px;display:flex;gap:12px;align-items:end">
      <div class="field" style="min-width:200px">
        <label>{{ 'status' | t : 'Status' }}</label>
        <select class="select" [ngModel]="status()" (ngModelChange)="status.set($event)">
          <option value="All">{{ 'all' | t : 'All' }}</option>
          <option value="Active">{{ 'active' | t : 'Active' }}</option>
          <option value="LoggedOut">{{ 'logged_out' | t : 'Logged Out' }}</option>
        </select>
      </div>
      <div class="field" style="min-width:200px">
        <label>{{ 'username' | t : 'Username' }}</label>
        <select class="select" [ngModel]="user()" (ngModelChange)="user.set($event)">
          <option value="All">{{ 'all' | t : 'All' }}</option>
          @for (u of usernames(); track u) { <option [value]="u">{{ u }}</option> }
        </select>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'username' | t : 'Username' }}</th><th>{{ 'terminal_ip' | t : 'Terminal IP' }}</th><th>{{ 'terminal_name' | t : 'Terminal Name' }}</th><th>{{ 'login_time' | t : 'Login Time' }}</th><th>{{ 'logout_time' | t : 'Logout Time' }}</th><th>{{ 'duration' | t : 'Duration' }}</th></tr></thead>
          <tbody>
            @for (s of filtered(); track s.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ s.username }}</b></td>
                <td class="mono small">{{ s.ipAddress ?? '—' }}</td>
                <td class="small muted">{{ (s.terminal ?? '—') | slice:0:40 }}</td>
                <td class="mono small">{{ s.loginAt | date:'short' }}</td>
                <td class="mono small">{{ s.logoutAt ? (s.logoutAt | date:'short') : '—' }}</td>
                <td class="mono">{{ dur(s.durationSec) }}</td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
})
export class SessionsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly rows = signal<Sess[]>([]);
  readonly status = signal('All');
  readonly user = signal('All');

  readonly usernames = computed(() => [...new Set(this.rows().map((s) => s.username))].sort());
  readonly activeCount = computed(() => this.rows().filter((s) => s.status === 'Active').length);
  readonly avgDurationMin = computed(() => {
    const rows = this.rows();
    return rows.length ? Math.round(rows.reduce((sum, s) => sum + s.durationSec, 0) / rows.length / 60) : 0;
  });

  readonly filtered = computed(() => {
    const st = this.status(); const u = this.user();
    return this.rows().filter((s) =>
      (st === 'All' || (st === 'Active' ? s.status === 'Active' : s.status !== 'Active')) &&
      (u === 'All' || s.username === u));
  });

  constructor() { this.load(); }
  dur(sec: number): string { const h = Math.floor(sec / 3600); const m = Math.floor((sec % 3600) / 60); return h ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m`; }
  load(): void {
    this.loading.set(true);
    this.api.get<Sess[]>('/sessions/all').subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
}
