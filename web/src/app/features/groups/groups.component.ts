import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface TestGroup { id: string; code: string; nameEn: string; nameAr: string | null; source: string; }

@Component({
  selector: 'app-groups',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px">
      <div><div class="breadcrumbs">Home / {{ 'groups_2' | t : 'Test groups' }}</div><h1>{{ 'groups_2' | t : 'Test groups' }}</h1></div>
      @if (auth.has('OracleIntegration')) {
        <button class="btn btn-s" [disabled]="syncing()" (click)="sync()" title="{{ 'sync_oracle_hint' | t : 'Pull the latest groups (add/edit/delete) from Oracle' }}">
          {{ syncing() ? ('syncing' | t : 'Syncing…') : ('sync_oracle' | t : 'Sync from Oracle') }}
        </button>
      }
    </div>
    @if (banner()) { <div class="inline-banner inline-banner-error">{{ banner() }}</div> }
    @if (notice()) { <div class="inline-banner" style="background:var(--ok-bg,#dff6dd);color:var(--ok-ink,#107c41)">{{ notice() }}</div> }

    <div class="grid" style="grid-template-columns:1fr 2fr;gap:16px;align-items:start">
      @if (auth.has('AddGroups') || editId()) {
        <div class="card" style="padding:20px">
          <h3 style="margin:0 0 15px;font-size:14px;font-weight:600;color:var(--slate-800)">{{ editId() ? ('edit_group' | t : 'Edit group') : ('add_new_group' | t : 'Add group') }}</h3>
          <div class="field"><label>{{ 'group_code' | t : 'Group code' }}</label><input class="input" [(ngModel)]="code" [disabled]="!!editId()" placeholder="e.g. HEM"></div>
          <div class="field" style="margin-top:10px"><label>{{ 'group_name' | t : 'Group name' }}</label><input class="input" [(ngModel)]="name" placeholder="e.g. Hematology"></div>
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
            <input class="input" [ngModel]="query()" (ngModelChange)="query.set($event)" placeholder="{{ 'search_groups' | t : 'Search by code or name…' }}">
            @if (query()) { <button class="fu-search-clear" (click)="query.set('')" title="Clear">×</button> }
          </div>
          <span class="fu-count">{{ filtered().length }} / {{ groups().length }}</span>
        </div>
        @if (loading()) { <div class="empty" style="padding:24px">{{ 'loading' | t : 'Loading…' }}</div> }
        @else {
          <table class="grid-table" style="margin:0;border:none">
            <thead><tr><th>{{ 'group_code_2' | t : 'Code' }}</th><th>{{ 'group_name_2' | t : 'Name' }}</th><th style="width:90px">{{ 'source' | t : 'Source' }}</th><th style="width:130px"></th></tr></thead>
            <tbody>
              @for (g of filtered(); track g.id) {
                <tr><td class="mono">{{ g.code }}</td><td>{{ g.nameEn }}</td>
                  <td>@if (g.source === 'Oracle') { <span class="src-badge src-oracle">Oracle</span> } @else { <span class="src-badge src-manual">Manual</span> }</td>
                  <td class="actions">
                    @if (auth.has('UpdateGroups')) { <button class="btn-ghost" (click)="edit(g)">{{ 'edit_2' | t : 'Edit' }}</button> }
                    @if (auth.has('DeleteGroups')) { <button class="btn-ghost red" (click)="del(g)" [disabled]="busy()">{{ 'delete' | t : 'Delete' }}</button> }
                  </td></tr>
              } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:24px">—</td></tr> }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}
    .src-badge{font-size:11px;padding:2px 8px;border-radius:10px;font-weight:600}
    .src-oracle{background:#eaf2fa;color:#2f7bd2}.src-manual{background:#eceff2;color:#6b7480}
    .fu-toolbar{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 14px;border-bottom:1px solid var(--slate-150)}
    .fu-search{position:relative;flex:1;max-width:360px;display:flex;align-items:center}
    .fu-search .input{padding-inline-start:32px;width:100%}
    .fu-search-ico{position:absolute;inset-inline-start:9px;width:15px;height:15px;color:var(--slate-500);pointer-events:none}
    .fu-search-clear{position:absolute;inset-inline-end:8px;border:none;background:none;font-size:18px;line-height:1;color:var(--slate-500);cursor:pointer}
    .fu-count{font-size:12px;color:var(--slate-500);white-space:nowrap}`],
})
export class GroupsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly syncing = signal(false);
  readonly groups = signal<TestGroup[]>([]);
  readonly query = signal('');
  readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    if (!q) return this.groups();
    return this.groups().filter((g) => g.code.toLowerCase().includes(q) || (g.nameEn ?? '').toLowerCase().includes(q));
  });
  readonly editId = signal<string | null>(null);
  readonly banner = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  code = ''; name = '';

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<TestGroup[]>('/test-groups').subscribe({ next: (g) => { this.groups.set(g); this.loading.set(false); }, error: () => this.loading.set(false) });
  }
  sync(): void {
    this.syncing.set(true); this.banner.set(null); this.notice.set(null);
    this.api.post<{ groupsUpserted: number; groupsDeleted: number }>('/integration/sync-now').subscribe({
      next: (r) => { this.syncing.set(false); this.notice.set(`Synced from Oracle: ${r.groupsUpserted} groups (${r.groupsDeleted} removed).`); this.load(); },
      error: (e) => { this.syncing.set(false); this.banner.set(e?.error?.detail ?? 'Oracle sync failed.'); },
    });
  }
  edit(g: TestGroup): void { this.editId.set(g.id); this.code = g.code; this.name = g.nameEn; }
  reset(): void { this.editId.set(null); this.code = ''; this.name = ''; this.banner.set(null); }
  save(): void {
    this.busy.set(true); this.banner.set(null);
    const id = this.editId();
    const obs = id
      ? this.api.put(`/test-groups/${id}`, { id, nameEn: this.name, nameAr: null })
      : this.api.post('/test-groups', { code: this.code, nameEn: this.name, nameAr: null });
    obs.subscribe({ next: () => { this.busy.set(false); this.reset(); this.load(); }, error: (e) => { this.busy.set(false); this.banner.set(e?.error?.detail ?? 'Save failed.'); } });
  }
  del(g: TestGroup): void {
    if (!window.confirm(`Delete group ${g.code}?`)) return;
    this.busy.set(true);
    this.api.delete(`/test-groups/${g.id}`).subscribe({ next: () => { this.busy.set(false); this.load(); }, error: (e) => { this.busy.set(false); this.banner.set(e?.error?.detail ?? 'Delete failed.'); } });
  }
}
