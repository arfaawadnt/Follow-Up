import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabListItem, PagedResult } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';
import { exportCsv, printTable } from '../../shared/export.util';

const SEGMENTS = ['All', 'A', 'B', 'C', 'D'];
const STATUSES = ['All', 'Scanned', 'Interactive', 'Active', 'Inactive', 'Stopped', 'Pending', 'Suspended', 'Churned'];

@Component({
  selector: 'app-labs',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'lab_mgmt' | t : 'Laboratories' }}</div><h1>{{ 'lab_mgmt' | t : 'Laboratories' }}</h1></div>
      <div class="pagehead-actions" style="display:flex;gap:8px">
        @if (auth.has('AddLabs')) { <a class="btn btn-p" routerLink="/labs/new">{{ 'new_lab_btn' | t : '+ New laboratory' }}</a> }
        <button class="btn btn-s" (click)="exportCsv()" [disabled]="!filtered().length">{{ 'export_excel' | t : 'Export Excel' }}</button>
        <button class="btn btn-s" (click)="exportPdf()" [disabled]="!filtered().length">{{ 'export_pdf' | t : 'Export PDF' }}</button>
        <button class="btn-ghost" (click)="clearFilters()">{{ 'clear_filters' | t : 'Clear Filters' }}</button>
      </div>
    </div>

    <div class="kpis" style="grid-template-columns:repeat(5,1fr);margin-bottom:16px">
      <div class="kpi kpi-blue"><div class="lbl">{{ 'total_labs' | t : 'Total Labs' }}</div><div class="val">{{ filtered().length }}</div><div class="sub">{{ 'matching_current_filters' | t : 'matching filters' }}</div></div>
      <div class="kpi kpi-green"><div class="lbl">{{ 'active_labs_3' | t : 'Active Labs' }}</div><div class="val">{{ countStatus('Active') }}</div><div class="sub">{{ 'generating_daily_visits' | t : 'generating visits' }}</div></div>
      <div class="kpi kpi-amber"><div class="lbl">{{ 'onboarding' | t : 'Onboarding' }}</div><div class="val">{{ onboarding() }}</div><div class="sub">{{ 'scanned_interactive' | t : 'scanned' }}</div></div>
      <div class="kpi kpi-red"><div class="lbl">{{ 'idle_stopped' | t : 'Idle / Stopped' }}</div><div class="val">{{ idleStopped() }}</div></div>
      <div class="kpi kpi-teal"><div class="lbl">{{ 'est_monthly_samples' | t : 'Est. Monthly Samples' }}</div><div class="val">{{ estMonthlySamples() }}</div></div>
    </div>

    <div class="card" style="padding:16px;margin-bottom:16px">
      <div class="frm-grid" style="grid-template-columns:repeat(5,1fr);gap:12px;align-items:end">
        <div class="field"><label>{{ 'search' | t : 'Search' }}</label><input class="input" [(ngModel)]="search" (keyup.enter)="load()" [placeholder]="'search_lab_name_or_code' | t : 'Name or code'"></div>
        <div class="field"><label>{{ 'status' | t }}</label><select class="select" [(ngModel)]="status" (ngModelChange)="load()"><option value="All">{{ 'all' | t }}</option>@for (s of statuses.slice(1); track s) { <option [value]="s">{{ s }}</option> }</select></div>
        <div class="field"><label>{{ 'segment' | t }}</label><select class="select" [(ngModel)]="segment" (ngModelChange)="load()"><option value="All">{{ 'all' | t }}</option>@for (s of segments.slice(1); track s) { <option [value]="s">{{ s }}</option> }</select></div>
        <div class="field"><label>{{ 'governorate_2' | t }}</label><select class="select" [ngModel]="gov()" (ngModelChange)="gov.set($event)"><option value="All">{{ 'all_2' | t }}</option>@for (g of govs(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
        <div class="field"><label>{{ 'city' | t : 'City' }}</label><select class="select" [ngModel]="city()" (ngModelChange)="city.set($event)"><option value="All">{{ 'all_2' | t }}</option>@for (c of cities(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        <div class="field"><label>{{ 'area' | t : 'Area' }}</label><select class="select" [ngModel]="area()" (ngModelChange)="area.set($event)"><option value="All">{{ 'all_2' | t }}</option>@for (a of areas(); track a) { <option [value]="a">{{ a }}</option> }</select></div>
        <div class="field"><label>{{ 'serving_branch' | t : 'Serving branch' }}</label><select class="select" [ngModel]="branch()" (ngModelChange)="branch.set($event)"><option value="All">{{ 'all_2' | t }}</option>@for (b of branches(); track b) { <option [value]="b">{{ b }}</option> }</select></div>
        <div class="field"><label>{{ 'collection_rep' | t : 'Collection rep' }}</label><select class="select" [ngModel]="collectorRep()" (ngModelChange)="collectorRep.set($event)"><option value="All">{{ 'all_2' | t }}</option>@for (r of collectorReps(); track r) { <option [value]="r">{{ r }}</option> }</select></div>
        <div class="field"><label>{{ 'marketing_rep' | t : 'Marketing rep' }}</label><select class="select" [ngModel]="marketingRep()" (ngModelChange)="marketingRep.set($event)"><option value="All">{{ 'all_2' | t }}</option>@for (r of marketingReps(); track r) { <option [value]="r">{{ r }}</option> }</select></div>
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>{{ 'laboratory_3' | t : 'Laboratory' }}</th><th>{{ 'code_2' | t : 'Code' }}</th><th>{{ 'segment' | t }}</th><th>{{ 'status' | t }}</th><th>{{ 'address' | t : 'Address' }}</th>@if (canViewLocation()) { <th>{{ 'map' | t : 'Map' }}</th> }<th>{{ 'collector' | t : 'Collector' }}</th><th>{{ 'marketing' | t : 'Marketing' }}</th><th class="r">{{ 'avg_mo' | t : 'Avg/mo' }}</th><th></th></tr></thead>
          <tbody>
            @for (l of filtered(); track l.id) {
              <tr class="clickable" (click)="open(l.id)">
                <td><b style="color:var(--slate-900)">{{ l.name }}</b>@if (l.branch) { <div class="small muted">{{ l.branch }}</div> }</td>
                <td class="mono">{{ l.displayCode }}@if (l.encrypted) { <span class="badge b-neu" style="margin-inline-start:6px">enc</span> }</td>
                <td>{{ l.segment }}<div class="small muted">{{ l.category ?? '' }}</div></td>
                <td><span class="badge" [class]="badge(l.status)">{{ l.status }}</span></td>
                <td>{{ l.area ?? '—' }}<div class="small muted">{{ l.governorate ?? '' }}</div></td>
                @if (canViewLocation()) { <td>@if (l.latitude != null && l.longitude != null) { <a [href]="mapUrl(l)" target="_blank" rel="noopener" (click)="$event.stopPropagation()">📍 {{ 'map' | t : 'Map' }}</a> } @else { — }</td> }
                <td>{{ l.collectors.length ? l.collectors.join(', ') : '—' }}</td>
                <td>{{ l.marketing ?? '—' }}</td>
                <td class="r mono">{{ l.avgMonthlySamples ?? '—' }}</td>
                <td class="r" style="white-space:nowrap">
                  <button class="btn-ghost" (click)="$event.stopPropagation(); open(l.id)">{{ 'images' | t : 'Images' }}</button>
                  <button class="btn-ghost" (click)="$event.stopPropagation(); open(l.id)">{{ 'edit' | t : 'Edit' }}</button>
                </td>
              </tr>
            } @empty { <tr><td [attr.colspan]="canViewLocation() ? 10 : 9" class="empty" style="text-align:center;padding:24px">{{ 'no_labs_match' | t : 'No labs match.' }}</td></tr> }
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
  search = ''; segment = 'All'; status = 'All';
  readonly gov = signal('All'); readonly city = signal('All'); readonly area = signal('All');
  readonly branch = signal('All'); readonly collectorRep = signal('All'); readonly marketingRep = signal('All');

  readonly filtered = computed(() => this.items().filter((l) =>
    (this.gov() === 'All' || l.governorate === this.gov())
    && (this.city() === 'All' || l.city === this.city())
    && (this.area() === 'All' || l.area === this.area())
    && (this.branch() === 'All' || l.branch === this.branch())
    && (this.collectorRep() === 'All' || l.collectors.includes(this.collectorRep()))
    && (this.marketingRep() === 'All' || l.marketing === this.marketingRep())));
  readonly govs = computed(() => this.distinct((l) => l.governorate));
  readonly cities = computed(() => this.distinct((l) => l.city));
  readonly areas = computed(() => this.distinct((l) => l.area));
  readonly branches = computed(() => this.distinct((l) => l.branch));
  readonly collectorReps = computed(() => [...new Set(this.items().flatMap((l) => l.collectors))].sort());
  readonly marketingReps = computed(() => this.distinct((l) => l.marketing));

  constructor() { this.load(); }

  private distinct(pick: (l: LabListItem) => string | null): string[] {
    return [...new Set(this.items().map(pick).filter((x): x is string => !!x))].sort();
  }

  countStatus(s: string): number { return this.filtered().filter((l) => l.status === s).length; }
  onboarding(): number { return this.filtered().filter((l) => l.status === 'Scanned' || l.status === 'Interactive').length; }
  idleStopped(): number { return this.filtered().filter((l) => l.status === 'Idle' || l.status === 'Stopped').length; }
  estMonthlySamples(): number { return this.filtered().reduce((sum, l) => sum + (l.avgMonthlySamples ?? 0), 0); }
  badge(s: string): string { return s === 'Active' ? 'b-ok' : s === 'Inactive' || s === 'Churned' || s === 'Stopped' ? 'b-bad' : 'b-warn'; }
  canViewLocation(): boolean { return this.auth.has('ViewLabLocation'); }
  open(id: string): void { void this.router.navigate(['/labs', id]); }
  mapUrl(l: LabListItem): string { return `https://maps.google.com/?q=${l.latitude},${l.longitude}`; }

  clearFilters(): void {
    this.search = ''; this.segment = 'All'; this.status = 'All';
    this.gov.set('All'); this.city.set('All'); this.area.set('All');
    this.branch.set('All'); this.collectorRep.set('All'); this.marketingRep.set('All');
    this.load();
  }

  exportCsv(): void {
    const map = this.canViewLocation();
    exportCsv('laboratories.csv',
      ['Laboratory', 'Code', 'Segment', 'Status', 'Address', ...(map ? ['Map'] : []), 'Collector', 'Marketing', 'Avg/mo'],
      this.filtered().map((l) => [l.name, l.displayCode, l.segment, l.status,
        [l.area, l.governorate].filter(Boolean).join(', '),
        ...(map ? [l.latitude != null && l.longitude != null ? this.mapUrl(l) : ''] : []),
        l.collectors.join('; '), l.marketing, l.avgMonthlySamples]));
  }

  exportPdf(): void {
    const map = this.canViewLocation();
    printTable('Laboratory Management',
      ['Laboratory', 'Code', 'Segment', 'Status', 'Address', ...(map ? ['Map'] : []), 'Collector', 'Marketing', 'Avg/mo'],
      this.filtered().map((l) => [l.name, l.displayCode, l.segment, l.status,
        [l.area, l.governorate].filter(Boolean).join(', '),
        ...(map ? [l.latitude != null && l.longitude != null ? `${l.latitude},${l.longitude}` : ''] : []),
        l.collectors.join(', '), l.marketing, l.avgMonthlySamples]));
  }

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
