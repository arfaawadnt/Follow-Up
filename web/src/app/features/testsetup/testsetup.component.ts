import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { FilterSelectComponent } from '../../shared/filter-select.component';
import { TranslatePipe } from '../../core/i18n';

interface TestGroup { id: string; code: string; nameEn: string; }
interface TestSetup {
  id: string; code: string; nameEn: string; nameAr: string | null; groupId: string | null;
  testType: number; cost: number; groupCode: string | null; groupName: string | null; source: string;
}

@Component({
  selector: 'app-testsetup',
  standalone: true,
  imports: [FormsModule, TranslatePipe, DecimalPipe, FilterSelectComponent],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px">
      <div><div class="breadcrumbs">Home / {{ 'testsetup' | t : 'Test setup' }}</div><h1>{{ 'testsetup' | t : 'Test setup' }}</h1></div>
      @if (auth.has('OracleIntegration')) {
        <button class="btn btn-s" [disabled]="syncing()" (click)="sync()" title="{{ 'sync_oracle_hint' | t : 'Pull the latest tests (add/edit/delete) from Oracle' }}">
          {{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
        </button>
      }
    </div>

    <div class="grid" style="grid-template-columns:1fr 2fr;gap:16px;align-items:start">
      @if (auth.has('AddTestsetup') || editId()) {
        <div class="card" style="padding:20px">
          <h3 style="margin:0 0 15px;font-size:14px;font-weight:600;color:var(--slate-800)">{{ editId() ? ('edit_test' | t : 'Edit test') : ('add_new_test' | t : 'Add test') }}</h3>
          <div class="field"><label>{{ 'test_code' | t : 'Test code' }}</label><input class="input" [(ngModel)]="code" [disabled]="!!editId()" placeholder="e.g. GLU"></div>
          <div class="field" style="margin-top:10px"><label>{{ 'test_name' | t : 'Test name' }}</label><input class="input" [(ngModel)]="name" placeholder="e.g. Glucose"></div>
          <div style="display:flex;gap:10px;margin-top:10px">
            <div class="field" style="flex:1"><label>{{ 'test_type' | t : 'Test type' }}</label><input class="input" type="number" [(ngModel)]="testType" placeholder="0"></div>
            <div class="field" style="flex:1"><label>{{ 'cost' | t : 'Cost' }}</label><input class="input" type="number" step="0.01" [(ngModel)]="cost" placeholder="0.00"></div>
          </div>
          <div class="field" style="margin-top:10px"><label>{{ 'parent_group' | t : 'Parent group' }}</label>
            <select class="select" [(ngModel)]="groupId"><option [ngValue]="null">{{ 'select_group_placeholder' | t : '-- Select Group --' }}</option>@for (g of groups(); track g.id) { <option [ngValue]="g.id">{{ g.nameEn }} ({{ g.code }})</option> }</select></div>
          <div style="display:flex;gap:8px;margin-top:15px">
            <button class="btn btn-p" [disabled]="busy() || !code || !name" (click)="save()">{{ 'save_2' | t : 'Save' }}</button>
            @if (editId()) { <button class="btn btn-s" (click)="reset()">{{ 'cancel_2' | t : 'Cancel' }}</button> }
          </div>
        </div>
      }
      <div class="card" style="padding:0;overflow:hidden">
        <div class="fu-toolbar">
          <div class="fu-search">
            <i data-lucide="search" class="fu-search-ico"></i>
            <input class="input" [ngModel]="query()" (ngModelChange)="query.set($event); page.set(1)" placeholder="{{ 'search_tests' | t : 'Search by code, name, or group…' }}">
            @if (query()) { <button class="fu-search-clear" (click)="query.set(''); page.set(1)" title="Clear">×</button> }
          </div>
          <app-filter-select class="fu-group-filter" [options]="groupOptions()" [ngModel]="groupFilter()" (ngModelChange)="groupFilter.set($event); page.set(1)" [placeholder]="'all_groups' | t : 'All groups'"></app-filter-select>
          <span class="fu-count">{{ filtered().length }} / {{ setups().length }}</span>
        </div>
        @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <table class="grid-table" style="margin:0;border:none">
            <thead><tr><th style="width:90px">{{ 'test_code_2' | t : 'Test Code' }}</th><th>{{ 'test_name_2' | t : 'Test Name' }}</th><th>{{ 'group_name_2' | t : 'Group Name' }}</th><th style="width:60px">{{ 'test_type' | t : 'Type' }}</th><th style="width:90px">{{ 'cost' | t : 'Cost' }}</th><th style="width:80px">{{ 'source' | t : 'Source' }}</th><th style="width:130px"></th></tr></thead>
            <tbody>
              @for (s of paged(); track s.id) {
                <tr><td class="mono">{{ s.code }}</td><td>{{ s.nameEn }}</td>
                  <td>{{ s.groupName ?? '—' }}@if (s.groupCode) { <span class="gcode">{{ s.groupCode }}</span> }</td>
                  <td class="mono">{{ s.testType }}</td><td class="mono">{{ s.cost | number:'1.2-2' }}</td>
                  <td>@if (s.source === 'Oracle') { <span class="src-badge src-oracle">Oracle</span> } @else { <span class="src-badge src-manual">Manual</span> }</td>
                  <td class="actions">
                    @if (auth.has('UpdateTestsetup')) { <button class="btn-ghost" (click)="edit(s)">{{ 'edit_2' | t : 'Edit' }}</button> }
                    @if (auth.has('DeleteTestsetup')) { <button class="btn-ghost red" (click)="del(s)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button> }
                  </td></tr>
              } @empty { <tr><td colspan="7" class="empty" style="text-align:center;padding:24px">—</td></tr> }
            </tbody>
          </table>
          <div class="fu-pager">
            <button class="btn-ghost" [disabled]="page() <= 1" (click)="page.set(page() - 1)">‹ Prev</button>
            <span>Page {{ page() }} / {{ pageCount() }} · {{ filtered().length }} items</span>
            <button class="btn-ghost" [disabled]="page() >= pageCount()" (click)="page.set(page() + 1)">Next ›</button>
            <select class="select" [ngModel]="pageSize()" (ngModelChange)="pageSize.set(+$event); page.set(1)" style="max-width:90px;margin-inline-start:auto">
              <option [ngValue]="25">25</option><option [ngValue]="50">50</option><option [ngValue]="100">100</option>
            </select>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}
    .src-badge{font-size:11px;padding:2px 8px;border-radius:10px;font-weight:600}
    .src-oracle{background:#eaf2fa;color:#2f7bd2}.src-manual{background:#eceff2;color:#6b7480}
    .gcode{display:inline-block;margin-inline-start:6px;font-size:11px;color:var(--slate-500);background:var(--slate-100);padding:1px 6px;border-radius:8px}
    .fu-toolbar{display:flex;align-items:center;gap:10px;padding:12px 14px;border-bottom:1px solid var(--slate-150)}
    .fu-search{position:relative;flex:1;max-width:340px;display:flex;align-items:center}
    .fu-search .input{padding-inline-start:32px;width:100%}
    .fu-search-ico{position:absolute;inset-inline-start:9px;width:15px;height:15px;color:var(--slate-500);pointer-events:none}
    .fu-search-clear{position:absolute;inset-inline-end:8px;border:none;background:none;font-size:18px;line-height:1;color:var(--slate-500);cursor:pointer}
    .fu-group-filter{max-width:200px}
    .fu-count{font-size:12px;color:var(--slate-500);white-space:nowrap;margin-inline-start:auto}
    .fu-pager{display:flex;align-items:center;gap:12px;padding:12px 14px;border-top:1px solid var(--slate-150);font-size:12.5px;color:var(--slate-600)}`],
})
export class TestSetupComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly syncing = signal(false);
  readonly groups = signal<TestGroup[]>([]);
  readonly setups = signal<TestSetup[]>([]);
  readonly query = signal('');
  readonly groupFilter = signal<string | null>(null);
  readonly groupOptions = computed(() => this.groups().map((g) => ({ value: g.id, label: `${g.nameEn} (${g.code})` })));
  readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const gf = this.groupFilter();
    return this.setups().filter((s) => {
      if (gf && s.groupId !== gf) return false;
      if (!q) return true;
      return s.code.toLowerCase().includes(q)
        || (s.nameEn ?? '').toLowerCase().includes(q)
        || (s.groupName ?? '').toLowerCase().includes(q)
        || (s.groupCode ?? '').toLowerCase().includes(q);
    });
  });
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.filtered().length / this.pageSize())));
  readonly paged = computed(() => {
    const p = Math.min(this.page(), this.pageCount());
    const start = (p - 1) * this.pageSize();
    return this.filtered().slice(start, start + this.pageSize());
  });
  readonly editId = signal<string | null>(null);
  code = ''; name = ''; groupId: string | null = null; testType = 0; cost = 0;

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<TestGroup[]>('/test-groups').subscribe({ next: (g) => this.groups.set(g) });
    this.api.get<TestSetup[]>('/test-setups').subscribe({ next: (s) => { this.setups.set(s); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  sync(): void {
    this.syncing.set(true);
    this.api.post<{ testsUpserted: number; testsDeleted: number }>('/integration/sync-now').subscribe({
      next: (r) => { this.syncing.set(false); this.toast.success(`Synced from Oracle: ${r.testsUpserted} tests (${r.testsDeleted} removed).`); this.load(); },
      error: () => { this.syncing.set(false); },
    });
  }
  groupName(id: string | null): string { return id ? (this.groups().find((g) => g.id === id)?.code ?? '—') : '—'; }
  edit(s: TestSetup): void { this.editId.set(s.id); this.code = s.code; this.name = s.nameEn; this.groupId = s.groupId; this.testType = s.testType; this.cost = s.cost; }
  reset(): void { this.editId.set(null); this.code = ''; this.name = ''; this.groupId = null; this.testType = 0; this.cost = 0; }
  save(): void {
    this.busy.set(true);
    const id = this.editId();
    const body = { nameEn: this.name, nameAr: null, groupId: this.groupId, testType: Number(this.testType) || 0, cost: Number(this.cost) || 0 };
    const obs = id
      ? this.api.put(`/test-setups/${id}`, { id, ...body })
      : this.api.post('/test-setups', { code: this.code, ...body });
    obs.subscribe({ next: () => { this.busy.set(false); this.reset(); this.load(); }, error: () => { this.busy.set(false); } });
  }
  del(s: TestSetup): void {
    if (!window.confirm(`Delete test ${s.code}?`)) return;
    this.busy.set(true);
    this.api.delete(`/test-setups/${s.id}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: () => { this.busy.set(false); } });
  }
}
