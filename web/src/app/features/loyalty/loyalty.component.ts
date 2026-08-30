import { Component, ElementRef, HostListener, computed, inject, signal, ViewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { ToastService } from '../../core/toast.service';
import { TranslatePipe } from '../../core/i18n';

interface LoyaltyRow {
  laboratoryId: string; code: string; name: string; branch: string | null; city: string | null;
  monthlyTarget: number; mtdSamples: number; loyaltyPoints: number; loyaltyTier: string | null;
}
const TIERS = ['All', 'Gold', 'Silver', 'Bronze'];

@Component({
  selector: 'app-loyalty',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'loyalty' | t }}</div><h1>{{ 'loyalty' | t }}</h1></div></div>

    <div class="card" style="padding:16px;margin-bottom:20px">
      <div style="display:flex;gap:12px;flex-wrap:wrap;align-items:center">
        <input class="input" style="flex:1;min-width:220px" [placeholder]="'search_by_name_or_code' | t : 'Search by name or code'" [(ngModel)]="q">
        <select class="select" [(ngModel)]="tier"><option value="All">{{ 'all_tiers' | t : 'All tiers' }}</option>@for (tv of tiers.slice(1); track tv) { <option [value]="tv">{{ tv }}</option> }</select>
        @if (auth.has('ManageLoyalty')) { <button class="btn btn-s" [disabled]="busy()" (click)="recalc()"><i data-lucide="refresh-cw" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ 'recalculate_points' | t : 'Recalculate points' }}</button> }
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'code_2' | t : 'Code' }}</th><th>{{ 'laboratory_3' | t : 'Laboratory' }}</th><th>{{ 'monthly_target_2' | t : 'Monthly target' }}</th>
            <th>{{ 'achieved_mtd' | t : 'Achieved MTD' }}</th><th>{{ 'achievement' | t : 'Achievement' }}</th><th>{{ 'tier' | t : 'Tier' }}</th><th>{{ 'loyalty_points_2' | t : 'Points' }}</th>
            @if (auth.has('ManageLoyalty')) { <th style="text-align:center">{{ 'actions_3' | t : 'Actions' }}</th> }</tr></thead>
          <tbody>
            @for (l of filtered(); track l.laboratoryId) {
              <tr>
                <td class="mono">{{ l.code }}</td>
                <td><div style="font-weight:600;color:var(--slate-800)">{{ l.name }}</div><div class="small muted">{{ l.branch ?? '—' }} · {{ l.city ?? '—' }}</div></td>
                <td class="mono" style="font-weight:700">{{ l.monthlyTarget | number:'1.0-0' }}</td>
                <td class="mono" style="font-weight:700">{{ l.mtdSamples | number:'1.0-0' }}</td>
                <td><div style="display:flex;align-items:center;gap:8px"><div class="bar" style="width:60px"><div [style.width.%]="min100(ach(l))" [style.background]="col(ach(l))"></div></div><span class="mono small">{{ ach(l) }}%</span></div></td>
                <td><span class="badge" [class]="tierClass(l.loyaltyTier)">{{ l.loyaltyTier ?? '—' }}</span></td>
                <td class="mono" style="font-weight:700">{{ l.loyaltyPoints | number:'1.0-0' }}</td>
                @if (auth.has('ManageLoyalty')) { <td style="text-align:center"><button class="btn btn-mini btn-s" (click)="openTarget(l)" [disabled]="busy()">{{ 'set_target' | t : 'Set target' }}</button></td> }
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>

    <!-- CPN-16: a typed, translated, accessible target form replaces window.prompt -->
    @if (editing(); as e) {
      <div class="overlay" (click)="closeTarget()">
        <div class="dlg" role="dialog" aria-modal="true" aria-labelledby="setTargetTitle"
             (click)="$event.stopPropagation()" style="width:min(94vw,420px)">
          <div class="dlg-head">
            <h2 id="setTargetTitle">{{ 'set_target' | t : 'Set target' }} — {{ e.name }}</h2>
            <button type="button" class="btn btn-mini btn-s" [attr.aria-label]="('cancel' | t : 'Cancel')" (click)="closeTarget()">✕</button>
          </div>
          <form (ngSubmit)="saveTarget()" style="padding:16px">
            <div class="field">
              <label for="monthlyTargetInput">{{ 'monthly_target_2' | t : 'Monthly target' }}</label>
              <input #targetInput id="monthlyTargetInput" type="number" min="0" class="input"
                     [(ngModel)]="targetValue" name="monthlyTarget" required>
            </div>
            <div style="display:flex;gap:8px;margin-top:14px;justify-content:flex-end">
              <button type="button" class="btn btn-s" (click)="closeTarget()">{{ 'cancel' | t : 'Cancel' }}</button>
              <button type="submit" class="btn btn-p" [disabled]="busy() || targetValue < 0">{{ 'save' | t : 'Save' }}</button>
            </div>
          </form>
        </div>
      </div>
    }
  `,
})
export class LoyaltyComponent {
  private readonly api = inject(ApiService);
  private readonly icons = inject(IconsService);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<LoyaltyRow[]>([]);
  readonly tiers = TIERS;
  q = ''; tier = 'All';

  // CPN-16: inline target-edit dialog state.
  readonly editing = signal<LoyaltyRow | null>(null);
  targetValue = 0;
  @ViewChild('targetInput') set targetInput(el: ElementRef<HTMLInputElement> | undefined) { el?.nativeElement.focus(); }
  @HostListener('document:keydown.escape') onEscape(): void { if (this.editing()) this.closeTarget(); }

  readonly filtered = computed(() => {
    const q = this.q.trim().toLowerCase();
    return this.items().filter((l) =>
      (this.tier === 'All' || l.loyaltyTier === this.tier) &&
      (!q || l.name.toLowerCase().includes(q) || l.code.toLowerCase().includes(q)));
  });
  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<LoyaltyRow[]>('/loyalty').subscribe({
      next: (r) => { this.items.set(r); this.loading.set(false); this.icons.render(); }, error: () => this.loading.set(false),
    });
  }
  ach(l: LoyaltyRow): number { return l.monthlyTarget > 0 ? Math.round((l.mtdSamples / l.monthlyTarget) * 100) : 0; }
  min100(v: number): number { return Math.min(100, v); }
  col(p: number): string { return p >= 70 ? 'var(--teal-500)' : p >= 40 ? '#D9A62E' : '#C4574A'; }
  tierClass(t: string | null): string { return t === 'Gold' ? 'b-ok' : t === 'Silver' ? 'b-info' : t === 'Bronze' ? 'b-pur' : 'b-neu'; }

  recalc(): void {
    this.busy.set(true);
    this.api.post('/loyalty/recalculate', {}).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false) });
  }
  openTarget(l: LoyaltyRow): void { this.targetValue = l.monthlyTarget; this.editing.set(l); }
  closeTarget(): void { this.editing.set(null); }
  saveTarget(): void {
    const l = this.editing();
    const target = Math.trunc(Number(this.targetValue));
    if (!l || isNaN(target) || target < 0) return;
    this.busy.set(true);
    // Errors surface via the global HTTP error interceptor (ProblemDetails toast); this adds success feedback.
    this.api.post('/loyalty/target', { laboratoryId: l.laboratoryId, monthlyTarget: target }).subscribe({
      next: () => { this.busy.set(false); this.closeTarget(); this.toast.success('Monthly target updated.'); this.load(); },
      error: () => this.busy.set(false),
    });
  }
}
