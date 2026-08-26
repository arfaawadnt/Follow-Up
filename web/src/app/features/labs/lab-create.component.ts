import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { RepListItem } from '../../core/models';

interface Ref { nameEn: string; }
interface City { id: string; name: string; governorate: string; }
interface Area { id: string; name: string; cityId: string; }
interface Contact { name: string; phone: string; birthday: string | null; }

const STATUSES = ['Scanned', 'Interactive', 'Active', 'Inactive', 'Stopped'];
const CHANNELS = ['WhatsApp', 'Phone Call', 'Email', 'In-person'];
const DAYS = ['Sat', 'Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri'];
const DAY_NAMES: Record<string, string> = { Sat: 'Saturday', Sun: 'Sunday', Mon: 'Monday', Tue: 'Tuesday', Wed: 'Wednesday', Thu: 'Thursday', Fri: 'Friday' };

@Component({
  selector: 'app-lab-create',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / New Laboratory</div><h1>New Laboratory</h1></div></div>

    <section class="card sect"><h3>Identity</h3>
      <div class="grid4">
        <div class="field"><label>Lab name *</label><input class="input" [(ngModel)]="f.name"></div>
        <div class="field"><label>Lab code *</label><input class="input" [(ngModel)]="f.code"></div>
        <div class="field"><label>Segment</label><select class="select" [(ngModel)]="f.segment">@for (s of segments(); track s) { <option [value]="s">{{ s }}</option> }</select></div>
        <div class="field"><label>Status</label><select class="select" [(ngModel)]="f.status">@for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }</select></div>
        <div class="field"><label>Lab Category</label><select class="select" [(ngModel)]="f.category"><option value="">—</option>@for (c of categories(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        <div class="field"><label>Serving branch</label><select class="select" [(ngModel)]="f.branch"><option value="">—</option>@for (b of branches(); track b) { <option [value]="b">{{ b }}</option> }</select></div>
        <div class="field"><label>License no.</label><input class="input" [(ngModel)]="f.licenseNo"></div>
        <div class="field"><label>License date</label><input type="date" class="input" [(ngModel)]="f.licenseDate"></div>
        <div class="field"><label>Avg monthly samples</label><input type="number" min="0" class="input" [(ngModel)]="f.avgMonthlySamples"></div>
      </div>
    </section>

    <section class="card sect"><h3>Location</h3>
      <div class="grid4">
        <div class="field"><label>Governorate *</label><select class="select" [(ngModel)]="f.governorate"><option value="">—</option>@for (g of governorates(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
        <div class="field"><label>City</label><select class="select" [(ngModel)]="f.city"><option value="">—</option>@for (c of cities(); track c.id) { <option [value]="c.name">{{ c.name }}</option> }</select></div>
        <div class="field"><label>Area</label><select class="select" [(ngModel)]="f.area"><option value="">—</option>@for (a of areas(); track a.id) { <option [value]="a.name">{{ a.name }}</option> }</select></div>
        <div class="field"><label>Latitude</label><input type="number" class="input" [(ngModel)]="f.latitude"></div>
        <div class="field"><label>Longitude</label><input type="number" class="input" [(ngModel)]="f.longitude"></div>
        <div class="field" style="grid-column:1/-1"><label>Address</label><input class="input" [(ngModel)]="f.address" placeholder="street, building, floor…"></div>
      </div>
    </section>

    <section class="card sect"><h3>Commercial &amp; Assignment</h3>
      <div class="grid4">
        <div class="field"><label>Payer type</label><select class="select" [(ngModel)]="f.payer"><option value="">—</option>@for (p of payers(); track p) { <option [value]="p">{{ p }}</option> }</select></div>
        <div class="field"><label>Contract</label><select class="select" [(ngModel)]="f.contractType"><option value="">—</option>@for (c of contracts(); track c) { <option [value]="c">{{ c }}</option> }</select></div>
        <div class="field"><label>Marketing rep</label><select class="select" [(ngModel)]="f.marketingRepId"><option value="">—</option>@for (r of marketingReps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }</select></div>
        <div class="field"><label>Preferred channel</label><select class="select" [(ngModel)]="f.preferredChannel"><option value="">—</option>@for (c of channels; track c) { <option [value]="c">{{ c }}</option> }</select></div>
      </div>
      <div class="field" style="margin-top:10px"><label>Collection reps (one or more)</label>
        <div class="chips">
          @for (r of collectorReps(); track r.id) {
            <label class="chip"><input type="checkbox" [checked]="collectorIds.includes(r.id)" (change)="toggleCollector(r.id)"> {{ r.fullName }}</label>
          } @empty { <span class="muted">No collector reps.</span> }
        </div>
      </div>
    </section>

    <section class="card sect"><h3>Collection Schedule</h3>
      <div class="grid4">
        <div class="field"><label>Visit time 1 *</label><input type="time" class="input" [(ngModel)]="f.time1"></div>
        <div class="field"><label>Visit time 2 (optional)</label><input type="time" class="input" [(ngModel)]="f.time2"></div>
      </div>
      <div class="field" style="margin-top:10px"><label>Working days *</label>
        <div class="chips">@for (d of days; track d) { <label class="chip"><input type="checkbox" [checked]="workDays.includes(d)" (change)="toggleDay(d)"> {{ d }}</label> }</div>
      </div>
    </section>

    <section class="card sect"><h3>Contacts — Managers</h3>
      @for (c of managers; track $index) {
        <div class="grid4 crow"><input class="input" placeholder="Name" [(ngModel)]="c.name"><input class="input" placeholder="Phone" [(ngModel)]="c.phone"><input type="date" class="input" [(ngModel)]="c.birthday"><button class="btn btn-s btn-mini" (click)="managers.splice($index,1)">Remove</button></div>
      }
      <button class="btn btn-s btn-mini" (click)="managers.push({name:'',phone:'',birthday:null})">+ Add manager</button>
    </section>

    <section class="card sect"><h3>Contacts — Receptionists</h3>
      @for (c of receptionists; track $index) {
        <div class="grid4 crow"><input class="input" placeholder="Name" [(ngModel)]="c.name"><input class="input" placeholder="Phone" [(ngModel)]="c.phone"><input type="date" class="input" [(ngModel)]="c.birthday"><button class="btn btn-s btn-mini" (click)="receptionists.splice($index,1)">Remove</button></div>
      }
      <button class="btn btn-s btn-mini" (click)="receptionists.push({name:'',phone:'',birthday:null})">+ Add receptionist</button>
    </section>

    @if (error()) { <div class="inline-banner inline-banner-error">{{ error() }}</div> }
    <div style="display:flex;gap:8px;margin-top:12px">
      <button class="btn btn-p" [disabled]="!canSave() || busy()" (click)="submit()">Create Laboratory</button>
      <button class="btn btn-s" (click)="cancel()">Cancel</button>
    </div>
  `,
  styles: [`
    .sect { padding:20px; margin-bottom:16px }
    .sect h3 { margin:0 0 14px; font:700 15px var(--ui); color:var(--slate-800) }
    .grid4 { display:grid; grid-template-columns:repeat(4,1fr); gap:12px }
    @media (max-width:900px){ .grid4 { grid-template-columns:1fr 1fr } }
    .crow { align-items:center; margin-bottom:8px }
    .field label { display:block; font:600 11px var(--ui); color:var(--slate-600); margin-bottom:4px }
    .chips { display:flex; flex-wrap:wrap; gap:10px }
    .chip { display:flex; align-items:center; gap:6px; font:600 12px var(--ui); color:var(--slate-700); border:1px solid var(--slate-300); border-radius:8px; padding:6px 10px; cursor:pointer }
    .muted { color:var(--slate-400) }
  `],
})
export class LabCreateComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
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
  readonly collectorReps = computed(() => this.reps().filter((r) => r.type === 'Collector' || r.type === 'Scanning'));
  readonly marketingReps = computed(() => this.reps().filter((r) => r.type === 'Marketing'));

  collectorIds: string[] = [];
  workDays: string[] = [];
  managers: Contact[] = [];
  receptionists: Contact[] = [];

  f = {
    name: '', code: '', segment: 'A', status: 'Scanned', category: '', branch: '',
    licenseNo: '', licenseDate: null as string | null, avgMonthlySamples: null as number | null,
    governorate: '', city: '', area: '', address: '', latitude: null as number | null, longitude: null as number | null,
    payer: '', contractType: '', marketingRepId: '', preferredChannel: '',
    time1: '', time2: '',
  };

  constructor() {
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
  }

  toggleCollector(id: string): void { this.collectorIds = this.collectorIds.includes(id) ? this.collectorIds.filter((x) => x !== id) : [...this.collectorIds, id]; }
  toggleDay(d: string): void { this.workDays = this.workDays.includes(d) ? this.workDays.filter((x) => x !== d) : [...this.workDays, d]; }
  canSave(): boolean { return !!this.f.name.trim() && !!this.f.code.trim() && !!this.f.time1 && this.workDays.length > 0; }

  submit(): void {
    this.busy.set(true); this.error.set(null);
    const contacts = [
      ...this.managers.filter((c) => c.name.trim()).map((c) => ({ name: c.name, role: 'Manager', phone: c.phone || null, birthday: c.birthday || null })),
      ...this.receptionists.filter((c) => c.name.trim()).map((c) => ({ name: c.name, role: 'Receptionist', phone: c.phone || null, birthday: c.birthday || null })),
    ];
    this.api.post<{ id: string }>('/labs', {
      code: this.f.code, name: this.f.name, segment: this.f.segment, status: this.f.status,
      category: this.f.category || null, branch: this.f.branch || null,
      licenseNo: this.f.licenseNo || null, licenseDate: this.f.licenseDate || null,
      avgMonthlySamples: this.f.avgMonthlySamples, preferredChannel: this.f.preferredChannel || null,
      governorate: this.f.governorate || null, city: this.f.city || null, area: this.f.area || null,
      address: this.f.address || null,
      latitude: this.f.latitude, longitude: this.f.longitude,
      payer: this.f.payer || null, contractType: this.f.contractType || null,
      collectorRepIds: this.collectorIds, marketingRepId: this.f.marketingRepId || null,
      workDays: this.workDays.map((d) => DAY_NAMES[d]),
      visitTimes: [this.f.time1, this.f.time2].filter(Boolean),
      contacts,
    }).subscribe({
      next: () => void this.router.navigate(['/labs']),
      error: (e) => { this.busy.set(false); this.error.set(e?.error?.detail ?? 'Create failed.'); },
    });
  }
  cancel(): void { void this.router.navigate(['/labs']); }
}
