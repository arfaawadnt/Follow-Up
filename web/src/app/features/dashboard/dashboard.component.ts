import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { Dashboard } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [StatusBadgePipe],
  template: `
    <h1 class="display page-title">Dashboard</h1>

    @if (loading()) { <div class="dcard"><div class="cbody">Loading…</div></div> }
    @if (!loading() && data(); as d) {
      <div class="kpis">
        <div class="kpi kpi-blue"><div class="lbl">Active labs</div><div class="val">{{ d.activeLabs }}</div></div>
        <div class="kpi kpi-red"><div class="lbl">Open complaints</div><div class="val">{{ d.openComplaints }}</div></div>
        <div class="kpi kpi-teal"><div class="lbl">Samples today</div><div class="val">{{ d.samplesToday }}</div></div>
        <div class="kpi kpi-amber"><div class="lbl">Missed today</div><div class="val">{{ d.missedToday }}</div></div>
      </div>

      <div class="grid">
        <div class="dcard">
          <div class="chead">Today's schedule</div>
          <div class="cbody">
            @if (d.todaySchedule.length === 0) { <p class="empty">No visits scheduled for today.</p> }
            @else {
              <table class="app">
                <thead><tr><th>Lab</th><th>Time</th><th>Status</th></tr></thead>
                <tbody>
                  @for (s of d.todaySchedule; track s.visitId) {
                    <tr><td><span class="client-code">{{ s.labDisplayCode }}</span> {{ s.labName }}</td>
                    <td class="mono">{{ s.time }}</td>
                    <td><span class="badge" [class]="s.status | statusBadge">{{ s.status }}</span></td></tr>
                  }
                </tbody>
              </table>
            }
          </div>
        </div>

        <div class="dcard">
          <div class="chead">Unresolved complaints</div>
          <div class="cbody">
            @if (d.unresolvedComplaints.length === 0) { <p class="empty">No unresolved complaints.</p> }
            @else {
              <table class="app">
                <thead><tr><th>Ref</th><th>Lab</th><th>Status</th></tr></thead>
                <tbody>
                  @for (c of d.unresolvedComplaints; track c.id) {
                    <tr><td class="mono">{{ c.reference }}</td><td class="client-code">{{ c.labDisplayCode }}</td>
                    <td><span class="badge" [class]="c.status | statusBadge">{{ c.status }}</span></td></tr>
                  }
                </tbody>
              </table>
            }
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .page-title { font-size: 22px; margin: 0 0 16px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 16px; margin-bottom: 20px; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
    @media (max-width: 940px) { .grid { grid-template-columns: 1fr; } }
    .empty { color: var(--slate-500); font-size: 13px; }
  `],
})
export class DashboardComponent {
  private readonly http = inject(HttpClient);
  readonly loading = signal(true);
  readonly data = signal<Dashboard | null>(null);

  constructor() {
    this.http.get<Dashboard>(`${environment.apiBase}/dashboard`).subscribe({
      next: (d) => { this.data.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
