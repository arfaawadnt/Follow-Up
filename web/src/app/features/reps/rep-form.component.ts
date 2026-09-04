import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DateInputComponent } from '../../shared/date-input.component';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { TranslatePipe } from '../../core/i18n';
import { RepDetail } from '../../core/models';
import { ToastService } from '../../core/toast.service';

interface Ref { nameEn: string; }
interface City { id: string; name: string; governorate: string; }
interface Area { id: string; name: string; cityId: string; }

const TYPES = ['Collector', 'Marketing', 'Transfer', 'Scanning'];
const EMPLOYMENT = ['Full-time', 'Part-time', 'Contract'];
const GOAL_TYPES = ['Samples Collected', 'Visit Completion', 'New Lab Contracts', 'Samples Growth', 'Lab Reactivations'];
const METRICS = ['Samples', 'visits %', 'Labs', '% growth', 'EGP'];
const DURATIONS = ['Monthly', 'Quarterly'];

@Component({
  selector: 'app-rep-form',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslatePipe, DateInputComponent],
  template: `
    @if (loading()) { <div class="card sect">{{ 'loading' | t : 'Loading…' }}</div> }
    @else {
      <div class="pagehead"><div>
        <div class="breadcrumbs">Home / <a routerLink="/reps" class="crumb">{{ 'representative_profiles' | t : 'Representative Profiles' }}</a> / {{ isEdit ? loadedName() : ('new_representative' | t : 'New representative') }}</div>
        <h1>@if (isEdit) { {{ 'edit_representative' | t : 'Edit Representative' }} — {{ loadedName() }} } @else { {{ 'new_representative_title' | t : 'New Representative' }} }</h1>
      </div></div>

      <section class="card sect"><h3>{{ 'identity_employment' | t : 'Identity & Employment' }}</h3>
        <div class="grid4">
          <div class="field"><label>{{ 'name_lbl' | t : 'Name *' }}</label><input class="input" [(ngModel)]="f.fullName"></div>
          <div class="field"><label>{{ 'type' | t : 'Type' }}</label><select class="select" [(ngModel)]="f.type" [disabled]="isEdit">@for (t of types; track t) { <option [value]="t">{{ t }}</option> }</select></div>
          <div class="field"><label>{{ 'phone' | t : 'Phone' }}</label><input class="input" [(ngModel)]="f.phone"></div>
          <div class="field"><label>{{ 'employment' | t : 'Employment' }}</label><select class="select" [(ngModel)]="f.employmentType">@for (e of employment; track e) { <option [value]="e">{{ e }}</option> }</select></div>
          <div class="field"><label>{{ 'salary_egp' | t : 'Salary (EGP)' }}</label><input type="number" min="0" class="input" [(ngModel)]="f.salary"></div>
          <div class="field"><label>{{ 'appointed' | t : 'Appointed' }}</label><app-date-input [(ngModel)]="f.appointedOn"></app-date-input></div>
          <div class="field"><label>{{ 'governorate_lbl' | t : 'Governorate *' }}</label><select class="select" [ngModel]="f.governorate" (ngModelChange)="onGovChange($event)"><option value="">—</option>@for (g of governorates(); track g) { <option [value]="g">{{ g }}</option> }</select></div>
          <div class="field"><label>{{ 'city' | t : 'City' }}</label><select class="select" [ngModel]="f.city" (ngModelChange)="onCityChange($event)"><option value="">—</option>@for (c of filteredCities(); track c.id) { <option [value]="c.name">{{ c.name }}</option> }</select></div>
          <div class="field"><label>{{ 'area' | t : 'Area' }}</label><select class="select" [(ngModel)]="f.area"><option value="">—</option>@for (a of filteredAreas(); track a.id) { <option [value]="a.name">{{ a.name }}</option> }</select></div>
        </div>
      </section>

      <section class="card sect"><h3>{{ 'goal_target' | t : 'Goal & Target' }}</h3>
        <div class="grid4">
          <div class="field"><label>{{ 'goal_type' | t : 'Goal type' }}</label><select class="select" [(ngModel)]="f.goalType"><option value="">—</option>@for (g of goalTypes; track g) { <option [value]="g">{{ g }}</option> }</select></div>
          <div class="field"><label>{{ 'target_amount_lbl' | t : 'Target amount *' }}</label><input type="number" min="0" class="input" [(ngModel)]="f.target"></div>
          <div class="field"><label>{{ 'metric' | t : 'Metric' }}</label><select class="select" [(ngModel)]="f.metric"><option value="">—</option>@for (m of metrics; track m) { <option [value]="m">{{ m }}</option> }</select></div>
          <div class="field"><label>{{ 'duration' | t : 'Duration' }}</label><select class="select" [(ngModel)]="f.goalDuration" [disabled]="isEdit">@for (d of durations; track d) { <option [value]="d">{{ d }}</option> }</select></div>
        </div>
      </section>

      <div class="foot-actions">
        <button type="button" class="btn btn-p" [disabled]="busy()" (click)="save()">{{ isEdit ? ('save_changes' | t : 'Save Changes') : ('create_profile' | t : 'Create Profile') }}</button>
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
    .field label { display:block; font:600 11px var(--ui); color:var(--slate-600); margin-bottom:4px }
    .foot-actions { display:flex; gap:8px; margin-top:12px }
  `],
})
export class RepFormComponent {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);

  readonly types = TYPES;
  readonly employment = EMPLOYMENT;
  readonly goalTypes = GOAL_TYPES;
  readonly metrics = METRICS;
  readonly durations = DURATIONS;

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly loadedName = signal('');

  readonly governorates = signal<string[]>([]);
  readonly cities = signal<City[]>([]);
  readonly areas = signal<Area[]>([]);

  // Dependent selects: cities narrow by governorate, areas narrow by the chosen city (joined via the cities list).
  readonly filteredCities = () => this.cities().filter((c) => !this.f.governorate || c.governorate === this.f.governorate);
  readonly filteredAreas = () => {
    const city = this.filteredCities().find((c) => c.name === this.f.city);
    return city ? this.areas().filter((a) => a.cityId === city.id) : [];
  };

  f = {
    fullName: '', type: 'Collector', phone: '', employmentType: 'Full-time',
    salary: null as number | null, appointedOn: '',
    governorate: '', city: '', area: '',
    goalType: '', target: null as number | null, metric: '', goalDuration: 'Monthly',
  };

  private readonly id: string | null;
  private rowVersion = 0;
  // The reference form has no Branch field; keep the loaded value so an edit never wipes it.
  private branch: string | null = null;

  get isEdit(): boolean { return this.id !== null; }

  constructor() {
    this.id = this.route.snapshot.paramMap.get('id');
    this.api.get<Ref[]>('/setup/refs', { type: 'Governorate' }).subscribe({ next: (r) => this.governorates.set(r.map((x) => x.nameEn)) });
    this.api.get<City[]>('/setup/cities').subscribe({ next: (c) => this.cities.set(c) });
    this.api.get<Area[]>('/setup/areas').subscribe({ next: (a) => this.areas.set(a) });
    if (this.id) this.load(this.id);
  }

  private load(id: string): void {
    this.loading.set(true);
    this.api.get<RepDetail>(`/reps/${id}`).subscribe({
      next: (d) => {
        this.rowVersion = d.rowVersion;
        this.branch = d.branch;
        this.loadedName.set(d.fullName);
        this.f = {
          fullName: d.fullName, type: d.type, phone: d.phone ?? '', employmentType: d.employmentType ?? 'Full-time',
          salary: d.salary, appointedOn: d.appointedOn ? d.appointedOn.slice(0, 10) : '',
          governorate: d.governorate ?? '', city: d.city ?? '', area: d.area ?? '',
          goalType: d.goalType ?? '', target: d.target, metric: d.metric ?? '', goalDuration: d.goalDuration,
        };
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.toast.error('Could not load representative.'); },
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

  /** Labels of the mandatory fields still empty (empty array = ready to save). */
  private missingFields(): string[] {
    const m: string[] = [];
    if (!this.f.fullName.trim()) m.push('Full name');
    if (!this.f.governorate) m.push('Governorate');
    if (this.f.target == null) m.push('Target');
    return m;
  }

  save(): void {
    if (this.busy()) return;
    const missing = this.missingFields();
    if (missing.length) { this.toast.warning('Please fill in the required fields: ' + missing.join(', ') + '.'); return; }
    this.busy.set(true);
    const body: Record<string, unknown> = {
      fullName: this.f.fullName.trim(),
      salary: this.f.salary ?? 0, target: this.f.target ?? 0,
      goalType: this.f.goalType || null, metric: this.f.metric || null,
      phone: this.f.phone || null, branch: this.branch,
      governorate: this.f.governorate || null, city: this.f.city || null, area: this.f.area || null,
      employmentType: this.f.employmentType || null, appointedOn: this.f.appointedOn || null,
    };
    const req = this.id
      // Type and goal duration are create-only (UpdateRepresentativeCommand has no such fields).
      ? this.api.put(`/reps/${this.id}`, { ...body, id: this.id, rowVersion: this.rowVersion })
      : this.api.post('/reps', { ...body, type: this.f.type, goalDuration: this.f.goalDuration });
    req.subscribe({
      next: () => void this.router.navigate(['/reps']),
      error: () => {
        this.busy.set(false);
      },
    });
  }

  cancel(): void { void this.router.navigate(['/reps']); }
}
