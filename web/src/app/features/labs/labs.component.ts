import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, PagedResult } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const SEGMENTS = ['All', 'A', 'B', 'C'];
const STATUSES = ['All', 'New', 'Scanned', 'Active', 'Inactive', 'Pending', 'Suspended', 'Stopped', 'Churned'];

@Component({
  selector: 'app-labs',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'lab_mgmt' | t : 'Laboratories' }}</div><h1>{{ 'lab_mgmt' | t : 'Laboratories' }}</h1></div>
      <div class="pagehead-actions">@if (auth.has('AddLabs')) { <a class="btn btn-p" routerLink="/labs/new">{{ 'new_lab_btn' | t : 'New laboratory' }}</a> }</div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(4,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'total_labs' | t : 'Total labs' }}</div><div class="val">{{ items().length }}</div><div class="sub">{{ 'matching_current_filters' | t : 'matching filters' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'active_labs_3' | t : 'Active labs' }}</div><div class="val">{{ countStatus('Active') }}</div><div class="sub">{{ 'generating_daily_visits' | t : 'generating visits' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'onboarding' | t : 'Onboarding' }}</div><div class="val">{{ onboarding() }}</div><div class="sub">{{ 'scanned_interactive' | t : 'scanned' }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">{{ 'inactive' | t : 'Inactive' }}</div><div class="val">{{ countStatus('Inactive') }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'search' | t : 'Search' }}</label><input class="input" [(ngModel)]="search" (keyup.enter)="load()" [placeholder]="'search_lab_name_or_code' | t : 'Name or code'"></div>
        <div class="field"><label>{{ 'segment' | t }}</label><select class="select" [(ngModel)]="segment" (ngModelChange)="load()"><option value="All">{{ 'all' | t }}</option>@for (s of segments.slice(1); track s) { <option [value]="s">{{ s }}</option> }</select></div>
        <div class="field"><label>{{ 'status' | t }}</label><select class="select" [(ngModel)]="status" (ngModelChange)="load()"><option value="All">{{ 'all' | t }}</option>@for (s of statuses.slice(1); track s) { <option [value]="s">{{ s }}</option> }</select></div>
        <div class="field"><label>{{ 'governorate_2' | t }}</label><select class="select" [(ngModel)]="gov"><option value="All">{{ 'all_2' | t }}</option>@for (g of govs(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'code_2' | t : 'Code' }}</th><th>{{ 'laboratory_3' | t : 'Name' }}</th><th>{{ 'segment' | t }}</th><th>{{ 'governorate_2' | t }}</th><th>{{ 'area_2' | t }}</th><th>{{ 'status' | t }}</th></tr></thead>
          <tbody>
            @for (l of filtered(); track l.id) {
              <tr class="clickable" (click)="open(l.id)">
                <td class="mono">{{ l.displayCode }}</td>
                <td><b style="color:var(--slate-900)">{{ l.name }}</b>@if (l.encrypted) { <span class="badge b-neu" style="margin-inline-start:6px">enc</span> }</td>
                <td>{{ l.segment }}</td><td>{{ l.governorate ?? '—' }}</td><td>{{ l.area ?? '—' }}</td>
                <td><span class="badge" [class]="badge(l.status)">{{ l.status }}</span></td>
              </tr>
            } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">{{ 'no_labs_match' | t : 'No labs match.' }}</td></tr> }
          </tbody>
        </table></div>
        <div class="foot" style="padding:10px 14px;font-size:12px;color:var(--slate-500);border-top:1px solid var(--slate-150)">
          {{ filtered().length }} {{ 'of' | t }} {{ result()?.total ?? 0 }} {{ 'total' | t }}
        </div>
      }
    </div>
  `,
  styles: [`tr.clickable{cursor:pointer}tr.clickable:hover{background:var(--slate-100)}`],
})
export class LabsComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly result = signal<PagedResult<LabListItem> | null>(null);
  readonly items = signal<LabListItem[]>([]);
  readonly segments = SEGMENTS; readonly statuses = STATUSES;
  search = ''; segment = 'All'; status = 'All'; gov = 'All';

  readonly filtered = computed(() => this.items().filter((l) => this.gov === 'All' || l.governorate === this.gov));
  readonly govs = computed(() => [...new Set(this.items().map((l) => l.governorate).filter((x): x is string => !!x))].sort());

  constructor() { this.load(); }

  countStatus(s: string): number { return this.filtered().filter((l) => l.status === s).length; }
  onboarding(): number { return this.filtered().filter((l) => l.status === 'Scanned' || l.status === 'New').length; }
  badge(s: string): string { return s === 'Active' ? 'b-ok' : s === 'Inactive' || s === 'Churned' || s === 'Stopped' ? 'b-bad' : 'b-warn'; }
  open(id: string): void { void this.router.navigate(['/labs', id]); }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 500 };
    if (this.search.trim()) params['search'] = this.search.trim();
    if (this.segment !== 'All') params['segment'] = this.segment;
    if (this.status !== 'All') params['status'] = this.status;
    this.api.get<PagedResult<LabListItem>>('/labs', params).subscribe({
      next: (r) => { this.result.set(r); this.items.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }
}
