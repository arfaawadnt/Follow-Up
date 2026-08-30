import { Component, inject, signal } from '@angular/core';
import { DecimalPipe, formatDate } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface Commission {
  repId: string; name: string; type: string; goalType: string; period: number;
  targetAmount: number; achievedAmount: number; baseSalary: number;
  commissionEarned: number; bonusEarned: number; totalPayout: number;
}
const GROUPS = ['Collector', 'Marketing', 'Scanning'];

@Component({
  selector: 'app-commissions',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'commissions' | t }}</div><h1>{{ 'commissions' | t }}</h1></div>
      <div class="pagehead-actions" style="display:flex;gap:8px;align-items:center">
        <label style="font-size:13px;font-weight:600;color:var(--slate-700)">{{ 'select_month' | t : 'Select Month:' }}</label>
        <select class="select" [(ngModel)]="period" (ngModelChange)="load()">
          @for (m of months; track m.value) { <option [ngValue]="m.value">{{ m.label }}</option> }
        </select>
        @if (auth.has('ManageCommissions')) { <button class="btn btn-p" [disabled]="busy() || !rows().length" (click)="save()">{{ 'lock_save_payouts' | t : 'Save payouts' }}</button> }
      </div>
    </div>

    @if (loading()) { <div class="card empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
    @else {
      @for (g of groups; track g) {
        @if (byType(g).length) {
          <div class="card" style="margin-bottom:20px;padding:0;overflow:hidden">
            <div style="background:var(--slate-100);padding:10px 16px;font-weight:700;border-bottom:1px solid var(--slate-150);font-size:13px">{{ g }} {{ 'reps_2' | t : 'reps' }}</div>
            <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
              <thead><tr><th>{{ 'representative_4' | t : 'Representative' }}</th><th>{{ 'target_2' | t : 'Target' }}</th><th>{{ 'achieved_mtd' | t : 'Achieved' }}</th>
                <th>{{ 'attainment_2' | t : 'Attainment' }}</th><th>{{ 'base_salary_2' | t : 'Base salary' }}</th><th>{{ 'commission' | t : 'Commission' }}</th><th>{{ 'bonus' | t : 'Bonus' }}</th><th>{{ 'total_payout_2' | t : 'Total payout' }}</th></tr></thead>
              <tbody>
                @for (r of byType(g); track r.repId) {
                  <tr>
                    <td><b style="color:var(--slate-900)">{{ r.name }}</b><div class="small muted">{{ r.goalType }}</div></td>
                    <td class="mono">{{ r.targetAmount | number:'1.0-0' }}</td>
                    <td class="mono">{{ r.achievedAmount | number:'1.0-0' }}</td>
                    <td class="mono">{{ pct(r) }}%</td>
                    <td class="mono">{{ r.baseSalary | number:'1.0-0' }}</td>
                    <td class="mono">{{ r.commissionEarned | number:'1.0-0' }}</td>
                    <td class="mono">{{ r.bonusEarned | number:'1.0-0' }}</td>
                    <td class="mono" style="font-weight:700">EGP {{ r.totalPayout | number:'1.0-0' }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          </div>
        }
      }
    }
  `,
})
export class CommissionsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly rows = signal<Commission[]>([]);
  readonly groups = GROUPS;
  readonly months = CommissionsComponent.buildMonths();
  period = this.months[0].value;

  private static buildMonths(): { value: number; label: string }[] {
    const now = new Date();
    return Array.from({ length: 12 }, (_, i) => {
      const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
      return { value: d.getFullYear() * 100 + d.getMonth() + 1, label: formatDate(d, 'MMMM yyyy', 'en-US') };
    });
  }

  constructor() { this.load(); }

  byType(t: string): Commission[] { return this.rows().filter((r) => r.type === t); }
  pct(r: Commission): number { return r.targetAmount > 0 ? Math.round((r.achievedAmount / r.targetAmount) * 100) : 0; }

  load(): void {
    this.loading.set(true);
    this.api.get<Commission[]>('/commissions', { period: this.period }).subscribe({
      next: (r) => { this.rows.set(r); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
  save(): void {
    if (!this.rows().length) return;
    this.busy.set(true);
    // One transactional call (CPN-8): the server recomputes and saves every in-scope rep's payout together,
    // so a period is never left half-saved the way the old per-row fan-out could.
    this.api.post('/commissions/save-all', { period: this.period })
      .subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
}
