import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';
import { LabDetail, RepListItem } from '../../core/models';
import { MapComponent } from '../../shared/map.component';
import { ToastService } from '../../core/toast.service';

interface Ref { nameEn: string; }
interface City { id: string; name: string; governorate: string; }
interface Area { id: string; name: string; cityId: string; }
interface Contact { name: string; phone: string; birthday: string | null; }

const STATUSES = ['Scanned', 'Interactive', 'Active', 'Inactive', 'Stopped', 'Pending', 'Suspended', 'Churned'];
const CHANNELS = ['WhatsApp', 'Phone Call', 'Email', 'In-person'];
const DAYS = ['Sat', 'Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri'];
const DAY_NAMES: Record<string, string> = { Sat: 'Saturday', Sun: 'Sunday', Mon: 'Monday', Tue: 'Tuesday', Wed: 'Wednesday', Thu: 'Thursday', Fri: 'Friday' };
const SHORT_DAYS: Record<string, string> = { Saturday: 'Sat', Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed', Thursday: 'Thu', Friday: 'Fri' };

/** Parses "lat, lng" free text; null when the text is not a valid coordinate pair. */
function parseGeo(text: string): { lat: number; lng: number } | null {
  const m = text.trim().match(/^(-?\d+(?:\.\d+)?)\s*[,\s]\s*(-?\d+(?:\.\d+)?)$/);
  if (!m) return null;
  const lat = +m[1], lng = +m[2];
  return Math.abs(lat) <= 90 && Math.abs(lng) <= 180 ? { lat, lng } : null;
}

@Component({
  selector: 'app-lab-detail',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, MapComponent, DateInputComponent],
  template: `
    @if (loading()) { <div class="card sect">{{ 'loading' | t : 'Loading…' }}</div> }
    @if (lab(); as l) {
      <div class="pagehead"><div>
        <div class="breadcrumbs">Home / <a routerLink="/labs" class="crumb">{{ 'lab_mgmt' | t : 'Laboratories' }}</a> / {{ l.displayCode }}</div>
        <h1>{{ 'edit_laboratory' | t : 'Edit Laboratory' }} — {{ l.name }}</h1>
      </div></div>

      <section class="card sect"><h3>{{ 'identity' | t : 'Identity' }}</h3>
        <div class="grid4">
          <div class="field"><label>{{ 'lab_name_lbl' | t : 'Lab name *' }}</label><input class="input" [(ngModel)]="f.name"></div>
          <div class="field"><label>{{ 'lab_code_lbl' | t : 'Lab code *' }}</label><input class="input" [ngModel]="f.code" disabled></div>
          <div class="field"><label>{{ 'segment' | t : 'Segment' }}</label><select class="select" [(ngModel)]="f.segment">@for (s of segments(); track s) { <option [value]="s">{{ s }}</option> }</select></div>
          <div class="field"><label>{{ 'status' | t : 'Status' }}</label><select class="select" [(ngModel)]="f.status">@for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }</select></div>
          <div class="field"><label>{{ 'lab_category' | t : 'Lab Category' }}</label><select class="select" [(ngModel)]="f.category"><option value="">—</option>@for (c of categories(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
          <div class="field"><label>{{ 'mapping_code' | t : 'Mapping Code' }}</label><input class="input" [(ngModel)]="f.mappingCode"></div>
          <div class="field"><label>{{ 'encrypted' | t : 'Encrypted' }}</label><label class="chip"><input type="checkbox" [(ngModel)]="f.isEncrypted"> {{ 'encrypted' | t : 'Encrypted' }}</label></div>
          <div class="field"><label>{{ 'serving_branch' | t : 'Serving branch' }}</label><select class="select" [(ngModel)]="f.branch"><option value="">—</option>@for (b of branches(); track b) { <option [value]="b">{{ b }}</option> }</select></div>
          <div class="field"><label>{{ 'license_no' | t : 'License no.' }}</label><input class="input" [(ngModel)]="f.licenseNo"></div>
          <div class="field"><label>{{ 'license_date' | t : 'License date' }}</label><app-date-input [(ngModel)]="f.licenseDate"></app-date-input></div>
          <div class="field"><label>{{ 'avg_monthly_samples' | t : 'Avg monthly samples' }}</label><input type="number" min="0" class="input" [(ngModel)]="f.avgMonthlySamples"></div>
        </div>
      </section>

      <section class="card sect"><h3>{{ 'location' | t : 'Location' }}</h3>
        <div class="grid4">
          <div class="field"><label>{{ 'governorate_lbl' | t : 'Governorate *' }}</label><select class="select" [ngModel]="f.governorate" (ngModelChange)="onGovChange($event)"><option value="">—</option>@for (g of governorates(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
          <div class="field"><label>{{ 'city_lbl' | t : 'City *' }}</label><select class="select" [ngModel]="f.city" (ngModelChange)="onCityChange($event)"><option value="">—</option>@for (c of filteredCities(); track c.id) { <option [value]="c.name">{{ c.name }}</option> }</select></div>
          <div class="field"><label>{{ 'area' | t : 'Area' }}</label><select class="select" [(ngModel)]="f.area"><option value="">—</option>@for (a of filteredAreas(); track a.id) { <option [value]="a.name">{{ a.name }}</option> }</select></div>
          @if (canViewLocation()) { <div class="field"><label>{{ 'geo_coords' | t : 'Geo (lat, lng)' }}</label><input class="input" [(ngModel)]="f.geo" placeholder="30.0444, 31.2357"></div> }
          <div class="field" style="grid-column:1/-1"><label>{{ 'address' | t : 'Address' }}</label><input class="input" [(ngModel)]="f.address" placeholder="street, building, floor…"></div>
          @if (canViewLocation()) {
            <div class="field" style="grid-column:1/-1"><label>{{ 'search_location_on_map' | t : 'Search location on map' }}</label>
              <div class="georow">
                <input class="input" [(ngModel)]="mapQuery" (keyup.enter)="searchLocation()" [placeholder]="'search_by_name_or_paste_google_maps_link' | t : 'Search by name, or paste Google Maps link, or coordinates...'">
                <button type="button" class="btn btn-s" [disabled]="!mapQuery.trim() || searching()" (click)="searchLocation()">{{ searching() ? ('searching' | t : 'Searching...') : ('search_2' | t : 'Search') }}</button>
              </div>
              @if (geoMiss()) { <div class="geo-msg">{{ 'sorry_no_matching_locations_found' | t : 'Sorry, no matching locations found.' }}</div> }
              @if (geoFail()) { <div class="geo-msg">{{ 'location_search_unavailable' | t : 'Location search is unavailable right now.' }}</div> }
            </div>
          }
        </div>
        @if (canViewLocation()) {
          <div style="margin-top:10px">
            <app-map [lat]="geoLat()" [lng]="geoLng()" [editable]="true" [height]="260" (coordChange)="onPick($event)" />
          </div>
        }
      </section>

      <section class="card sect"><h3>{{ 'commercial_assignment' | t : 'Commercial & Assignment' }}</h3>
        <div class="grid4">
          <div class="field"><label>{{ 'payer_type' | t : 'Payer type' }}</label><select class="select" [(ngModel)]="f.payer"><option value="">—</option>@for (p of payers(); track p) { <option [value]="p">{{ p }}</option> }</select></div>
          <div class="field"><label>{{ 'contract' | t : 'Contract' }}</label><select class="select" [(ngModel)]="f.contractType"><option value="">—</option>@for (c of contracts(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
          <div class="field"><label>{{ 'marketing_rep' | t : 'Marketing rep' }}</label><select class="select" [(ngModel)]="f.marketingRepId"><option value="">—</option>@for (r of marketingReps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
          <div class="field"><label>{{ 'preferred_channel' | t : 'Preferred channel' }}</label><select class="select" [(ngModel)]="f.preferredChannel"><option value="">—</option>@for (c of channels; track c) { <option [value]="c">{{ c }}</option> }</select></div>
        </div>
        <div class="field" style="margin-top:10px"><label>{{ 'collection_rep' | t : 'Collection rep' }}</label>
          <div class="tags">
            @for (r of selectedCollectors(); track r.id) { <span class="tag">{{ r.fullName }}<button type="button" class="tag-x" (click)="removeCollector(r.id)" aria-label="Remove">×</button></span> }
            <select class="select tag-add" #addSel (change)="addCollector(addSel.value); addSel.value = ''">
              <option value="">{{ 'select_collectors' | t : 'Select collectors...' }}</option>
              @for (r of availableCollectors(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }
            </select>
          </div>
        </div>
      </section>

      <section class="card sect"><h3>{{ 'collection_schedule' | t : 'Collection Schedule' }}</h3>
        <div class="grid4">
          <div class="field"><label>{{ 'visit_time1_lbl' | t : 'Visit time 1 *' }}</label><input type="time" class="input" [(ngModel)]="f.time1"></div>
          <div class="field"><label>{{ 'visit_time2_lbl' | t : 'Visit time 2 (optional)' }}</label><input type="time" class="input" [(ngModel)]="f.time2"></div>
        </div>
        <div class="field" style="margin-top:10px"><label>{{ 'working_days_lbl' | t : 'Working days *' }}</label>
          <div class="chips">@for (d of days; track d) { <button type="button" class="chip" [class.on]="workDays.includes(d)" (click)="toggleDay(d)">{{ d }}</button> }</div>
        </div>
      </section>

      <section class="card sect"><h3>{{ 'contacts_managers' | t : 'Contacts — Managers' }}</h3>
        @for (c of managers; track $index) {
          <div class="grid4 crow"><input class="input" [placeholder]="'name' | t : 'Name'" [(ngModel)]="c.name"><input class="input" [placeholder]="'phone' | t : 'Phone'" [(ngModel)]="c.phone"><app-date-input [(ngModel)]="c.birthday"></app-date-input><button type="button" class="btn btn-s btn-mini" (click)="managers.splice($index, 1)">{{ 'remove' | t : 'Remove' }}</button></div>
        }
        <button type="button" class="btn btn-s btn-mini" (click)="managers.push({ name: '', phone: '', birthday: null })">{{ 'add_manager' | t : '+ Add manager' }}</button>
      </section>

      <section class="card sect"><h3>{{ 'contacts_receptionists' | t : 'Contacts — Receptionists' }}</h3>
        @for (c of receptionists; track $index) {
          <div class="grid4 crow"><input class="input" [placeholder]="'name' | t : 'Name'" [(ngModel)]="c.name"><input class="input" [placeholder]="'phone' | t : 'Phone'" [(ngModel)]="c.phone"><app-date-input [(ngModel)]="c.birthday"></app-date-input><button type="button" class="btn btn-s btn-mini" (click)="receptionists.splice($index, 1)">{{ 'remove' | t : 'Remove' }}</button></div>
        }
        <button type="button" class="btn btn-s btn-mini" (click)="receptionists.push({ name: '', phone: '', birthday: null })">{{ 'add_receptionist' | t : '+ Add receptionist' }}</button>
      </section>

      <section class="card sect"><h3>{{ 'attached_laboratory_images' | t : 'Attached Laboratory Images' }}</h3>
        <div class="field"><label>{{ 'attach_images_multiple_files_supported' | t : 'Attach images (multiple files supported)' }}</label>
          <input type="file" multiple accept="image/*" (change)="onFiles($event)" [disabled]="busy()">
        </div>
        @if (images().length) {
          <div class="thumbs">
            @for (img of images(); track $index) {
              <div class="thumb"><img [src]="img" alt=""><button type="button" class="thumb-x" (click)="removeImage($index)" aria-label="Remove image">×</button></div>
            }
          </div>
        }
      </section>

      <div class="foot-actions">
        <button type="button" class="btn btn-p" [disabled]="!canSave() || busy() || !auth.has('UpdateLabs')" (click)="save()">{{ 'save_changes' | t : 'Save Changes' }}</button>
        <button type="button" class="btn btn-s" (click)="cancel()">{{ 'cancel' | t : 'Cancel' }}</button>
      </div>
    }
  `,
  styles: [`
    .crumb { color:inherit; text-decoration:none } .crumb:hover { text-decoration:underline }
    .sect { padding:20px; margin-bottom:16px }
    .sect h3 { margin:0 0 14px; font:700 15px var(--ui); color:var(--slate-800) }
    .grid4 { display:grid; grid-template-columns:repeat(4,1fr); gap:12px }
    @media (max-width:900px){ .grid4 { grid-template-columns:1fr 1fr } }
    .crow { align-items:center; margin-bottom:8px }
    .field label { display:block; font:600 11px var(--ui); color:var(--slate-600); margin-bottom:4px }
    .chips { display:flex; flex-wrap:wrap; gap:10px }
    .chip { display:flex; align-items:center; gap:6px; font:600 12px var(--ui); color:var(--slate-700); border:1px solid var(--slate-300); border-radius:8px; padding:6px 10px; cursor:pointer; background:var(--white) }
    .chip.on { background:var(--primary-blue); border-color:var(--primary-blue); color:#fff }
    .tags { display:flex; flex-wrap:wrap; gap:8px; align-items:center }
    .tag { display:inline-flex; align-items:center; gap:6px; background:var(--slate-100); border:1px solid var(--slate-300); border-radius:999px; padding:4px 10px; font:600 12px var(--ui); color:var(--slate-700) }
    .tag-x { border:0; background:none; cursor:pointer; font-size:14px; line-height:1; color:var(--slate-500); padding:0 }
    .tag-x:hover { color:var(--danger, #dc2626) }
    .tag-add { width:auto; min-width:180px }
    .georow { display:flex; gap:8px } .georow .input { flex:1 }
    .geo-msg { margin-top:6px; font:600 12px var(--ui); color:#b45309 }
    .thumbs { display:flex; flex-wrap:wrap; gap:10px; margin-top:10px }
    .thumb { position:relative; width:96px; height:96px; border:1px solid var(--slate-200); border-radius:8px; overflow:hidden; background:var(--slate-100) }
    .thumb img { width:100%; height:100%; object-fit:cover; display:block }
    .thumb-x { position:absolute; top:2px; right:2px; width:20px; height:20px; border-radius:50%; border:0; background:rgba(15,23,42,.65); color:#fff; cursor:pointer; font-size:13px; line-height:20px; padding:0 }
    .foot-actions { display:flex; gap:8px; margin-top:12px }
    .muted { color:var(--slate-400) }
  `],
})
export class LabDetailComponent {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly lab = signal<LabDetail | null>(null);
  readonly statuses = STATUSES;
  readonly channels = CHANNELS;
  readonly days = DAYS;

  readonly segments = signal<string[]>([]);
  readonly governorates = signal<string[]>([]);
  readonly branches = signal<string[]>([]);
  readonly categories = signal<string[]>([]);
  readonly payers = signal<string[]>([]);
  readonly contracts = signal<string[]>([]);
  readonly cities = signal<City[]>([]);
  readonly areas = signal<Area[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  readonly images = signal<string[]>([]);

  readonly collectorReps = () => this.reps().filter((r) => r.type === 'Collector' || r.type === 'Scanning');
  readonly marketingReps = () => this.reps().filter((r) => r.type === 'Marketing');
  readonly selectedCollectors = () => this.collectorIds.map((id) => this.reps().find((r) => r.id === id)).filter((r): r is RepListItem => !!r);
  readonly availableCollectors = () => this.collectorReps().filter((r) => !this.collectorIds.includes(r.id));

  // Dependent selects: cities narrow by governorate, areas narrow by the chosen city (joined via the cities list).
  readonly filteredCities = () => this.cities().filter((c) => !this.f.governorate || c.governorate === this.f.governorate);
  readonly filteredAreas = () => {
    const city = this.filteredCities().find((c) => c.name === this.f.city);
    return city ? this.areas().filter((a) => a.cityId === city.id) : [];
  };

  collectorIds: string[] = [];
  workDays: string[] = [];
  managers: Contact[] = [];
  receptionists: Contact[] = [];

  mapQuery = '';
  readonly searching = signal(false);
  readonly geoMiss = signal(false);
  readonly geoFail = signal(false);

  f = {
    name: '', code: '', segment: 'C', status: 'Scanned', category: '', mappingCode: '', isEncrypted: false, branch: '',
    licenseNo: '', licenseDate: null as string | null, avgMonthlySamples: null as number | null,
    governorate: '', city: '', area: '', address: '', geo: '',
    payer: '', contractType: '', marketingRepId: '', preferredChannel: '',
    time1: '', time2: '',
  };

  private readonly id: string;

  constructor() {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    const ref = (type: string, sig: (v: string[]) => void) =>
      this.api.get<Ref[]>('/setup/refs', { type }).subscribe({ next: (r) => sig(r.map((x) => x.nameEn)) });
    ref('Segment', (v) => this.segments.set(v.length ? v : ['A', 'B', 'C']));
    ref('Governorate', (v) => this.governorates.set(v));
    ref('Branch', (v) => this.branches.set(v));
    ref('LabCategory', (v) => this.categories.set(v));
    ref('Payer', (v) => this.payers.set(v));
    ref('ContractType', (v) => this.contracts.set(v));
    this.api.get<City[]>('/setup/cities').subscribe({ next: (c) => this.cities.set(c) });
    this.api.get<Area[]>('/setup/areas').subscribe({ next: (a) => this.areas.set(a) });
    this.api.get<{ items: RepListItem[] }>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.get<LabDetail>(`/labs/${this.id}`).subscribe({
      next: (l) => {
        this.lab.set(l);
        this.f = {
          name: l.name, code: l.displayCode, segment: l.segment, status: l.status,
          category: l.category ?? '', mappingCode: l.mappingCode ?? '', isEncrypted: l.isEncrypted, branch: l.branch ?? '',
          licenseNo: l.licenseNo ?? '', licenseDate: l.licenseDate, avgMonthlySamples: l.avgMonthlySamples,
          governorate: l.governorate ?? '', city: l.city ?? '', area: l.area ?? '', address: l.address ?? '',
          geo: l.latitude != null && l.longitude != null ? `${l.latitude}, ${l.longitude}` : '',
          payer: l.payer ?? '', contractType: l.contractType ?? '', marketingRepId: l.marketingRepId ?? '',
          preferredChannel: l.preferredChannel ?? '',
          time1: (l.visitTimes[0] ?? '').slice(0, 5), time2: (l.visitTimes[1] ?? '').slice(0, 5),
        };
        this.collectorIds = [...l.collectorRepIds];
        this.workDays = l.workDays.map((d) => SHORT_DAYS[d] ?? d).filter((d) => DAYS.includes(d));
        this.managers = l.contacts.filter((c) => c.role === 'Manager').map((c) => ({ name: c.name, phone: c.phone ?? '', birthday: c.birthday }));
        this.receptionists = l.contacts.filter((c) => c.role === 'Receptionist').map((c) => ({ name: c.name, phone: c.phone ?? '', birthday: c.birthday }));
        this.images.set([...l.images]);
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.toast.error('Could not load laboratory.'); },
    });
  }

  onGovChange(v: string): void {
    this.f.governorate = v;
    if (!this.filteredCities().some((c) => c.name === this.f.city)) { this.f.city = ''; this.f.area = ''; }
  }

  onCityChange(v: string): void {
    this.f.city = v;
    if (!this.filteredAreas().some((a) => a.name === this.f.area)) this.f.area = '';
  }

  addCollector(id: string): void { if (id && !this.collectorIds.includes(id)) this.collectorIds = [...this.collectorIds, id]; }
  removeCollector(id: string): void { this.collectorIds = this.collectorIds.filter((x) => x !== id); }
  canViewLocation(): boolean { return this.auth.has('ViewLabLocation'); }
  toggleDay(d: string): void { this.workDays = this.workDays.includes(d) ? this.workDays.filter((x) => x !== d) : [...this.workDays, d]; }

  geoLat(): number | null { return parseGeo(this.f.geo)?.lat ?? null; }
  geoLng(): number | null { return parseGeo(this.f.geo)?.lng ?? null; }
  onPick(c: { lat: number; lng: number }): void { this.f.geo = `${c.lat}, ${c.lng}`; }

  async searchLocation(): Promise<void> {
    const q = this.mapQuery.trim();
    if (!q || this.searching()) return;
    this.geoMiss.set(false); this.geoFail.set(false); this.searching.set(true);
    try {
      const res = await fetch('https://nominatim.openstreetmap.org/search?format=json&limit=1&q=' + encodeURIComponent(q));
      const hits = (await res.json()) as { lat: string; lon: string }[];
      if (Array.isArray(hits) && hits.length) this.f.geo = `${(+hits[0].lat).toFixed(6)}, ${(+hits[0].lon).toFixed(6)}`;
      else this.geoMiss.set(true);
    } catch {
      this.geoFail.set(true); // offline / blocked geocoder must never break the form
    } finally {
      this.searching.set(false);
    }
  }

  onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    if (!files.length) return;
    this.busy.set(true);
    let pending = files.length;
    for (const file of files) {
      const data = new FormData();
      data.append('file', file);
      this.api.post<{ path: string }>('/labs/upload', data).subscribe({
        next: (r) => { this.images.update((a) => [...a, r.path]); if (--pending === 0) this.busy.set(false); },
        error: () => { if (--pending === 0) this.busy.set(false); },
      });
    }
  }

  removeImage(index: number): void { this.images.update((a) => a.filter((_, i) => i !== index)); }

  canSave(): boolean {
    return !!this.f.name.trim() && !!this.f.governorate && !!this.f.city && !!this.f.time1 && this.workDays.length > 0;
  }

  save(): void {
    const l = this.lab();
    if (!l || !this.canSave() || this.busy()) return;
    const canGeo = this.canViewLocation();
    const geo = canGeo ? parseGeo(this.f.geo) : null;
    if (canGeo && this.f.geo.trim() && !geo) { this.toast.warning('Enter coordinates as "lat, lng" (e.g. 30.0444, 31.2357).'); return; }
    this.busy.set(true);
    const contacts = [
      ...this.managers.filter((c) => c.name.trim()).map((c) => ({ name: c.name, role: 'Manager', phone: c.phone || null, birthday: c.birthday || null })),
      ...this.receptionists.filter((c) => c.name.trim()).map((c) => ({ name: c.name, role: 'Receptionist', phone: c.phone || null, birthday: c.birthday || null })),
    ];
    this.api.put(`/labs/${this.id}`, {
      id: this.id, rowVersion: l.rowVersion, name: this.f.name, segment: this.f.segment,
      branch: this.f.branch || null, governorate: this.f.governorate || null,
      city: this.f.city || null, area: this.f.area || null, address: this.f.address || null,
      category: this.f.category || null, mappingCode: this.f.mappingCode || null,
      isEncrypted: this.f.isEncrypted, images: this.images(),
      payer: this.f.payer || null, contractType: this.f.contractType || null,
      licenseNo: this.f.licenseNo || null, licenseDate: this.f.licenseDate || null,
      avgMonthlySamples: this.f.avgMonthlySamples, preferredChannel: this.f.preferredChannel || null,
      // Editors without ViewLabLocation never see coordinates — preserve the loaded values, never wipe them.
      latitude: canGeo ? geo?.lat ?? null : l.latitude,
      longitude: canGeo ? geo?.lng ?? null : l.longitude,
      workDays: this.workDays.map((d) => DAY_NAMES[d]),
      visitTimes: [this.f.time1, this.f.time2].filter(Boolean),
      collectorRepIds: this.collectorIds, marketingRepId: this.f.marketingRepId || null,
      contacts,
    }).subscribe({
      next: () => {
        // The Update command intentionally never touches status — change it via the dedicated endpoint when edited.
        if (this.f.status !== l.status) {
          this.api.put(`/labs/${this.id}/status`, { status: this.f.status }).subscribe({
            next: () => void this.router.navigate(['/labs']),
            error: () => { this.busy.set(false); },
          });
        } else {
          void this.router.navigate(['/labs']);
        }
      },
      error: () => {
        this.busy.set(false);
      },
    });
  }

  cancel(): void { void this.router.navigate(['/labs']); }
}
