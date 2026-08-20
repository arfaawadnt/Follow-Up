import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LabDetail } from '../../core/models';
import { StatusBadgePipe } from '../../shared/status-badge.pipe';
import { MapComponent } from '../../shared/map.component';

/** Extracts a (lat,lng) pair from common map-URL shapes: @lat,lng · q=lat,lng · !3dlat!4dlng · mlat/mlon · /lat,lng. */
function parseCoords(text: string): { lat: number; lng: number } | null {
  if (!text) return null;
  const patterns: RegExp[] = [
    /@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)/,
    /[?&]q=(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)/,
    /!3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)/,
    /[?&]mlat=(-?\d{1,2}\.\d+)&mlon=(-?\d{1,3}\.\d+)/,
    /(-?\d{1,2}\.\d{4,}),\s*(-?\d{1,3}\.\d{4,})/,
  ];
  for (const re of patterns) {
    const m = text.match(re);
    if (m) {
      const lat = +m[1], lng = +m[2];
      if (Math.abs(lat) <= 90 && Math.abs(lng) <= 180) return { lat, lng };
    }
  }
  return null;
}

const STATUSES = ['New', 'Scanned', 'Active', 'Inactive', 'Pending', 'Suspended', 'Stopped', 'Churned'];

@Component({
  selector: 'app-lab-detail',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule, RouterLink, StatusBadgePipe, MapComponent],
  template: `
    @if (loading()) { <div class="dcard"><div class="cbody">Loading…</div></div> }
    @if (lab(); as l) {
      <div class="head">
        <div>
          <a routerLink="/labs" class="back">← Laboratories</a>
          <h1 class="display page-title">{{ l.displayCode }} · {{ l.name }}</h1>
        </div>
        <div class="tools">
          <span class="badge" [class]="l.status | statusBadge">{{ l.status }}</span>
          @if (auth.has('UpdateLabs')) {
            <select class="status-sel" (change)="changeStatus($event)" [disabled]="busy()">
              <option value="">Set status →</option>
              @for (s of statuses; track s) { <option [value]="s" [disabled]="s === l.status">{{ s }}</option> }
            </select>
            <button class="btn btn-s" (click)="editing.set(!editing())">{{ editing() ? 'Cancel edit' : 'Edit' }}</button>
          }
        </div>
      </div>

      @if (banner()) { <div class="inline-banner" [class.inline-banner-error]="bannerError()">{{ banner() }}</div> }

      @if (!editing()) {
        <div class="grid">
          <div class="dcard"><div class="cbody">
            <h3 class="sec">Profile</h3>
            <dl>
              <dt>Segment</dt><dd>{{ l.segment }}</dd>
              <dt>Category</dt><dd>{{ l.category ?? '—' }}</dd>
              <dt>Branch</dt><dd>{{ l.branch ?? '—' }}</dd>
              <dt>Governorate</dt><dd>{{ l.governorate ?? '—' }}</dd>
              <dt>City / Area</dt><dd>{{ l.city ?? '—' }} / {{ l.area ?? '—' }}</dd>
              <dt>Payer</dt><dd>{{ l.payer ?? '—' }}</dd>
              <dt>Contract</dt><dd>{{ l.contractType ?? '—' }}</dd>
              <dt>Monthly target</dt><dd class="mono">{{ l.monthlyTarget }}</dd>
              <dt>Loyalty</dt><dd>{{ l.loyaltyPoints }} pts @if (l.loyaltyTier) { · {{ l.loyaltyTier }} }</dd>
              <dt>Work days</dt><dd>{{ l.workDays.length ? l.workDays.join(', ') : '—' }}</dd>
              <dt>Visit times</dt><dd class="mono">{{ l.visitTimes.length ? l.visitTimes.join(', ') : '—' }}</dd>
              <dt>Coordinates</dt><dd class="mono">{{ l.latitude != null ? (l.latitude + ', ' + l.longitude) : '—' }}</dd>
            </dl>
          </div></div>
          <div class="dcard"><div class="cbody">
            <h3 class="sec">Contacts</h3>
            @if (l.contacts.length) {
              <ul class="contacts">
                @for (c of l.contacts; track c.id) {
                  <li><strong>{{ c.name }}</strong> <span class="role">{{ c.role }}</span>@if (c.phone) { <span class="mono ph">{{ c.phone }}</span> }</li>
                }
              </ul>
            } @else { <p class="muted">No contacts recorded.</p> }

            @if (auth.has('UpdateLabs')) {
              <h3 class="sec" style="margin-top:20px">Image</h3>
              <input type="file" accept="image/png,image/jpeg" (change)="upload($event)" [disabled]="busy()">
              @if (uploadedPath()) { <p class="muted">Uploaded: <span class="mono">{{ uploadedPath() }}</span></p> }
            }
          </div></div>
        </div>

        @if (l.latitude != null && l.longitude != null) {
          <div class="dcard" style="margin-top:16px"><div class="cbody">
            <h3 class="sec">Location</h3>
            <app-map [lat]="l.latitude" [lng]="l.longitude" [height]="280" />
            <p class="muted" style="margin-top:8px">
              <a class="ext" [href]="'https://www.openstreetmap.org/?mlat=' + l.latitude + '&mlon=' + l.longitude + '#map=15/' + l.latitude + '/' + l.longitude" target="_blank" rel="noopener">Open in OpenStreetMap ↗</a>
            </p>
          </div></div>
        }
      }

      @if (editing()) {
        <form class="dcard" [formGroup]="form" (ngSubmit)="save()">
          <div class="cbody">
            <div class="row">
              <div class="field"><label>Name <span class="req">*</span></label><input formControlName="name"></div>
              <div class="field"><label>Segment</label><select formControlName="segment"><option>A</option><option>B</option><option>C</option></select></div>
            </div>
            <div class="row">
              <div class="field"><label>Branch</label><input formControlName="branch"></div>
              <div class="field"><label>Governorate</label><input formControlName="governorate"></div>
            </div>
            <div class="row">
              <div class="field"><label>City</label><input formControlName="city"></div>
              <div class="field"><label>Area</label><input formControlName="area"></div>
            </div>
            <div class="row">
              <div class="field"><label>Category</label><input formControlName="category"></div>
              <div class="field"><label>Payer</label><input formControlName="payer"></div>
            </div>
            <div class="row">
              <div class="field"><label>Work days (comma)</label><input formControlName="workDays" placeholder="Sunday,Tuesday"></div>
              <div class="field"><label>Visit times (comma)</label><input formControlName="visitTimes" placeholder="09:00,14:30"></div>
            </div>

            <h3 class="sec" style="margin-top:8px">Location</h3>
            <div class="row">
              <div class="field"><label>Latitude</label><input type="number" step="any" formControlName="latitude"></div>
              <div class="field"><label>Longitude</label><input type="number" step="any" formControlName="longitude"></div>
            </div>
            <div class="row">
              <div class="field" style="min-width:100%">
                <label>Resolve from a maps link</label>
                <div class="resolve">
                  <input [(ngModel)]="mapsLink" [ngModelOptions]="{ standalone: true }" placeholder="Paste a Google/OSM maps link">
                  <button class="btn btn-s" type="button" [disabled]="!mapsLink || busy()" (click)="resolveLink()">Resolve</button>
                </div>
                @if (resolveError()) { <span class="muted err">{{ resolveError() }}</span> }
              </div>
            </div>
            <p class="muted">Click the map to drop the marker, or type coordinates above.</p>
            <app-map [lat]="form.controls.latitude.value" [lng]="form.controls.longitude.value"
                     [editable]="true" [height]="300" (coordChange)="onPick($event)" />
          </div>
          <div class="foot">
            <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">Save changes</button>
            <button class="btn btn-s" type="button" (click)="editing.set(false)">Cancel</button>
          </div>
        </form>
      }
    }
  `,
  styles: [`
    .head { display:flex; justify-content:space-between; align-items:flex-start; margin-bottom:16px; gap:16px; }
    .back { font-size:12px; color:var(--slate-500); text-decoration:none; }
    .page-title { font-size:22px; margin:4px 0 0; }
    .tools { display:flex; gap:10px; align-items:center; }
    .status-sel { padding:6px 8px; border:1px solid var(--slate-300); border-radius:var(--r-btn); background:var(--white); color:var(--slate-900); font-size:12px; }
    .grid { display:grid; grid-template-columns:1fr 1fr; gap:16px; }
    @media (max-width: 820px) { .grid { grid-template-columns:1fr; } }
    .sec { font:700 12px var(--ui); text-transform:uppercase; letter-spacing:.04em; color:var(--slate-500); margin:0 0 10px; }
    dl { display:grid; grid-template-columns:auto 1fr; gap:6px 16px; margin:0; }
    dt { color:var(--slate-500); font-size:12.5px; } dd { margin:0; font-size:13px; color:var(--slate-900); }
    .contacts { list-style:none; padding:0; margin:0; } .contacts li { padding:6px 0; border-bottom:1px solid var(--slate-150); font-size:13px; }
    .role { color:var(--slate-500); font-size:12px; margin-inline-start:8px; } .ph { margin-inline-start:8px; }
    .muted { color:var(--slate-500); font-size:12.5px; }
    .row { display:flex; gap:16px; flex-wrap:wrap; }
    .field { flex:1; min-width:220px; margin-bottom:12px; }
    .field label { display:block; font:600 12px var(--ui); color:var(--slate-600); margin-bottom:5px; }
    .field input, .field select { width:100%; border:1px solid var(--slate-300); border-radius:var(--r-input); padding:8px 10px; font-size:13px; background:var(--white); color:var(--slate-900); }
    .req { color: var(--danger, #dc2626); }
    .foot { display:flex; gap:10px; justify-content:flex-end; padding:14px 18px; border-top:1px solid var(--slate-150); background:var(--filter-bg); }
    .resolve { display:flex; gap:8px; } .resolve input { flex:1; }
    .err { color:#b45309; } .ext { color:var(--primary-blue); text-decoration:none; }
  `],
})
export class LabDetailComponent {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly lab = signal<LabDetail | null>(null);
  readonly editing = signal(false);
  readonly banner = signal<string | null>(null);
  readonly bannerError = signal(false);
  readonly uploadedPath = signal<string | null>(null);
  readonly statuses = STATUSES;

  readonly form = this.fb.group({
    name: this.fb.control('', Validators.required),
    segment: this.fb.control('C'),
    branch: this.fb.control(''), governorate: this.fb.control(''),
    city: this.fb.control(''), area: this.fb.control(''),
    category: this.fb.control(''), payer: this.fb.control(''),
    workDays: this.fb.control(''), visitTimes: this.fb.control(''),
    latitude: this.fb.control<number | null>(null),
    longitude: this.fb.control<number | null>(null),
  });

  mapsLink = '';
  readonly resolveError = signal<string | null>(null);

  private id = '';

  constructor() {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.api.get<LabDetail>(`/labs/${this.id}`).subscribe({
      next: (l) => {
        this.lab.set(l);
        this.form.patchValue({
          name: l.name, segment: l.segment, branch: l.branch ?? '', governorate: l.governorate ?? '',
          city: l.city ?? '', area: l.area ?? '', category: l.category ?? '', payer: l.payer ?? '',
          workDays: l.workDays.join(','), visitTimes: l.visitTimes.join(','),
          latitude: l.latitude, longitude: l.longitude,
        });
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); this.setBanner('Could not load laboratory.', true); },
    });
  }

  changeStatus(event: Event): void {
    const status = (event.target as HTMLSelectElement).value;
    if (!status) return;
    this.busy.set(true);
    this.api.put(`/labs/${this.id}/status`, { status }).subscribe({
      next: () => { this.busy.set(false); this.setBanner(`Status changed to ${status}.`, false); this.load(); },
      error: (err) => { this.busy.set(false); this.setBanner(err?.error?.detail ?? 'Status change failed.', true); },
    });
  }

  save(): void {
    const l = this.lab();
    if (!l || this.form.invalid) return;
    this.busy.set(true);
    const v = this.form.getRawValue();
    const split = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean);
    this.api.put(`/labs/${this.id}`, {
      id: this.id, rowVersion: l.rowVersion, name: v.name, segment: v.segment,
      branch: v.branch || null, governorate: v.governorate || null, city: v.city || null, area: v.area || null,
      category: v.category || null, payer: v.payer || null,
      collectorRepId: l.collectorRepId, marketingRepId: l.marketingRepId,
      latitude: v.latitude, longitude: v.longitude,
      workDays: split(v.workDays), visitTimes: split(v.visitTimes),
    }).subscribe({
      next: () => { this.busy.set(false); this.editing.set(false); this.setBanner('Saved.', false); this.load(); },
      error: (err) => {
        this.busy.set(false);
        this.setBanner(err?.status === 409 ? 'This lab changed since you opened it — reload and retry.' : (err?.error?.detail ?? 'Save failed.'), true);
      },
    });
  }

  upload(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const data = new FormData();
    data.append('file', file);
    this.busy.set(true);
    this.http.post<{ path: string }>(`${environment.apiBase}/labs/upload`, data).subscribe({
      next: (r) => { this.busy.set(false); this.uploadedPath.set(r.path); this.setBanner('Image uploaded.', false); },
      error: (err) => { this.busy.set(false); this.setBanner(err?.error?.detail ?? 'Upload failed.', true); },
    });
  }

  onPick(c: { lat: number; lng: number }): void {
    this.form.patchValue({ latitude: c.lat, longitude: c.lng });
  }

  resolveLink(): void {
    if (!this.mapsLink) return;
    this.busy.set(true);
    this.resolveError.set(null);
    // First try to parse coordinates directly from the pasted link; if none, ask the API to follow the redirect.
    const direct = parseCoords(this.mapsLink);
    if (direct) { this.applyCoords(direct); this.busy.set(false); return; }
    this.api.get<{ target: string }>('/maps/resolve-redirect', { url: this.mapsLink }).subscribe({
      next: (r) => {
        this.busy.set(false);
        const c = parseCoords(r.target ?? '');
        if (c) this.applyCoords(c);
        else this.resolveError.set('Could not find coordinates in that link.');
      },
      error: (err) => { this.busy.set(false); this.resolveError.set(err?.error?.detail ?? 'Could not resolve that link.'); },
    });
  }

  private applyCoords(c: { lat: number; lng: number }): void {
    this.form.patchValue({ latitude: c.lat, longitude: c.lng });
    this.mapsLink = '';
  }

  private setBanner(msg: string, error: boolean): void { this.banner.set(msg); this.bannerError.set(error); }
}
