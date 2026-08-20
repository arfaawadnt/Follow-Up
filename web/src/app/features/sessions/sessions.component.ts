import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, SlicePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface Sess { id: string; username: string; ipAddress: string | null; terminal: string | null; loginAt: string; logoutAt: string | null; lastSeenAt: string; durationSec: number; status: string; }
const STATUSES = ['All', 'Active', 'Revoked', 'Expired'];

@Component({
  selector: 'app-sessions',
  standalone: true,
  imports: [FormsModule, DatePipe, SlicePipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'active_sessions' | t : 'Sessions' }}</div><h1>{{ 'active_sessions' | t : 'Sessions' }}</h1></div></div>

    <div class="kpis" style="grid-template-columns:repeat(3,1fr);margin-bottom:16px">
      <div class="kpi kpi-green"><div class="lbl">Active</div><div class="val">{{ count('Active') }}</div></div>
      <div class="kpi kpi-neu" style="background:var(--neu-ink)"><div class="lbl" style="color:rgba(255,255,255,.85)">Revoked</div><div class="val" style="color:#fff">{{ count('Revoked') }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">Expired</div><div class="val">{{ count('Expired') }}</div></div>
    </div>

    <div class="card" style="padding:12px;margin-bottom:16px;display:flex;gap:8px;align-items:center">
      @for (s of statuses; track s) { <span class="pill" [class.on]="status() === s" (click)="status.set(s)">{{ s === 'All' ? ('all' | t) : s }}</span> }
      <input class="input" style="margin-inline-start:auto;max-width:240px" [(ngModel)]="q" [placeholder]="'search' | t : 'Search user'">
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'username' | t : 'User' }}</th><th>IP</th><th>{{ 'terminal' | t : 'Terminal' }}</th><th>{{ 'login' | t : 'Login' }}</th><th>{{ 'logout' | t : 'Logout' }}</th><th>{{ 'duration' | t : 'Duration' }}</th><th>{{ 'status' | t }}</th></tr></thead>
          <tbody>
            @for (s of filtered(); track s.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ s.username }}</b></td>
                <td class="mono small">{{ s.ipAddress ?? '—' }}</td>
                <td class="small muted">{{ (s.terminal ?? '—') | slice:0:40 }}</td>
                <td class="mono small">{{ s.loginAt | date:'short' }}</td>
                <td class="mono small">{{ s.logoutAt ? (s.logoutAt | date:'short') : '—' }}</td>
                <td class="mono">{{ dur(s.durationSec) }}</td>
                <td><span class="badge" [class]="s.status==='Active'?'b-ok':s.status==='Revoked'?'b-neu':'b-warn'">{{ s.status }}</span></td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:24px">—</td></tr> }
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
  readonly statuses = STATUSES;
  q = '';

  readonly filtered = computed(() => {
    const q = this.q.trim().toLowerCase();
    return this.rows().filter((s) => (this.status() === 'All' || s.status === this.status()) && (!q || s.username.toLowerCase().includes(q)));
  });

  constructor() { this.load(); }
  count(st: string): number { return this.rows().filter((s) => s.status === st).length; }
  dur(sec: number): string { const h = Math.floor(sec / 3600); const m = Math.floor((sec % 3600) / 60); return h ? `${h}h ${m}m` : `${m}m`; }
  load(): void {
    this.loading.set(true);
    this.api.get<Sess[]>('/sessions/all').subscribe({ next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
}
