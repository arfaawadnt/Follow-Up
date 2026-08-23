import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';

interface RefItem { id: string; type: string; code: string; nameEn: string; nameAr: string | null; sortOrder: number; }
interface City { id: string; name: string; governorate: string; }
interface Area { id: string; name: string; cityId: string; transportationRequired: boolean; transferReps: string[]; }
type Tab = 'refs' | 'cities' | 'areas';

@Component({
  selector: 'app-setup',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'setup' | t : 'Reference data' }}</div><h1>{{ 'setup' | t : 'Reference data' }}</h1></div></div>
    @if (banner()) { <div class="inline-banner inline-banner-error">{{ banner() }}</div> }

    <div class="tabs" style="display:flex;gap:6px;margin-bottom:14px">
      <button class="tab" [class.on]="tab()==='refs'" (click)="tab.set('refs')">{{ 'reference_data' | t : 'Reference data' }}</button>
      <button class="tab" [class.on]="tab()==='cities'" (click)="setTab('cities')">{{ 'city_2' | t : 'Cities' }}</button>
      <button class="tab" [class.on]="tab()==='areas'" (click)="setTab('areas')">{{ 'area_2' | t : 'Areas' }}</button>
    </div>

    @if (tab() === 'refs') {
      <div class="card" style="padding:16px;margin-bottom:16px">
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:end">
          <div class="field"><label>Type</label><select class="select" [(ngModel)]="refType"><option value="">—</option>@for (t of refTypeOptions; track t) { <option [value]="t">{{ t }}</option> }</select></div>
          <div class="field"><label>Code</label><input class="input" [(ngModel)]="refCode"></div>
          <div class="field"><label>Name</label><input class="input" [(ngModel)]="refName"></div>
          @if (auth.has('SetupRefs')) { <button class="btn btn-p" [disabled]="!refType||!refCode||!refName||busy()" (click)="addRef()">Add</button> }
        </div>
      </div>
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>Type</th><th>Code</th><th>Name</th><th></th></tr></thead>
        <tbody>@for (r of refs(); track r.id) { <tr><td class="mono small">{{ r.type }}</td><td class="mono">{{ r.code }}</td><td>{{ r.nameEn }}</td><td class="actions">@if (auth.has('SetupRefs')) { <button class="btn btn-mini btn-d" (click)="delRef(r)">Delete</button> }</td></tr> } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:24px">—</td></tr> }</tbody>
      </table></div>
    }

    @if (tab() === 'cities') {
      <div class="card" style="padding:16px;margin-bottom:16px"><div style="display:flex;gap:8px;align-items:end">
        <div class="field"><label>Name</label><input class="input" [(ngModel)]="cityName"></div>
        <div class="field"><label>Governorate</label><input class="input" [(ngModel)]="cityGov"></div>
        @if (auth.has('SetupCities')) { <button class="btn btn-p" [disabled]="!cityName||!cityGov||busy()" (click)="addCity()">Add</button> }
      </div></div>
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>City</th><th>Governorate</th><th></th></tr></thead>
        <tbody>@for (c of cities(); track c.id) { <tr><td>{{ c.name }}</td><td>{{ c.governorate }}</td><td class="actions">@if (auth.has('SetupCities')) { <button class="btn btn-mini btn-d" (click)="delCity(c)">Delete</button> }</td></tr> } @empty { <tr><td colspan="3" class="empty" style="text-align:center;padding:24px">—</td></tr> }</tbody>
      </table></div>
    }

    @if (tab() === 'areas') {
      <div class="card" style="padding:16px;margin-bottom:16px"><div style="display:flex;gap:8px;align-items:end;flex-wrap:wrap">
        <div class="field"><label>Name</label><input class="input" [(ngModel)]="areaName"></div>
        <div class="field"><label>City</label><select class="select" [(ngModel)]="areaCity"><option value="">—</option>@for (c of cities(); track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select></div>
        <div class="field"><label>Transport</label><input type="checkbox" [(ngModel)]="areaTransport" style="width:18px;height:18px"></div>
        @if (auth.has('SetupAreas')) { <button class="btn btn-p" [disabled]="!areaName||!areaCity||busy()" (click)="addArea()">Add</button> }
      </div></div>
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>Area</th><th>City</th><th>Transport</th><th></th></tr></thead>
        <tbody>@for (a of areas(); track a.id) { <tr><td>{{ a.name }}</td><td>{{ cityName2(a.cityId) }}</td><td>{{ a.transportationRequired ? 'Yes' : 'No' }}</td><td class="actions">@if (auth.has('SetupAreas')) { <button class="btn btn-mini btn-d" (click)="delArea(a)">Delete</button> }</td></tr> } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:24px">—</td></tr> }</tbody>
      </table></div>
    }
  `,
  styles: [`.tab{background:var(--white);border:1px solid var(--slate-300);color:var(--slate-700);border-radius:var(--r-btn);padding:7px 16px;font:600 12.5px var(--ui);cursor:pointer}.tab.on{background:var(--primary-blue);color:#fff;border-color:var(--primary-blue)}.field label{display:block;font:600 11px var(--ui);color:var(--slate-600);margin-bottom:4px}.actions{display:flex;gap:6px}.btn-d{background:#fee2e2;color:#991b1b;border:1px solid #fecaca}`],
})
export class SetupComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly tab = signal<Tab>('refs');
  readonly refs = signal<RefItem[]>([]);
  readonly cities = signal<City[]>([]);
  readonly areas = signal<Area[]>([]);
  readonly banner = signal<string | null>(null);
  // The fixed RefType names the backend accepts (Enumeration.FromName<RefType>); free text produced 400s.
  readonly refTypeOptions = ['Governorate', 'Branch', 'MarketingPurpose', 'ComplaintCategory', 'Team', 'Channel', 'Payer', 'ContractType', 'LabCategory'];
  refType = ''; refCode = ''; refName = '';
  cityName = ''; cityGov = '';
  areaName = ''; areaCity = ''; areaTransport = false;

  readonly refTypes = computed(() => [...new Set(this.refs().map((r) => r.type))].sort());

  constructor() { this.api.get<RefItem[]>('/setup/refs').subscribe({ next: (r) => this.refs.set(r) }); }
  setTab(t: Tab): void {
    this.tab.set(t);
    if ((t === 'cities' || t === 'areas') && !this.cities().length) this.api.get<City[]>('/setup/cities').subscribe({ next: (r) => this.cities.set(r) });
    if (t === 'areas' && !this.areas().length) this.api.get<Area[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r) });
  }
  cityName2(id: string): string { return this.cities().find((c) => c.id === id)?.name ?? '—'; }

  private run(obs: { subscribe: Function }, onOk: () => void): void {
    this.busy.set(true); this.banner.set(null);
    (obs as { subscribe: Function }).subscribe({ next: () => { this.busy.set(false); onOk(); }, error: (e: { error?: { detail?: string } }) => { this.busy.set(false); this.banner.set(e?.error?.detail ?? 'Operation failed.'); } });
  }
  reloadRefs(): void { this.api.get<RefItem[]>('/setup/refs').subscribe({ next: (r) => this.refs.set(r) }); }
  reloadCities(): void { this.api.get<City[]>('/setup/cities').subscribe({ next: (r) => this.cities.set(r) }); }
  reloadAreas(): void { this.api.get<Area[]>('/setup/areas').subscribe({ next: (r) => this.areas.set(r) }); }

  addRef(): void { this.run(this.api.post('/setup/refs', { type: this.refType, code: this.refCode, nameEn: this.refName, nameAr: null, sortOrder: 0 }), () => { this.refCode = ''; this.refName = ''; this.reloadRefs(); }); }
  delRef(r: RefItem): void { this.run(this.api.delete(`/setup/refs/${r.id}`), () => this.reloadRefs()); }
  addCity(): void { this.run(this.api.post('/setup/cities', { name: this.cityName, governorate: this.cityGov }), () => { this.cityName = ''; this.cityGov = ''; this.reloadCities(); }); }
  delCity(c: City): void { this.run(this.api.delete(`/setup/cities/${c.id}`), () => this.reloadCities()); }
  addArea(): void { this.run(this.api.post('/setup/areas', { name: this.areaName, cityId: this.areaCity, transportationRequired: this.areaTransport, transferReps: [] }), () => { this.areaName = ''; this.areaCity = ''; this.areaTransport = false; this.reloadAreas(); }); }
  delArea(a: Area): void { this.run(this.api.delete(`/setup/areas/${a.id}`), () => this.reloadAreas()); }
}
