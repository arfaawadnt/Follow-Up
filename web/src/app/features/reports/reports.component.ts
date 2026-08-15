import { Component, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { NetworkOverview, RepPerformanceRow } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [TranslatePipe, DecimalPipe],
  template: `
    <h1 class="display page-title">{{ 'reports.title' | t }}</h1>

    @if (overview(); as o) {
      <div class="chead-plain">{{ 'reports.overview' | t }}</div>
      <div class="kpis">
        <div class="kpi kpi-blue"><div class="lbl">Total labs</div><div class="val">{{ o.totalLabs }}</div></div>
        <div class="kpi kpi-green"><div class="lbl">Active labs</div><div class="val">{{ o.activeLabs }}</div></div>
        <div class="kpi kpi-teal"><div class="lbl">Samples (month)</div><div class="val">{{ o.samplesThisMonth }}</div></div>
        <div class="kpi kpi-amber"><div class="lbl">Income (month)</div><div class="val">{{ o.incomeThisMonth | number:'1.0-0' }}</div></div>
      </div>
    }

    <div class="dcard">
      <div class="chead">{{ 'reports.performance' | t }}</div>
      <div class="cbody" style="padding:0">
        <table class="app">
          <thead><tr><th>Rep</th><th>Attainment %</th><th>On track</th><th>Salary</th></tr></thead>
          <tbody>
            @for (row of performance(); track row.repId) {
              <tr><td>{{ row.repName }}</td><td class="mono">{{ row.achievementPercent }}</td>
                <td><span class="badge" [class]="row.onTrack ? 'b-ok' : 'b-warn'">{{ row.onTrack ? 'On track' : 'Behind' }}</span></td>
                <td class="mono">{{ row.salary | number:'1.2-2' }}</td></tr>
            } @empty { <tr><td colspan="4" class="empty">{{ 'common.empty' | t }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>
  `,
  styles: [`
    .page-title{font-size:22px;margin:0 0 16px}
    .chead-plain{font:700 11px var(--disp);text-transform:uppercase;letter-spacing:.06em;color:var(--slate-500);margin-bottom:8px}
    .kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:16px;margin-bottom:20px}
    .empty{color:var(--slate-500);text-align:center;padding:24px}
  `],
})
export class ReportsComponent {
  private readonly api = inject(ApiService);
  readonly overview = signal<NetworkOverview | null>(null);
  readonly performance = signal<RepPerformanceRow[]>([]);
  constructor() {
    this.api.get<NetworkOverview>('/reports/overview').subscribe({ next: (o) => this.overview.set(o), error: () => {} });
    this.api.get<RepPerformanceRow[]>('/reports/performance').subscribe({ next: (p) => this.performance.set(p), error: () => {} });
  }
}
