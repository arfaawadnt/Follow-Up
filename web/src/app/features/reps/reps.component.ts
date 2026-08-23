import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const TYPES = ['All', 'Collector', 'Marketing', 'Scanning'];

@Component({
  selector: 'app-reps',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'reps_2' | t : 'Representatives' }}</div><h1>{{ 'reps_2' | t : 'Representatives' }}</h1></div>
      @if (canAdd()) { <button class="btn btn-p" (click)="showForm.set(!showForm())" style="height:38px">{{ showForm() ? ('cancel' | t : 'Cancel') : ('new_representative' | t : 'New representative') }}</button> }
    </div>

    @if (showForm() && canAdd()) {
      <div class="card" style="padding:20px;margin-bottom:16px">
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>{{ 'name_lbl' | t : 'Full name' }}</label><input class="input" [(ngModel)]="form.fullName"></div>
          <div class="field"><label>{{ 'type' | t }}</label><select class="select" [(ngModel)]="form.type">@for (t of createTypes; track t) { <option [value]="t">{{ t }}</option> }</select></div>
          <div class="field"><label>{{ 'goal' | t : 'Goal duration' }}</label><select class="select" [(ngModel)]="form.goalDuration"><option value="Monthly">Monthly</option><option value="Quarterly">Quarterly</option></select></div>
          <div class="field"><label>{{ 'phone' | t : 'Phone' }}</label><input class="input" [(ngModel)]="form.phone"></div>
          <div class="field"><label>{{ 'salary' | t : 'Salary' }}</label><input type="number" min="0" class="input" [(ngModel)]="form.salary"></div>
          <div class="field"><label>{{ 'goal_target' | t : 'Target' }}</label><input type="number" min="0" class="input" [(ngModel)]="form.target"></div>
          <div class="field"><label>{{ 'governorate_lbl' | t : 'Governorate' }}</label><input class="input" [(ngModel)]="form.governorate"></div>
          <div class="field"><label>{{ 'branch_2' | t : 'Branch' }}</label><input class="input" [(ngModel)]="form.branch"></div>
        </div>
        <div style="margin-top:12px;display:flex;gap:8px;justify-content:flex-end">
          <button class="btn btn-s" (click)="showForm.set(false)" style="height:36px">{{ 'cancel' | t : 'Cancel' }}</button>
          <button class="btn btn-p" [disabled]="!form.fullName.trim() || busy()" (click)="submit()" style="height:36px">{{ 'save' | t : 'Save' }}</button>
        </div>
      </div>
    }

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-teal"><div class="lbl">{{ 'total' | t }}</div><div class="val">{{ items().length }}</div></div>
      <div class="kpi kpi-blue"><div class="lbl">Collector</div><div class="val">{{ count('Collector') }}</div></div>
      <div class="kpi kpi-pur" style="background:var(--pur-ink)"><div class="lbl" style="color:rgba(255,255,255,.85)">Marketing</div><div class="val" style="color:#fff">{{ count('Marketing') }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'active_labs_2' | t : 'Active' }}</div><div class="val">{{ activeCount() }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(3,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'search' | t : 'Search' }}</label><input class="input" [(ngModel)]="search" (keyup.enter)="load()" [placeholder]="'name_lbl' | t : 'Name'"></div>
        <div class="field"><label>{{ 'type' | t }}</label><select class="select" [(ngModel)]="type" (ngModelChange)="load()"><option value="All">{{ 'all' | t }}</option>@for (t of types.slice(1); track t) { <option [value]="t">{{ t }}</option> }</select></div>
        <div class="field"><label>{{ 'governorate_lbl' | t : 'Governorate' }}</label><select class="select" [(ngModel)]="gov"><option value="All">{{ 'all_2' | t }}</option>@for (g of govs(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'name_lbl' | t : 'Name' }}</th><th>{{ 'type' | t }}</th><th>{{ 'goal' | t : 'Goal' }}</th><th>{{ 'goal_target' | t : 'Target' }}</th>
            <th>{{ 'salary' | t : 'Salary' }}</th><th>{{ 'assigned_labs' | t : 'Assigned labs' }}</th><th>{{ 'governorate_lbl' | t : 'Governorate' }}</th><th>{{ 'status' | t }}</th></tr></thead>
          <tbody>
            @for (r of filtered(); track r.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ r.fullName }}</b>@if (r.phone) { <div class="small muted mono">{{ r.phone }}</div> }</td>
                <td><span class="badge" [class]="r.type === 'Collector' ? 'b-info' : 'b-pur'">{{ r.type }}</span></td>
                <td>{{ r.goalType ?? r.goalDuration }}<div class="small muted">{{ r.metric ?? '' }}</div></td>
                <td class="mono">{{ r.target | number:'1.0-0' }}</td>
                <td class="mono">EGP {{ r.salary | number:'1.0-0' }}</td>
                <td class="mono" style="font-weight:700">{{ r.assignedCount }}</td>
                <td>{{ r.governorate ?? '—' }}</td>
                <td><span class="badge" [class]="r.isActive ? 'b-ok' : 'b-neu'">{{ r.isActive ? ('active_labs_2' | t : 'Active') : 'Inactive' }}</span></td>
              </tr>
            } @empty { <tr><td colspan="8" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
})
export class RepsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly items = signal<RepListItem[]>([]);
  readonly types = TYPES;
  readonly busy = signal(false);
  readonly showForm = signal(false);
  readonly createTypes = ['Collector', 'Marketing', 'Transfer', 'Scanning'];
  form = { fullName: '', type: 'Collector', goalDuration: 'Monthly', salary: 0, target: 0, phone: '', governorate: '', branch: '' };
  search = ''; type = 'All'; gov = 'All';

  canAdd(): boolean { return this.auth.has('AddReps') || this.auth.has('ManageReps'); }

  readonly filtered = computed(() => this.items().filter((r) => this.gov === 'All' || r.governorate === this.gov));
  readonly govs = computed(() => [...new Set(this.items().map((r) => r.governorate).filter((x): x is string => !!x))].sort());

  constructor() { this.load(); }

  count(t: string): number { return this.filtered().filter((r) => r.type === t).length; }
  activeCount(): number { return this.filtered().filter((r) => r.isActive).length; }

  submit(): void {
    if (!this.form.fullName.trim()) return;
    this.busy.set(true);
    this.api.post('/reps', {
      fullName: this.form.fullName.trim(), type: this.form.type, goalDuration: this.form.goalDuration,
      salary: this.form.salary || 0, target: this.form.target || 0,
      phone: this.form.phone || null, governorate: this.form.governorate || null, branch: this.form.branch || null,
    }).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.resetForm(); this.load(); },
      error: () => this.busy.set(false),
    });
  }

  private resetForm(): void {
    this.form = { fullName: '', type: 'Collector', goalDuration: 'Monthly', salary: 0, target: 0, phone: '', governorate: '', branch: '' };
  }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 500 };
    if (this.search.trim()) params['search'] = this.search.trim();
    if (this.type !== 'All') params['type'] = this.type;
    this.api.get<PagedResult<RepListItem>>('/reps', params).subscribe({
      next: (r) => { this.items.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
