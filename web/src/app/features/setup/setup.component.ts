import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { PagedResult, RepListItem } from '../../core/models';
import { FilterSelectComponent } from '../../shared/filter-select.component';

interface RefItem { id: string; type: string; code: string; nameEn: string; nameAr: string | null; realName: string | null; sortOrder: number; source: string; targetIncomeFrom: number | null; targetIncomeTo: number | null; }
interface City { id: string; name: string; governorate: string; realName: string | null; source: string; }
interface Area {
  id: string; name: string; cityId: string; transportationRequired: boolean; transferReps: string[]; realName: string | null; source: string;
  /** Area management roles — reps of type AreaManager / AreaResponsible. */
  areaManagerId: string | null; areaResponsibleId: string | null;
}
interface Tier { name: string; minAchievementPercent: number; points: number; }
interface CompConfig { commissionRatePercent: number; bonusThresholdPercent: number; bonusAmount: number; tiers: Tier[]; }

type Tab =
  | 'governorates' | 'cities' | 'areas' | 'labcategories' | 'segments'
  | 'branches' | 'payers' | 'contracts' | 'compensation';

/** RefType-backed tabs → the backend RefType name they manage. */
const REF_MAP: Partial<Record<Tab, string>> = {
  governorates: 'Governorate', labcategories: 'LabCategory', segments: 'Segment',
  branches: 'Branch', payers: 'Payer', contracts: 'ContractType',
};

const TABS: { key: Tab; label: string }[] = [
  { key: 'governorates', label: 'Governorates' },
  { key: 'cities', label: 'Cities' },
  { key: 'areas', label: 'Areas' },
  { key: 'labcategories', label: 'Lab Categories' },
  { key: 'segments', label: 'Segments' },
  { key: 'branches', label: 'Branches' },
  { key: 'payers', label: 'Payers' },
  { key: 'contracts', label: 'Contracts' },
  { key: 'compensation', label: 'Commissions & Loyalty' },
];

@Component({
  selector: 'app-setup',
  standalone: true,
  imports: [FormsModule, DecimalPipe, FilterSelectComponent],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:flex-start;gap:12px">
      <div><div class="breadcrumbs">Home / Setup & Configuration</div><h1>Setup &amp; Configuration</h1></div>
      @if (auth.has('OracleIntegration') && oracleTab()) {
        <button class="btn btn-s" [disabled]="syncing()" (click)="sync()" title="Pull the latest reference data from Oracle">
          {{ syncing() ? 'Syncing…' : 'Sync from Oracle' }}
        </button>
      }
    </div>

    <div class="tabbar">
      @for (t of tabs; track t.key) {
        <button class="tab" [class.on]="tab() === t.key" (click)="select(t.key)">{{ t.label }}</button>
      }
    </div>

    <!-- ===== Reference-type tabs (single Name) ===== -->
    @if (isRefTab()) {
      <div class="setup-grid">
        <div class="card panel">
          <h3>Create New {{ singular() }}</h3>
          <label class="lbl">{{ singular() }} Name</label>
          <input class="input" [(ngModel)]="newName" placeholder="e.g. New Value" [disabled]="!canEdit()">
          @if (tab() === 'segments') {
            <div style="display:flex;gap:8px;margin-top:10px">
              <div style="flex:1"><label class="lbl">Monthly income from</label><input class="input" type="number" min="0" [(ngModel)]="newFrom" placeholder="0" [disabled]="!canEdit()"></div>
              <div style="flex:1"><label class="lbl">Monthly income to</label><input class="input" type="number" min="0" [(ngModel)]="newTo" placeholder="∞ (blank = top tier)" [disabled]="!canEdit()"></div>
            </div>
          }
          <button class="btn btn-p" style="margin-top:14px" [disabled]="!newName.trim() || busy() || !canEdit()" (click)="addRef()">Add</button>
        </div>
        <div class="card panel">
          <div class="setup-toolbar"><h3 style="margin:0">Current Items</h3><input class="input srch" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="Search…"><span class="cnt">{{ refsF().length }}/{{ refs().length }}</span></div>
          <table class="items">
            <thead><tr><th>{{ singular() }}</th>@if (tab() === 'governorates') { <th>Real Name</th> }@if (tab() === 'segments') { <th style="width:120px">Income from</th><th style="width:120px">Income to</th> }<th style="width:80px">Source</th><th class="ar">Actions</th></tr></thead>
            <tbody>
              @for (r of refsF(); track r.id) {
                <tr>
                  <td>
                    @if (editId() === r.id) { <input class="input" [(ngModel)]="editName"> }
                    @else { <b>{{ r.nameEn }}</b> }
                  </td>
                  @if (tab() === 'governorates') {
                    <td>@if (editId() === r.id) { <input class="input" [(ngModel)]="editRealName" placeholder="optional real name"> } @else { {{ r.realName || '—' }} }</td>
                  }
                  @if (tab() === 'segments') {
                    <td>@if (editId() === r.id) { <input class="input" type="number" min="0" [(ngModel)]="editFrom" placeholder="0"> } @else { {{ r.targetIncomeFrom === null ? '—' : (r.targetIncomeFrom | number) }} }</td>
                    <td>@if (editId() === r.id) { <input class="input" type="number" min="0" [(ngModel)]="editTo" placeholder="∞"> } @else { {{ r.targetIncomeTo === null ? '∞' : (r.targetIncomeTo | number) }} }</td>
                  }
                  <td>@if (r.source === 'Oracle') { <span class="src-b src-o">Oracle</span> } @else { <span class="src-b src-m">Manual</span> }</td>
                  <td class="ar actions">
                    @if (canEdit()) {
                      @if (editId() === r.id) {
                        <button class="btn btn-mini btn-p" [disabled]="!editName.trim() || busy()" (click)="saveRef(r)">Save</button>
                        <button class="btn btn-mini btn-s" (click)="cancelEdit()">Cancel</button>
                      } @else {
                        <button class="icon-btn" title="Edit" (click)="startEdit(r.id, r.nameEn, r.realName, r.targetIncomeFrom, r.targetIncomeTo)">✎</button>
                        <button class="icon-btn del" title="Delete" (click)="delRef(r)">🗑</button>
                      }
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="3" class="empty">No items yet.</td></tr> }
            </tbody>
          </table>
        </div>
        @if (tab() === 'segments') {
          <div class="card panel" style="grid-column:1/-1">
            <h3>Monthly segment auto-assignment</h3>
            <p class="lbl" style="max-width:640px">On the 1st of each month every lab is automatically reassigned to the segment whose income band contains its <b>previous month's achieved income</b> (patient + insurance fees). Use the button to run it now for last month.</p>
            <button class="btn btn-s" [disabled]="busy() || !canEdit()" (click)="assignSegments()">Run assignment now</button>
          </div>
        }
      </div>
    }

    <!-- ===== Cities ===== -->
    @if (tab() === 'cities') {
      <div class="setup-grid">
        <div class="card panel">
          <h3>Create New City</h3>
          <label class="lbl">City Name</label>
          <input class="input" [(ngModel)]="cityName" placeholder="e.g. New Value" [disabled]="!canEdit()">
          <label class="lbl" style="margin-top:10px">Governorate</label>
          <select class="select" [(ngModel)]="cityGov" [disabled]="!canEdit()">
            <option value="">—</option>
            @for (g of govOptions(); track g) { <option [value]="g">{{ g }}</option> }
          </select>
          <button class="btn btn-p" style="margin-top:14px" [disabled]="!cityName.trim() || !cityGov || busy() || !canEdit()" (click)="addCity()">Add</button>
        </div>
        <div class="card panel">
          <div class="setup-toolbar"><h3 style="margin:0">Current Items</h3><input class="input srch" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="Search…"><span class="cnt">{{ citiesF().length }}/{{ cities().length }}</span></div>
          <table class="items">
            <thead><tr><th>City</th><th>Governorate</th><th>Real Name</th><th style="width:80px">Source</th><th class="ar">Actions</th></tr></thead>
            <tbody>
              @for (c of citiesF(); track c.id) {
                <tr>
                  <td>@if (editId() === c.id) { <input class="input" [(ngModel)]="editName"> } @else { <b>{{ c.name }}</b> }</td>
                  <td>
                    @if (editId() === c.id) {
                      <select class="select" [(ngModel)]="editGov"><option value="">—</option>@for (g of govOptions(); track g) { <option [value]="g">{{ g }}</option> }</select>
                    } @else { {{ c.governorate }} }
                  </td>
                  <td>@if (editId() === c.id) { <input class="input" [(ngModel)]="editRealName" placeholder="optional real name"> } @else { {{ c.realName || '—' }} }</td>
                  <td>@if (c.source === 'Oracle') { <span class="src-b src-o">Oracle</span> } @else { <span class="src-b src-m">Manual</span> }</td>
                  <td class="ar actions">
                    @if (canEdit()) {
                      @if (editId() === c.id) {
                        <button class="btn btn-mini btn-p" [disabled]="!editName.trim() || !editGov || busy()" (click)="saveCity(c)">Save</button>
                        <button class="btn btn-mini btn-s" (click)="cancelEdit()">Cancel</button>
                      } @else {
                        <button class="icon-btn" title="Edit" (click)="startEditCity(c)">✎</button>
                        <button class="icon-btn del" title="Delete" (click)="delCity(c)">🗑</button>
                      }
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="5" class="empty">No items yet.</td></tr> }
            </tbody>
          </table>
        </div>
      </div>
    }

    <!-- ===== Areas ===== -->
    @if (tab() === 'areas') {
      <div class="setup-grid">
        <div class="card panel">
          <h3>Create New Area</h3>
          <label class="lbl">Area Name</label>
          <input class="input" [(ngModel)]="areaName" placeholder="e.g. New Value" [disabled]="!canEdit()">
          <label class="lbl" style="margin-top:10px">City</label>
          <select class="select" [(ngModel)]="areaCity" [disabled]="!canEdit()">
            <option value="">—</option>@for (c of cities(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }
          </select>
          <label class="lbl" style="margin-top:10px">Area Manager</label>
          <app-filter-select [(ngModel)]="areaManager" [options]="managerOptions()" [clearable]="true" placeholder="—" searchPlaceholder="Search managers…" [disabled]="!canEdit()"></app-filter-select>
          <label class="lbl" style="margin-top:10px">Area Responsible</label>
          <app-filter-select [(ngModel)]="areaResponsible" [options]="responsibleOptions()" [clearable]="true" placeholder="—" searchPlaceholder="Search responsibles…" [disabled]="!canEdit()"></app-filter-select>
          <label class="chk" style="margin-top:10px"><input type="checkbox" [(ngModel)]="areaTransport" [disabled]="!canEdit()"> Transportation required</label>
          <button class="btn btn-p" style="margin-top:14px" [disabled]="!areaName.trim() || !areaCity || busy() || !canEdit()" (click)="addArea()">Add</button>
        </div>
        <div class="card panel">
          <div class="setup-toolbar"><h3 style="margin:0">Current Items</h3><input class="input srch" [ngModel]="q()" (ngModelChange)="q.set($event)" placeholder="Search…"><span class="cnt">{{ areasF().length }}/{{ areas().length }}</span></div>
          <table class="items">
            <thead><tr><th>Area</th><th>City</th><th>Real Name</th><th>Area Manager</th><th>Area Responsible</th><th>Transport</th><th style="width:80px">Source</th><th class="ar">Actions</th></tr></thead>
            <tbody>
              @for (a of areasF(); track a.id) {
                <tr>
                  <td>@if (editId() === a.id) { <input class="input" [(ngModel)]="editName"> } @else { <b>{{ a.name }}</b> }</td>
                  <td>
                    @if (editId() === a.id) {
                      <select class="select" [(ngModel)]="editCityId"><option value="">—</option>@for (c of cities(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select>
                    } @else { {{ cityName2(a.cityId) }} }
                  </td>
                  <td>@if (editId() === a.id) { <input class="input" [(ngModel)]="editRealName" placeholder="optional real name"> } @else { {{ a.realName || '—' }} }</td>
                  <td>@if (editId() === a.id) { <app-filter-select [(ngModel)]="editManager" [options]="managerOptions()" [clearable]="true" placeholder="—" searchPlaceholder="Search managers…"></app-filter-select> } @else { {{ repName(a.areaManagerId) }} }</td>
                  <td>@if (editId() === a.id) { <app-filter-select [(ngModel)]="editResponsible" [options]="responsibleOptions()" [clearable]="true" placeholder="—" searchPlaceholder="Search responsibles…"></app-filter-select> } @else { {{ repName(a.areaResponsibleId) }} }</td>
                  <td>
                    @if (editId() === a.id) { <input type="checkbox" [(ngModel)]="editTransport"> }
                    @else { {{ a.transportationRequired ? 'Yes' : 'No' }} }
                  </td>
                  <td>@if (a.source === 'Oracle') { <span class="src-b src-o">Oracle</span> } @else { <span class="src-b src-m">Manual</span> }</td>
                  <td class="ar actions">
                    @if (canEdit()) {
                      @if (editId() === a.id) {
                        <button class="btn btn-mini btn-p" [disabled]="!editName.trim() || !editCityId || busy()" (click)="saveArea(a)">Save</button>
                        <button class="btn btn-mini btn-s" (click)="cancelEdit()">Cancel</button>
                      } @else {
                        <button class="icon-btn" title="Edit" (click)="startEditArea(a)">✎</button>
                        <button class="icon-btn del" title="Delete" (click)="delArea(a)">🗑</button>
                      }
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="8" class="empty">No items yet.</td></tr> }
            </tbody>
          </table>
        </div>
      </div>
    }

    <!-- ===== Commissions & Loyalty ===== -->
    @if (tab() === 'compensation') {
      <div class="card panel" style="max-width:820px">
        <h3>Commissions &amp; Loyalty</h3>
        <div class="frm-grid" style="grid-template-columns:repeat(3,1fr);gap:12px">
          <div><label class="lbl">Commission rate (%)</label><input type="number" min="0" class="input" [(ngModel)]="comp.commissionRatePercent" [disabled]="!canEdit()"></div>
          <div><label class="lbl">Bonus threshold (%)</label><input type="number" min="0" class="input" [(ngModel)]="comp.bonusThresholdPercent" [disabled]="!canEdit()"></div>
          <div><label class="lbl">Bonus amount (EGP)</label><input type="number" min="0" class="input" [(ngModel)]="comp.bonusAmount" [disabled]="!canEdit()"></div>
        </div>

        <div style="display:flex;justify-content:space-between;align-items:center;margin:18px 0 8px">
          <h3 style="margin:0">Loyalty tiers</h3>
          @if (canEdit()) { <button class="btn btn-s btn-mini" (click)="addTier()">+ Add tier</button> }
        </div>
        <table class="items">
          <thead><tr><th>Tier</th><th>Min Achievement (%)</th><th>Points</th><th class="ar"></th></tr></thead>
          <tbody>
            @for (t of comp.tiers; track $index) {
              <tr>
                <td><input class="input" [(ngModel)]="t.name" [disabled]="!canEdit()"></td>
                <td><input type="number" min="0" class="input" [(ngModel)]="t.minAchievementPercent" [disabled]="!canEdit()"></td>
                <td><input type="number" min="0" class="input" [(ngModel)]="t.points" [disabled]="!canEdit()"></td>
                <td class="ar">@if (canEdit()) { <button class="icon-btn del" (click)="removeTier($index)">🗑</button> }</td>
              </tr>
            } @empty { <tr><td colspan="4" class="empty">No tiers configured.</td></tr> }
          </tbody>
        </table>
        @if (canEdit()) { <button class="btn btn-p" style="margin-top:16px" [disabled]="busy()" (click)="saveComp()">Save configuration</button> }
      </div>
    }
  `,
  styles: [`
    .tabbar { display:flex; gap:6px; flex-wrap:wrap; margin-bottom:16px }
    .tab { background:var(--white); border:1px solid var(--slate-300); color:var(--slate-700); border-radius:var(--r-btn); padding:8px 16px; font:600 12.5px var(--ui); cursor:pointer }
    .tab.on { background:var(--primary-blue); color:#fff; border-color:var(--primary-blue) }
    .setup-grid { display:grid; grid-template-columns:340px 1fr; gap:20px; align-items:start }
    @media (max-width:900px){ .setup-grid { grid-template-columns:1fr } }
    .panel { padding:20px }
    .panel h3 { margin:0 0 14px; font:700 15px var(--ui); color:var(--slate-800) }
    .lbl { display:block; font:600 11px var(--ui); color:var(--slate-600); margin-bottom:4px }
    .chk { display:flex; align-items:center; gap:8px; font:600 12px var(--ui); color:var(--slate-700) }
    .chk input { width:18px; height:18px }
    table.items { width:100%; border-collapse:collapse }
    table.items th { text-align:start; font:700 11px var(--ui); color:var(--slate-500); padding:10px 12px; border-bottom:2px solid var(--slate-150); background:var(--slate-50) }
    table.items td { padding:10px 12px; border-bottom:1px solid var(--slate-100); vertical-align:middle }
    table.items .ar { text-align:end }
    .actions { display:flex; gap:8px; justify-content:flex-end }
    .icon-btn { background:var(--white); border:1px solid var(--slate-300); border-radius:8px; width:32px; height:32px; cursor:pointer; font-size:14px }
    .icon-btn.del { color:#b91c1c; border-color:#fecaca; background:#fee2e2 }
    .empty { text-align:center; padding:24px; color:var(--slate-400) }
    .setup-toolbar { display:flex; align-items:center; gap:10px; margin-bottom:12px }
    .setup-toolbar .srch { flex:1; max-width:260px }
    .setup-toolbar .cnt { font-size:12px; color:var(--slate-500); margin-inline-start:auto }
    .src-b { font-size:11px; padding:2px 8px; border-radius:10px; font-weight:600 }
    .src-o { background:#eaf2fa; color:#2f7bd2 } .src-m { background:#eceff2; color:#6b7480 }
    .inline-banner { padding:10px 14px; border-radius:8px; margin-bottom:12px; font-size:13px }
  `],
})
export class SetupComponent {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);
  readonly tabs = TABS;

  readonly tab = signal<Tab>('governorates');
  readonly busy = signal(false);
  readonly refs = signal<RefItem[]>([]);
  readonly cities = signal<City[]>([]);
  readonly areas = signal<Area[]>([]);
  readonly govOptions = signal<string[]>([]);
  readonly q = signal('');
  readonly syncing = signal(false);
  readonly oracleTab = computed(() => ['governorates', 'labcategories', 'branches', 'cities', 'areas'].includes(this.tab()));
  readonly refsF = computed(() => this.filter(this.refs(), (r) => [r.nameEn, r.code]));
  readonly citiesF = computed(() => this.filter(this.cities(), (c) => [c.name, c.governorate]));
  readonly areasF = computed(() => this.filter(this.areas(), (a) => [a.name, this.cityName2(a.cityId)]));
  comp: CompConfig = { commissionRatePercent: 0, bonusThresholdPercent: 0, bonusAmount: 0, tiers: [] };

  private filter<T>(list: T[], fields: (x: T) => (string | undefined)[]): T[] {
    const term = this.q().trim().toLowerCase();
    if (!term) return list;
    return list.filter((x) => fields(x).some((f) => (f ?? '').toLowerCase().includes(term)));
  }
  sync(): void {
    this.syncing.set(true);
    this.api.post('/integration/sync-now').subscribe({
      next: () => { this.syncing.set(false); this.toast.success('Synced from Oracle.'); this.select(this.tab()); },
      error: () => { this.syncing.set(false); },
    });
  }

  // Reps for the area-role pickers (loaded with the Areas tab); each picker is bound to its matching rep type.
  readonly reps = signal<RepListItem[]>([]);
  readonly managerOptions = computed(() => this.reps().filter((r) => r.type === 'AreaManager').map((r) => ({ value: r.id, label: r.fullName })));
  readonly responsibleOptions = computed(() => this.reps().filter((r) => r.type === 'AreaResponsible').map((r) => ({ value: r.id, label: r.fullName })));
  repName(id: string | null): string { return id ? (this.reps().find((r) => r.id === id)?.fullName ?? '—') : '—'; }

  readonly editId = signal<string | null>(null);
  editName = ''; editGov = ''; editCityId = ''; editTransport = false; editRealName = '';
  editManager = ''; editResponsible = '';
  editFrom: number | null = null; editTo: number | null = null;   // segment income band (edit row)

  newName = '';
  newFrom: number | null = null; newTo: number | null = null;     // segment income band (create panel)
  cityName = ''; cityGov = '';
  areaName = ''; areaCity = ''; areaTransport = false; areaManager = ''; areaResponsible = '';

  readonly isRefTab = computed(() => this.tab() in REF_MAP);
  private type(): string { return REF_MAP[this.tab()] ?? ''; }
  singular(): string {
    const map: Partial<Record<Tab, string>> = { governorates: 'Governorate', labcategories: 'Lab Category', segments: 'Segment', branches: 'Branch', payers: 'Payer', contracts: 'Contract' };
    return map[this.tab()] ?? 'Item';
  }

  constructor() { this.select('governorates'); }

  canEdit(): boolean {
    switch (this.tab()) {
      case 'cities': return this.auth.has('SetupCities');
      case 'areas': return this.auth.has('SetupAreas');
      case 'compensation': return this.auth.has('SetupRefs') || this.auth.has('ManageUsers');
      default: return this.auth.has('SetupRefs');
    }
  }

  select(t: Tab): void {
    this.tab.set(t);
    this.q.set('');
    this.cancelEdit();
    if (t in REF_MAP) { this.reloadRefs(); return; }
    if (t === 'cities') { this.loadGovOptions(); this.reloadCities(); }
    if (t === 'areas') { this.reloadCities(); this.reloadAreas(); this.reloadReps(); }
    if (t === 'compensation') { this.loadComp(); }
  }

  cityName2(id: string): string { return this.cities().find((c) => c.id === id)?.name ?? '—'; }

  private reloadRefs(): void { this.api.get<RefItem[]>('/setup/refs', { type: this.type() }).subscribe({ next: (r) => this.refs.set(r) }); }
  private reloadCities(): void { this.api.get<City[]>('/setup/cities').subscribe({ next: (r) => this.cities.set(r) }); }
  private reloadAreas(): void { this.api.get<Area[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r) }); }
  private reloadReps(): void { this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) }); }
  private loadGovOptions(): void { this.api.get<RefItem[]>('/setup/refs', { type: 'Governorate' }).subscribe({ next: (r) => this.govOptions.set(r.map((x) => x.nameEn)) }); }
  private loadComp(): void { this.api.get<CompConfig | null>('/setup/compensation-config').subscribe({ next: (c) => { if (c) this.comp = { ...c, tiers: c.tiers ?? [] }; } }); }

  private run(obs: { subscribe: Function }, onOk: () => void): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({ next: () => { this.busy.set(false); onOk(); }, error: () => this.busy.set(false) });
  }

  startEdit(id: string, name: string, realName: string | null = null, from: number | null = null, to: number | null = null): void {
    this.editId.set(id); this.editName = name; this.editRealName = realName ?? ''; this.editFrom = from; this.editTo = to;
  }
  startEditCity(c: City): void { this.editId.set(c.id); this.editName = c.name; this.editGov = c.governorate; this.editRealName = c.realName ?? ''; }
  startEditArea(a: Area): void { this.editId.set(a.id); this.editName = a.name; this.editCityId = a.cityId; this.editTransport = a.transportationRequired; this.editRealName = a.realName ?? ''; this.editManager = a.areaManagerId ?? ''; this.editResponsible = a.areaResponsibleId ?? ''; }
  cancelEdit(): void { this.editId.set(null); this.editName = ''; this.editGov = ''; this.editCityId = ''; this.editTransport = false; this.editRealName = ''; this.editManager = ''; this.editResponsible = ''; this.editFrom = null; this.editTo = null; }

  // Reference items (single Name → code + nameEn)
  private numOrNull(v: number | null): number | null {
    return (v === null || v === undefined || (v as unknown) === '' || Number.isNaN(v as number)) ? null : Number(v);
  }
  addRef(): void {
    const name = this.newName.trim();
    const body: Record<string, unknown> = { type: this.type(), code: name, nameEn: name, nameAr: null, sortOrder: 0 };
    if (this.tab() === 'segments') { body['targetIncomeFrom'] = this.numOrNull(this.newFrom); body['targetIncomeTo'] = this.numOrNull(this.newTo); }
    this.run(this.api.post('/setup/refs', body), () => { this.newName = ''; this.newFrom = null; this.newTo = null; this.reloadRefs(); });
  }
  saveRef(r: RefItem): void {
    const body: Record<string, unknown> = { name: this.editName.trim(), realName: this.editRealName.trim() || null };
    if (this.tab() === 'segments') { body['targetIncomeFrom'] = this.numOrNull(this.editFrom); body['targetIncomeTo'] = this.numOrNull(this.editTo); }
    this.run(this.api.put(`/setup/refs/${r.id}`, body), () => { this.cancelEdit(); this.reloadRefs(); });
  }
  delRef(r: RefItem): void { if (confirm(`Delete "${r.nameEn}"?`)) this.run(this.api.delete(`/setup/refs/${r.id}`), () => this.reloadRefs()); }

  assignSegments(): void {
    this.busy.set(true);
    this.api.post<{ ran: boolean; status: string; month: string; labsEvaluated: number; labsReassigned: number; perSegment: Record<string, number> }>('/setup/segments/assign', {}).subscribe({
      next: (r) => {
        this.busy.set(false);
        if (!r.ran) { this.toast.warning(`Assignment skipped (${r.status}). Configure segment income bands first.`); return; }
        const dist = Object.entries(r.perSegment).sort(([a], [b]) => a.localeCompare(b)).map(([k, v]) => `${k}: ${v}`).join(', ');
        this.toast.success(`Segments assigned for ${r.month}: ${r.labsReassigned} of ${r.labsEvaluated} labs changed (${dist}).`);
      },
      error: () => this.busy.set(false),
    });
  }

  // Cities
  addCity(): void { this.run(this.api.post('/setup/cities', { name: this.cityName.trim(), governorate: this.cityGov }), () => { this.cityName = ''; this.cityGov = ''; this.reloadCities(); }); }
  saveCity(c: City): void { this.run(this.api.put(`/setup/cities/${c.id}`, { name: this.editName.trim(), governorate: this.editGov, realName: this.editRealName.trim() || null }), () => { this.cancelEdit(); this.reloadCities(); }); }
  delCity(c: City): void { if (confirm(`Delete "${c.name}"?`)) this.run(this.api.delete(`/setup/cities/${c.id}`), () => this.reloadCities()); }

  // Areas
  addArea(): void {
    this.run(this.api.post('/setup/areas', {
      name: this.areaName.trim(), cityId: this.areaCity, transportationRequired: this.areaTransport, transferReps: [],
      areaManagerId: this.areaManager || null, areaResponsibleId: this.areaResponsible || null,
    }), () => { this.areaName = ''; this.areaCity = ''; this.areaTransport = false; this.areaManager = ''; this.areaResponsible = ''; this.reloadAreas(); });
  }
  saveArea(a: Area): void {
    this.run(this.api.put(`/setup/areas/${a.id}`, {
      name: this.editName.trim(), cityId: this.editCityId, transportationRequired: this.editTransport, realName: this.editRealName.trim() || null,
      areaManagerId: this.editManager || null, areaResponsibleId: this.editResponsible || null,
    }), () => { this.cancelEdit(); this.reloadAreas(); });
  }
  delArea(a: Area): void { if (confirm(`Delete "${a.name}"?`)) this.run(this.api.delete(`/setup/areas/${a.id}`), () => this.reloadAreas()); }

  // Compensation
  addTier(): void { this.comp.tiers = [...this.comp.tiers, { name: '', minAchievementPercent: 0, points: 0 }]; }
  removeTier(i: number): void { this.comp.tiers = this.comp.tiers.filter((_, idx) => idx !== i); }
  saveComp(): void {
    this.run(this.api.post('/setup/compensation-config', {
      commissionRatePercent: this.comp.commissionRatePercent,
      bonusThresholdPercent: this.comp.bonusThresholdPercent,
      bonusAmount: this.comp.bonusAmount,
      tiers: this.comp.tiers,
    }), () => this.toast.success('Configuration saved.'));
  }
}
