import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';

interface RefItem { id: string; type: string; code: string; nameEn: string; nameAr: string | null; sortOrder: number; }
interface City { id: string; name: string; governorate: string; }
interface Area { id: string; name: string; cityId: string; transportationRequired: boolean; transferReps: string[]; }
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
  imports: [FormsModule],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / Setup & Configuration</div><h1>Setup &amp; Configuration</h1></div></div>

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
          <button class="btn btn-p" style="margin-top:14px" [disabled]="!newName.trim() || busy() || !canEdit()" (click)="addRef()">Add</button>
        </div>
        <div class="card panel">
          <h3>Current Items</h3>
          <table class="items">
            <thead><tr><th>{{ singular().toUpperCase() }}</th><th class="ar">ACTIONS</th></tr></thead>
            <tbody>
              @for (r of refs(); track r.id) {
                <tr>
                  <td>
                    @if (editId() === r.id) { <input class="input" [(ngModel)]="editName"> }
                    @else { <b>{{ r.nameEn }}</b> }
                  </td>
                  <td class="ar actions">
                    @if (canEdit()) {
                      @if (editId() === r.id) {
                        <button class="btn btn-mini btn-p" [disabled]="!editName.trim() || busy()" (click)="saveRef(r)">Save</button>
                        <button class="btn btn-mini btn-s" (click)="cancelEdit()">Cancel</button>
                      } @else {
                        <button class="icon-btn" title="Edit" (click)="startEdit(r.id, r.nameEn)">✎</button>
                        <button class="icon-btn del" title="Delete" (click)="delRef(r)">🗑</button>
                      }
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="2" class="empty">No items yet.</td></tr> }
            </tbody>
          </table>
        </div>
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
          <h3>Current Items</h3>
          <table class="items">
            <thead><tr><th>CITY</th><th>GOVERNORATE</th><th class="ar">ACTIONS</th></tr></thead>
            <tbody>
              @for (c of cities(); track c.id) {
                <tr>
                  <td>@if (editId() === c.id) { <input class="input" [(ngModel)]="editName"> } @else { <b>{{ c.name }}</b> }</td>
                  <td>
                    @if (editId() === c.id) {
                      <select class="select" [(ngModel)]="editGov"><option value="">—</option>@for (g of govOptions(); track g) { <option [value]="g">{{ g }}</option> }</select>
                    } @else { {{ c.governorate }} }
                  </td>
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
              } @empty { <tr><td colspan="3" class="empty">No items yet.</td></tr> }
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
          <label class="chk" style="margin-top:10px"><input type="checkbox" [(ngModel)]="areaTransport" [disabled]="!canEdit()"> Transportation required</label>
          <button class="btn btn-p" style="margin-top:14px" [disabled]="!areaName.trim() || !areaCity || busy() || !canEdit()" (click)="addArea()">Add</button>
        </div>
        <div class="card panel">
          <h3>Current Items</h3>
          <table class="items">
            <thead><tr><th>AREA</th><th>CITY</th><th>TRANSPORT</th><th class="ar">ACTIONS</th></tr></thead>
            <tbody>
              @for (a of areas(); track a.id) {
                <tr>
                  <td>@if (editId() === a.id) { <input class="input" [(ngModel)]="editName"> } @else { <b>{{ a.name }}</b> }</td>
                  <td>
                    @if (editId() === a.id) {
                      <select class="select" [(ngModel)]="editCityId"><option value="">—</option>@for (c of cities(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select>
                    } @else { {{ cityName2(a.cityId) }} }
                  </td>
                  <td>
                    @if (editId() === a.id) { <input type="checkbox" [(ngModel)]="editTransport"> }
                    @else { {{ a.transportationRequired ? 'Yes' : 'No' }} }
                  </td>
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
              } @empty { <tr><td colspan="4" class="empty">No items yet.</td></tr> }
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
          <thead><tr><th>TIER</th><th>MIN ACHIEVEMENT (%)</th><th>POINTS</th><th class="ar"></th></tr></thead>
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
  comp: CompConfig = { commissionRatePercent: 0, bonusThresholdPercent: 0, bonusAmount: 0, tiers: [] };

  readonly editId = signal<string | null>(null);
  editName = ''; editGov = ''; editCityId = ''; editTransport = false;

  newName = '';
  cityName = ''; cityGov = '';
  areaName = ''; areaCity = ''; areaTransport = false;

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
    this.cancelEdit();
    if (t in REF_MAP) { this.reloadRefs(); return; }
    if (t === 'cities') { this.loadGovOptions(); this.reloadCities(); }
    if (t === 'areas') { this.reloadCities(); this.reloadAreas(); }
    if (t === 'compensation') { this.loadComp(); }
  }

  cityName2(id: string): string { return this.cities().find((c) => c.id === id)?.name ?? '—'; }

  private reloadRefs(): void { this.api.get<RefItem[]>('/setup/refs', { type: this.type() }).subscribe({ next: (r) => this.refs.set(r) }); }
  private reloadCities(): void { this.api.get<City[]>('/setup/cities').subscribe({ next: (r) => this.cities.set(r) }); }
  private reloadAreas(): void { this.api.get<Area[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r) }); }
  private loadGovOptions(): void { this.api.get<RefItem[]>('/setup/refs', { type: 'Governorate' }).subscribe({ next: (r) => this.govOptions.set(r.map((x) => x.nameEn)) }); }
  private loadComp(): void { this.api.get<CompConfig | null>('/setup/compensation-config').subscribe({ next: (c) => { if (c) this.comp = { ...c, tiers: c.tiers ?? [] }; } }); }

  private run(obs: { subscribe: Function }, onOk: () => void): void {
    this.busy.set(true);
    (obs as { subscribe: Function }).subscribe({ next: () => { this.busy.set(false); onOk(); }, error: () => this.busy.set(false) });
  }

  startEdit(id: string, name: string): void { this.editId.set(id); this.editName = name; }
  startEditCity(c: City): void { this.editId.set(c.id); this.editName = c.name; this.editGov = c.governorate; }
  startEditArea(a: Area): void { this.editId.set(a.id); this.editName = a.name; this.editCityId = a.cityId; this.editTransport = a.transportationRequired; }
  cancelEdit(): void { this.editId.set(null); this.editName = ''; this.editGov = ''; this.editCityId = ''; this.editTransport = false; }

  // Reference items (single Name → code + nameEn)
  addRef(): void {
    const name = this.newName.trim();
    this.run(this.api.post('/setup/refs', { type: this.type(), code: name, nameEn: name, nameAr: null, sortOrder: 0 }),
      () => { this.newName = ''; this.reloadRefs(); });
  }
  saveRef(r: RefItem): void { this.run(this.api.put(`/setup/refs/${r.id}`, { name: this.editName.trim() }), () => { this.cancelEdit(); this.reloadRefs(); }); }
  delRef(r: RefItem): void { if (confirm(`Delete "${r.nameEn}"?`)) this.run(this.api.delete(`/setup/refs/${r.id}`), () => this.reloadRefs()); }

  // Cities
  addCity(): void { this.run(this.api.post('/setup/cities', { name: this.cityName.trim(), governorate: this.cityGov }), () => { this.cityName = ''; this.cityGov = ''; this.reloadCities(); }); }
  saveCity(c: City): void { this.run(this.api.put(`/setup/cities/${c.id}`, { name: this.editName.trim(), governorate: this.editGov }), () => { this.cancelEdit(); this.reloadCities(); }); }
  delCity(c: City): void { if (confirm(`Delete "${c.name}"?`)) this.run(this.api.delete(`/setup/cities/${c.id}`), () => this.reloadCities()); }

  // Areas
  addArea(): void { this.run(this.api.post('/setup/areas', { name: this.areaName.trim(), cityId: this.areaCity, transportationRequired: this.areaTransport, transferReps: [] }), () => { this.areaName = ''; this.areaCity = ''; this.areaTransport = false; this.reloadAreas(); }); }
  saveArea(a: Area): void { this.run(this.api.put(`/setup/areas/${a.id}`, { name: this.editName.trim(), cityId: this.editCityId, transportationRequired: this.editTransport }), () => { this.cancelEdit(); this.reloadAreas(); }); }
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
