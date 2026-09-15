import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';
import { ToastService } from '../../core/toast.service';
import { RefItem, RoleItem, TreasuryGrant } from '../../core/models';

interface MatrixSpecial { priv: string; key: string; label: string; }
interface MatrixRow { key: string; label: string; view: string | null; add: string | null; update: string | null; special: MatrixSpecial[]; }

/**
 * Privilege matrix: every privilege in Privileges.cs appears exactly once (Verify/Resolve are the standalone
 * checkboxes). Guarded by RolePrivilegeMatrixTests (ArchitectureTests): a privilege added to the backend without a
 * checkbox here fails the build, so the page can never silently fall behind the catalogue again.
 * Rows map to sidebar pages; a page gated by another page's privilege (Lab Checkin → Transfers/Confirm, Rep
 * Intervals → Reports/View, Groups + Test Setup → Test Stats/View) is named on that checkbox rather than given an
 * empty row. The "Add" column on the stats pages is the manual "Sync from Oracle" action.
 */
const MATRIX: MatrixRow[] = [
  { key: 'page_dashboard', label: 'Dashboard', view: 'ViewDashboard', add: null, update: null, special: [] },
  { key: 'page_labs', label: 'Labs', view: null, add: 'AddLabs', update: 'UpdateLabs', special: [
    { priv: 'ManageLabs', key: 'priv_manage', label: 'Manage' },
    { priv: 'ViewLabLocation', key: 'priv_lab_location', label: 'Lab Location' },
    { priv: 'ShowEncryptedLabs', key: 'priv_show_encrypted', label: 'Show Encrypted' },
  ] },
  { key: 'page_representatives', label: 'Representatives', view: 'ViewReps', add: 'AddReps', update: 'UpdateReps', special: [
    { priv: 'ManageReps', key: 'priv_manage', label: 'Manage' },
  ] },
  { key: 'page_daily_followup', label: 'Daily Follow-up', view: 'ViewDailyFollowup', add: 'AddDailyFollowup', update: 'UpdateDailyFollowup', special: [] },
  { key: 'page_transfers', label: 'Transfers', view: 'ViewTransfers', add: null, update: null, special: [
    { priv: 'ManageTransfers', key: 'priv_manage', label: 'Manage' },
    { priv: 'ConfirmTransfers', key: 'priv_confirm_checkin', label: 'Confirm (Lab Checkin)' },
  ] },
  { key: 'page_sample_tracking', label: 'Sample Tracking', view: 'SampleTracking', add: null, update: null, special: [] },
  { key: 'page_outsource', label: 'Outsource', view: 'OutsourceSamples', add: null, update: null, special: [] },
  { key: 'page_lab_stats', label: 'Lab Stats', view: 'ViewLabStats', add: 'AddLabStats', update: null, special: [] },
  { key: 'page_test_stats', label: 'Test Stats', view: 'ViewTeststats', add: 'AddTeststats', update: null, special: [] },
  { key: 'page_area_stats', label: 'Area Stats', view: 'ViewAreaStats', add: 'AddAreaStats', update: null, special: [] },
  { key: 'page_detailed_stats', label: 'Detailed Stats', view: 'ViewDetailedStats', add: null, update: null, special: [] },
  { key: 'page_reports', label: 'Reports & Rep Intervals', view: 'ViewReports', add: null, update: null, special: [] },
  { key: 'page_accounting', label: 'Accounting', view: 'ViewAccounting', add: null, update: null, special: [
    { priv: 'ManageAccounting', key: 'priv_manage', label: 'Manage' },
  ] },
  { key: 'page_marketing', label: 'Marketing', view: 'ViewMarketing', add: 'AddMarketing', update: 'UpdateMarketing', special: [] },
  { key: 'page_complaints', label: 'Complaints', view: 'ViewComplaints', add: 'AddComplaints', update: 'UpdateComplaints', special: [
    { priv: 'ManageComplaints', key: 'priv_manage', label: 'Manage' },
  ] },
  { key: 'page_groups', label: 'Groups', view: null, add: 'AddGroups', update: 'UpdateGroups', special: [
    { priv: 'DeleteGroups', key: 'priv_delete', label: 'Delete' },
  ] },
  { key: 'page_test_setup', label: 'Test Setup', view: null, add: 'AddTestsetup', update: 'UpdateTestsetup', special: [
    { priv: 'DeleteTestsetup', key: 'priv_delete', label: 'Delete' },
  ] },
  { key: 'page_loyalty', label: 'Loyalty', view: null, add: null, update: null, special: [
    { priv: 'ManageLoyalty', key: 'priv_manage', label: 'Manage' },
  ] },
  { key: 'page_commissions', label: 'Commissions', view: null, add: null, update: null, special: [
    { priv: 'ManageCommissions', key: 'priv_manage', label: 'Manage' },
  ] },
  { key: 'page_users', label: 'Users', view: null, add: null, update: null, special: [
    { priv: 'ManageUsers', key: 'priv_manage', label: 'Manage' },
  ] },
  { key: 'page_setup', label: 'Setup', view: null, add: null, update: null, special: [
    { priv: 'SetupRefs', key: 'priv_references', label: 'References' },
    { priv: 'SetupCities', key: 'priv_cities', label: 'Cities' },
    { priv: 'SetupAreas', key: 'priv_areas', label: 'Areas' },
  ] },
  { key: 'page_oracle', label: 'Oracle', view: null, add: null, update: null, special: [
    { priv: 'OracleIntegration', key: 'priv_integration', label: 'Integration' },
  ] },
  { key: 'page_email_reports', label: 'Email Reports', view: null, add: null, update: null, special: [
    { priv: 'ManageEmailReports', key: 'priv_manage', label: 'Manage' },
  ] },
];

@Component({
  selector: 'app-roles',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'roles' | t : 'Roles' }}</div><h1>{{ 'role_privilege' | t : 'Role Privilege' }}</h1></div></div>

    <div class="grid" style="grid-template-columns:1fr 3fr;gap:16px;align-items:start">
      <div class="card" style="padding:16px">
        <h3 style="margin:0 0 10px;font-size:13px;font-weight:700;color:var(--slate-600);text-transform:uppercase">{{ 'roles' | t : 'Roles' }}</h3>
        @for (r of roles(); track r.id) {
          <div class="rolerow" [class.on]="selected()?.id === r.id" (click)="select(r)">
            <span>{{ r.name }}</span>@if (r.isBuiltIn) { <span class="badge b-neu">built-in</span> }
          </div>
        }
        @if (auth.has('ManageUsers')) {
          <div style="margin-top:12px;display:flex;gap:6px">
            <input class="input" [(ngModel)]="newName" placeholder="New role name" style="flex:1">
            <button class="btn btn-mini btn-p" [disabled]="!newName || busy()" (click)="createRole()">Add</button>
          </div>
        }
      </div>

      <div class="card" style="padding:20px">
        @if (selected(); as r) {
          <div class="hrow" style="margin-bottom:12px"><h3 style="margin:0;font-size:16px;font-weight:700;color:var(--slate-900);flex:1">{{ r.name }}</h3>
            @if (auth.has('ManageUsers') && !r.isBuiltIn) {
              <button class="btn btn-mini btn-p" [disabled]="busy()" (click)="savePrivs()">{{ 'save' | t : 'Save' }}</button>
              <button class="btn btn-mini btn-d" [disabled]="busy()" (click)="delRole(r)">{{ 'delete' | t : 'Delete' }}</button>
            }
          </div>

          <div class="matrixwrap grid-scroll">
            <table class="privmatrix">
              <thead><tr>
                <th>{{ 'system_page' | t : 'System Page' }}</th>
                <th>{{ 'view' | t : 'View' }}</th>
                <th>{{ 'add' | t : 'Add' }}</th>
                <th>{{ 'update' | t : 'Update' }}</th>
                <th>{{ 'delete_special' | t : 'Delete / Special' }}</th>
              </tr></thead>
              <tbody>
                @for (row of matrix; track row.key) {
                  <tr>
                    <td class="pagename">{{ row.key | t : row.label }}</td>
                    @for (p of [row.view, row.add, row.update]; track $index) {
                      <td class="ccell">
                        @if (p) { <input type="checkbox" [checked]="draft().has(p)" [disabled]="lock()" (change)="togglePriv(p, $event)"> }
                        @else { <span class="dash">—</span> }
                      </td>
                    }
                    <td class="ccell scell">
                      @if (row.special.length) {
                        @for (s of row.special; track s.priv) {
                          <label class="spec"><input type="checkbox" [checked]="draft().has(s.priv)" [disabled]="lock()" (change)="togglePriv(s.priv, $event)"><span>{{ s.key | t : s.label }}</span></label>
                        }
                      } @else { <span class="dash">—</span> }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <div class="standalones">
            <label class="spec"><input type="checkbox" [checked]="draft().has('VerifyDailyFollowup')" [disabled]="lock()" (change)="togglePriv('VerifyDailyFollowup', $event)"><span>{{ 'priv_verify' | t : 'Verify' }}</span></label>
            <label class="spec"><input type="checkbox" [checked]="draft().has('ResolveComplaints')" [disabled]="lock()" (change)="togglePriv('ResolveComplaints', $event)"><span>{{ 'priv_resolve' | t : 'Resolve' }}</span></label>
          </div>

          <div class="defaults">
            <div class="field"><label>{{ 'default_language' | t : 'Default Language' }}</label>
              <select class="select" [ngModel]="defLang()" (ngModelChange)="defLang.set($event)" [disabled]="lock()">
                <option value="en">English</option>
                <option value="ar">العربية</option>
              </select>
            </div>
            <div class="field"><label>{{ 'default_color_mood' | t : 'Default Color Mood' }}</label>
              <select class="select" [ngModel]="defTheme()" (ngModelChange)="defTheme.set($event)" [disabled]="lock()">
                <option value="light">{{ 'light' | t : 'Light' }}</option>
                <option value="dark">{{ 'dark' | t : 'Dark' }}</option>
              </select>
            </div>
          </div>

          <div class="scopes">
            <div class="scopebox">
              <div class="scopehead">
                <h4>{{ 'branches' | t : 'Branches' }}</h4>
                <label class="spec"><input type="checkbox" [checked]="allSelected('branches')" [disabled]="lock()" (change)="toggleAll('branches', $event)"><span>{{ 'select_all' | t : 'Select All' }}</span></label>
              </div>
              <div class="scopegrid">
                @for (b of branchOptions(); track b) {
                  <label class="spec"><input type="checkbox" [checked]="scopeBranches().has(b)" [disabled]="lock()" (change)="toggleScope('branches', b, $event)"><span>{{ b }}</span></label>
                }
                @if (!branchOptions().length) { <span class="dash">—</span> }
              </div>
            </div>
            <div class="scopebox">
              <div class="scopehead">
                <h4>{{ 'governorates' | t : 'Governorates' }}</h4>
                <label class="spec"><input type="checkbox" [checked]="allSelected('governorates')" [disabled]="lock()" (change)="toggleAll('governorates', $event)"><span>{{ 'select_all' | t : 'Select All' }}</span></label>
              </div>
              <div class="scopegrid">
                @for (g of governorateOptions(); track g) {
                  <label class="spec"><input type="checkbox" [checked]="scopeGovernorates().has(g)" [disabled]="lock()" (change)="toggleScope('governorates', g, $event)"><span>{{ g }}</span></label>
                }
                @if (!governorateOptions().length) { <span class="dash">—</span> }
              </div>
            </div>
          </div>

          @if (treasuryGrants().length) {
            <div class="scopebox" style="margin-top:16px">
              <div class="scopehead"><h4>{{ 'treasury_rights' | t : 'Treasury rights' }}</h4><span class="small muted">{{ 'treasury_rights_hint' | t : 'Per treasury: View the account, Validate mirrored collections, Update entries (records manual entries; corrects validated ones).' }}</span></div>
              <div class="matrixwrap grid-scroll"><table class="privmatrix">
                <thead><tr><th>{{ 'treasury' | t : 'Treasury' }}</th><th>{{ 'view' | t : 'View' }}</th><th>{{ 'validate' | t : 'Validate' }}</th><th>{{ 'update' | t : 'Update' }}</th></tr></thead>
                <tbody>
                  @for (g of treasuryGrants(); track g.treasuryId) {
                    <tr>
                      <td class="pagename">{{ g.treasuryName }}@if (!g.isActive) { <span class="badge b-neu" style="margin-inline-start:6px">{{ 'inactive' | t : 'Inactive' }}</span> }</td>
                      <td class="ccell"><input type="checkbox" [checked]="g.canView || g.canValidate || g.canUpdate" [disabled]="lock() || r.isBuiltIn || g.canValidate || g.canUpdate" (change)="toggleGrant(g, 'canView', $event)"></td>
                      <td class="ccell"><input type="checkbox" [checked]="g.canValidate" [disabled]="lock() || r.isBuiltIn" (change)="toggleGrant(g, 'canValidate', $event)"></td>
                      <td class="ccell"><input type="checkbox" [checked]="g.canUpdate" [disabled]="lock() || r.isBuiltIn" (change)="toggleGrant(g, 'canUpdate', $event)"></td>
                    </tr>
                  }
                </tbody>
              </table></div>
              @if (r.isBuiltIn) { <p class="muted small" style="margin:8px 0 0">{{ 'admin_all_treasuries' | t : 'The built-in administrator holds every right on every treasury.' }}</p> }
            </div>
          }

          @if (r.isBuiltIn) { <p class="muted small" style="margin-top:12px">Built-in roles cannot be edited.</p> }
        } @else { <div class="empty">{{ 'select_role' | t : 'Select a role.' }}</div> }
      </div>
    </div>
  `,
  styles: [`
    .rolerow{padding:8px 10px;border-radius:var(--r-md);cursor:pointer;display:flex;gap:8px;align-items:center;font-size:13px}
    .rolerow:hover{background:var(--slate-100)} .rolerow.on{background:var(--primary-blue-light);color:var(--primary-blue);font-weight:600}
    .rolerow .badge{margin-inline-start:auto}
    .matrixwrap{overflow-x:auto}
    .privmatrix{width:100%;border-collapse:collapse;font-size:12.5px}
    .privmatrix th{text-align:start;padding:8px 10px;background:var(--slate-100);color:var(--slate-600);font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.03em;white-space:nowrap}
    .privmatrix td{padding:6px 10px;border-top:1px solid var(--slate-200);vertical-align:middle}
    .pagename{font-weight:600;color:var(--slate-800);white-space:nowrap}
    .ccell{text-align:center} .ccell input{vertical-align:middle}
    .scell{text-align:start}
    .dash{color:var(--slate-600);opacity:.5}
    .spec{display:inline-flex;gap:6px;align-items:center;font-size:12.5px;color:var(--slate-800);margin-inline-end:14px;white-space:nowrap}
    .standalones{display:flex;gap:8px;margin-top:12px;padding:8px 10px;background:var(--slate-100);border-radius:var(--r-md)}
    .defaults{display:flex;gap:16px;margin-top:16px;flex-wrap:wrap}
    .defaults .field{display:flex;flex-direction:column;gap:4px;min-width:200px}
    .defaults label{font-size:12px;font-weight:600;color:var(--slate-600)}
    .scopes{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-top:16px;align-items:start}
    .scopebox{border:1px solid var(--slate-200);border-radius:var(--r-md);padding:10px 12px}
    .scopehead{display:flex;align-items:center;justify-content:space-between;margin-bottom:8px}
    .scopehead h4{margin:0;font-size:12px;font-weight:700;color:var(--slate-600);text-transform:uppercase}
    .scopegrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:6px 12px}
    .btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}
  `],
})
export class RolesComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  readonly busy = signal(false);
  readonly roles = signal<RoleItem[]>([]);
  readonly selected = signal<RoleItem | null>(null);
  readonly draft = signal<Set<string>>(new Set());
  readonly defLang = signal('en');
  readonly defTheme = signal('light');
  readonly branchOptions = signal<string[]>([]);
  readonly governorateOptions = signal<string[]>([]);
  readonly scopeBranches = signal<Set<string>>(new Set());
  readonly scopeGovernorates = signal<Set<string>>(new Set());
  readonly matrix = MATRIX;
  newName = '';

  constructor() {
    this.load();
    this.api.get<RefItem[]>('/setup/refs', { type: 'Branch' }).subscribe({ next: (r) => this.branchOptions.set(r.map((x) => x.nameEn)) });
    this.api.get<RefItem[]>('/setup/refs', { type: 'Governorate' }).subscribe({ next: (r) => this.governorateOptions.set(r.map((x) => x.nameEn)) });
  }

  load(): void {
    this.api.get<RoleItem[]>('/setup/roles').subscribe({
      next: (r) => {
        this.roles.set(r);
        const cur = this.selected();
        const again = cur ? r.find((x) => x.id === cur.id) : undefined;
        if (again) this.select(again); else if (r.length && !cur) this.select(r[0]);
      },
    });
  }

  select(r: RoleItem): void {
    this.selected.set(r);
    this.draft.set(new Set(r.privileges));
    this.defLang.set(r.defaultLanguage || 'en');
    this.defTheme.set(r.defaultTheme || 'light');
    this.scopeBranches.set(new Set(r.scope?.branches ?? []));
    this.scopeGovernorates.set(new Set(r.scope?.governorates ?? []));
    // Per-treasury rights (every treasury, granted or not). Built-in admin: shown read-only as all-granted.
    this.treasuryGrants.set([]);
    this.api.get<TreasuryGrant[]>(`/accounting/treasury/grants/${r.id}`).subscribe({
      next: (g) => this.treasuryGrants.set(r.isBuiltIn ? g.map((x) => ({ ...x, canView: true, canValidate: true, canUpdate: true })) : g),
      error: () => {},
    });
  }

  /** The selected role's treasury rights being edited (saved with the role). */
  readonly treasuryGrants = signal<TreasuryGrant[]>([]);
  toggleGrant(g: TreasuryGrant, right: 'canView' | 'canValidate' | 'canUpdate', e: Event): void {
    const on = (e.target as HTMLInputElement).checked;
    this.treasuryGrants.update((list) => list.map((x) => {
      if (x.treasuryId !== g.treasuryId) return x;
      const next = { ...x, [right]: on };
      if (right !== 'canView' && on) next.canView = true;          // Validate / Update need the page
      if (right === 'canView' && !on) { next.canValidate = false; next.canUpdate = false; }
      return next;
    }));
  }

  lock(): boolean { const r = this.selected(); return !r || r.isBuiltIn || !this.auth.has('ManageUsers'); }
  togglePriv(p: string, e: Event): void { const on = (e.target as HTMLInputElement).checked; this.draft.update((s) => { const n = new Set(s); on ? n.add(p) : n.delete(p); return n; }); }

  private scopeSig(kind: 'branches' | 'governorates') { return kind === 'branches' ? this.scopeBranches : this.scopeGovernorates; }
  private optionsSig(kind: 'branches' | 'governorates') { return kind === 'branches' ? this.branchOptions : this.governorateOptions; }
  allSelected(kind: 'branches' | 'governorates'): boolean {
    const opts = this.optionsSig(kind)(); const sel = this.scopeSig(kind)();
    return opts.length > 0 && opts.every((o) => sel.has(o));
  }
  toggleAll(kind: 'branches' | 'governorates', e: Event): void {
    const on = (e.target as HTMLInputElement).checked;
    this.scopeSig(kind).set(on ? new Set(this.optionsSig(kind)()) : new Set());
  }
  toggleScope(kind: 'branches' | 'governorates', name: string, e: Event): void {
    const on = (e.target as HTMLInputElement).checked;
    this.scopeSig(kind).update((s) => { const n = new Set(s); on ? n.add(name) : n.delete(name); return n; });
  }

  savePrivs(): void {
    const r = this.selected(); if (!r) return;
    this.busy.set(true);
    const scope = {
      branches: [...this.scopeBranches()],
      governorates: [...this.scopeGovernorates()],
      cities: r.scope?.cities ?? [],
      areas: r.scope?.areas ?? [],
      categories: r.scope?.categories ?? [],
      segments: r.scope?.segments ?? [],
    };
    this.api.put(`/setup/roles/${r.id}`, {
      id: r.id, name: r.name, privileges: [...this.draft()],
      defaultLanguage: this.defLang(), defaultTheme: this.defTheme(), scope,
    }).subscribe({
      next: () => {
        // Then the treasury rights (separate resource; the page always sends the full list, so omissions revoke).
        const grants = this.treasuryGrants().map((g) => ({ treasuryId: g.treasuryId, canView: g.canView, canValidate: g.canValidate, canUpdate: g.canUpdate }));
        if (!grants.length) { this.busy.set(false); this.toast.success('Role saved.'); this.load(); return; }
        this.api.put(`/accounting/treasury/grants/${r.id}`, grants).subscribe({
          next: () => { this.busy.set(false); this.toast.success('Role saved.'); this.load(); },
          error: () => { this.busy.set(false); this.load(); },
        });
      },
      error: () => { this.busy.set(false); },
    });
  }
  createRole(): void {
    this.busy.set(true);
    this.api.post('/setup/roles', { name: this.newName, privileges: [], defaultLanguage: 'en', defaultTheme: 'light' }).subscribe({
      next: () => { this.busy.set(false); this.newName = ''; this.toast.success('Role created.'); this.load(); }, error: () => { this.busy.set(false); },
    });
  }
  delRole(r: RoleItem): void {
    if (!window.confirm(`Delete role ${r.name}?`)) return;
    this.busy.set(true);
    this.api.delete(`/setup/roles/${r.id}`).subscribe({ next: () => { this.busy.set(false); this.selected.set(null); this.load(); }, error: () => { this.busy.set(false); } });
  }
}
