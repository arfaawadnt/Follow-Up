import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Commission, LabListItem, LabStat, LoyaltyLedger, PagedResult, RepListItem } from '../../core/models';

type Tab = 'loyalty' | 'commissions' | 'labstats';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [FormsModule, DatePipe, DecimalPipe],
  template: `
    <h1 class="display page-title">Analytics</h1>

    <div class="tabs">
      @if (auth.has('ManageLoyalty')) { <button class="tab" [class.on]="tab() === 'loyalty'" (click)="setTab('loyalty')">Loyalty</button> }
      @if (auth.has('ManageCommissions')) { <button class="tab" [class.on]="tab() === 'commissions'" (click)="setTab('commissions')">Commissions</button> }
      @if (auth.has('ViewLabStats')) { <button class="tab" [class.on]="tab() === 'labstats'" (click)="setTab('labstats')">Lab statistics</button> }
    </div>

    <div class="filters">
      @if (tab() === 'labstats') {
        <label>From <input type="date" [(ngModel)]="from"></label>
        <label>To <input type="date" [(ngModel)]="to"></label>
      } @else {
        <label>Period <input type="month" [(ngModel)]="month"></label>
      }
      <button class="btn btn-p btn-mini" (click)="load()" [disabled]="loading()">Refresh</button>
    </div>

    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">Loading…</div> }

      @if (!loading() && tab() === 'loyalty') {
        <table class="app">
          <thead><tr><th>Laboratory</th><th class="r">Target</th><th class="r">Achieved</th><th class="r">Points</th><th>Tier</th></tr></thead>
          <tbody>
            @for (l of loyalty(); track l.laboratoryId) {
              <tr><td>{{ labName(l.laboratoryId) }}</td><td class="r mono">{{ l.monthlyTarget }}</td>
                <td class="r mono">{{ l.mtdSamples }}</td><td class="r mono">{{ l.loyaltyPoints }}</td><td>{{ l.loyaltyTier ?? '—' }}</td></tr>
            } @empty { <tr><td colspan="5" class="empty">No loyalty data for this period.</td></tr> }
          </tbody>
        </table>
      }

      @if (!loading() && tab() === 'commissions') {
        <table class="app">
          <thead><tr><th>Representative</th><th class="r">Target</th><th class="r">Achieved</th><th class="r">Base</th>
            <th class="r">Commission</th><th class="r">Bonus</th><th class="r">Total</th></tr></thead>
          <tbody>
            @for (c of commissions(); track c.repId) {
              <tr><td>{{ repName(c.repId) }}</td>
                <td class="r mono">{{ c.targetAmount | number:'1.0-0' }}</td><td class="r mono">{{ c.achievedAmount | number:'1.0-0' }}</td>
                <td class="r mono">{{ c.baseSalary | number:'1.0-0' }}</td><td class="r mono">{{ c.commissionEarned | number:'1.0-0' }}</td>
                <td class="r mono">{{ c.bonusEarned | number:'1.0-0' }}</td><td class="r mono strong">{{ c.totalPayout | number:'1.0-0' }}</td></tr>
            } @empty { <tr><td colspan="7" class="empty">No commission data for this period.</td></tr> }
          </tbody>
        </table>
      }

      @if (!loading() && tab() === 'labstats') {
        <table class="app">
          <thead><tr><th>Date</th><th>Lab</th><th class="r">Registrations</th><th class="r">Tests</th><th class="r">Income</th></tr></thead>
          <tbody>
            @for (s of labstats(); track $index) {
              <tr><td>{{ s.date | date:'mediumDate' }}</td><td class="client-code mono">{{ s.labCode }}</td>
                <td class="r mono">{{ s.registrations }}</td><td class="r mono">{{ s.testCount }}</td>
                <td class="r mono">{{ s.income | number:'1.0-2' }}</td></tr>
            } @empty { <tr><td colspan="5" class="empty">No statistics in this range.</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`
    .page-title { font-size:22px; margin:0 0 16px; }
    .tabs { display:flex; gap:6px; margin-bottom:14px; }
    .tab { background:var(--white); border:1px solid var(--slate-300); color:var(--slate-700); border-radius:var(--r-btn);
      padding:7px 16px; font:600 12.5px var(--ui); cursor:pointer; }
    .tab.on { background:var(--primary-blue); color:#fff; border-color:var(--primary-blue); }
    .filters { display:flex; gap:14px; align-items:center; margin-bottom:14px; flex-wrap:wrap; }
    .filters label { font:600 12px var(--ui); color:var(--slate-600); display:flex; gap:6px; align-items:center; }
    .filters input { border:1px solid var(--slate-300); border-radius:var(--r-input); padding:6px 9px; background:var(--white); color:var(--slate-900); }
    .btn-mini { padding:5px 12px; font-size:12px; border-radius:var(--r-btn); }
    .empty { color:var(--slate-500); text-align:center; padding:24px; }
    th.r, td.r { text-align:right; } td.strong { font-weight:700; }
  `],
})
export class AnalyticsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(false);
  readonly tab = signal<Tab>(this.firstAllowedTab());
  readonly loyalty = signal<LoyaltyLedger[]>([]);
  readonly commissions = signal<Commission[]>([]);
  readonly labstats = signal<LabStat[]>([]);

  private readonly labs = signal<Map<string, string>>(new Map());
  private readonly reps = signal<Map<string, string>>(new Map());
  readonly labName = (id: string) => this.labs().get(id) ?? id.slice(0, 8);
  readonly repName = (id: string) => this.reps().get(id) ?? id.slice(0, 8);

  month = new Date().toISOString().slice(0, 7);   // YYYY-MM
  from = new Date(new Date().getFullYear(), new Date().getMonth(), 1).toISOString().slice(0, 10);
  to = new Date().toISOString().slice(0, 10);

  readonly period = computed(() => {
    const [y, m] = this.month.split('-').map(Number);
    return y * 100 + m;
  });

  constructor() {
    // Name lookups are shared across tabs; fetch once.
    this.api.get<PagedResult<LabListItem>>('/labs', { pageSize: 500 }).subscribe({
      next: (r) => this.labs.set(new Map(r.items.map((l) => [l.id, `${l.displayCode} · ${l.name}`]))),
    });
    if (this.auth.has('ManageCommissions')) {
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({
        next: (r) => this.reps.set(new Map(r.items.map((x) => [x.id, x.fullName]))),
      });
    }
    if (this.tab()) this.load();
  }

  private firstAllowedTab(): Tab {
    if (this.auth.has('ManageLoyalty')) return 'loyalty';
    if (this.auth.has('ManageCommissions')) return 'commissions';
    return 'labstats';
  }

  setTab(t: Tab): void { this.tab.set(t); this.load(); }

  load(): void {
    this.loading.set(true);
    const done = () => this.loading.set(false);
    if (this.tab() === 'loyalty') {
      this.api.get<LoyaltyLedger[]>('/loyalty', { period: this.period() }).subscribe({ next: (r) => { this.loyalty.set(r); done(); }, error: done });
    } else if (this.tab() === 'commissions') {
      this.api.get<Commission[]>('/commissions', { period: this.period() }).subscribe({ next: (r) => { this.commissions.set(r); done(); }, error: done });
    } else {
      this.api.get<LabStat[]>('/labstats', { from: this.from, to: this.to }).subscribe({ next: (r) => { this.labstats.set(r); done(); }, error: done });
    }
  }
}
