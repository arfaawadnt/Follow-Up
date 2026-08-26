import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepDetail, RepListItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

const TYPES = ['Collector', 'Marketing', 'Transfer', 'Scanning'];
const EMPLOYMENT = ['Full-time', 'Part-time', 'Contract'];

@Component({
  selector: 'app-reps',
  standalone: true,
  imports: [FormsModule, DecimalPipe, TranslatePipe],
  template: `
    <div class="pagehead" style="display:flex;justify-content:space-between;align-items:center">
      <div><div class="breadcrumbs">Home / {{ 'reps_2' | t : 'Representative Profiles' }}</div><h1>{{ 'representative_profiles' | t : 'Representative Profiles' }}</h1></div>
      @if (canEdit()) { <button class="btn btn-p" (click)="openNew()" style="height:38px">+ {{ 'new_representative' | t : 'New representative' }}</button> }
    </div>

    @if (showForm() && canEdit()) {
      <div class="card" style="padding:20px;margin-bottom:16px">
        <h3 style="margin:0 0 14px">{{ editId() ? 'Edit representative' : 'New representative' }}</h3>
        <div class="frm-grid" style="grid-template-columns:repeat(4,1fr);gap:12px">
          <div class="field"><label>Full name</label><input class="input" [(ngModel)]="f.fullName"></div>
          <div class="field"><label>Type</label><select class="select" [(ngModel)]="f.type">@for (t of typeOptions; track t) { <option [value]="t">{{ t }}</option> }</select></div>
          <div class="field"><label>Employment</label><select class="select" [(ngModel)]="f.employmentType">@for (e of employment; track e) { <option [value]="e">{{ e }}</option> }</select></div>
          <div class="field"><label>Phone</label><input class="input" [(ngModel)]="f.phone"></div>
          <div class="field"><label>Governorate</label><input class="input" [(ngModel)]="f.governorate"></div>
          <div class="field"><label>Area</label><input class="input" [(ngModel)]="f.area"></div>
          <div class="field"><label>Branch</label><input class="input" [(ngModel)]="f.branch"></div>
          <div class="field"><label>Goal duration</label><select class="select" [(ngModel)]="f.goalDuration"><option value="Monthly">Monthly</option><option value="Quarterly">Quarterly</option></select></div>
          <div class="field"><label>Goal type</label><input class="input" [(ngModel)]="f.goalType" placeholder="e.g. Samples Collected"></div>
          <div class="field"><label>Target</label><input type="number" min="0" class="input" [(ngModel)]="f.target"></div>
          <div class="field"><label>Metric</label><input class="input" [(ngModel)]="f.metric" placeholder="e.g. Samples"></div>
          <div class="field"><label>Salary (EGP)</label><input type="number" min="0" class="input" [(ngModel)]="f.salary"></div>
        </div>
        <div style="margin-top:12px;display:flex;gap:8px;justify-content:flex-end">
          <button class="btn btn-s" (click)="showForm.set(false)" style="height:36px">Cancel</button>
          <button class="btn btn-p" [disabled]="!f.fullName.trim() || busy()" (click)="submit()" style="height:36px">Save</button>
        </div>
      </div>
    }

    <div class="card" style="padding:12px;margin-bottom:14px">
      <div style="display:flex;gap:6px;flex-wrap:wrap">
        @for (t of typeFilters; track t) { <span class="pill" [class.on]="type() === t" (click)="setType(t)">{{ t }}</span> }
      </div>
    </div>

    <div class="card" style="padding:0;overflow:hidden">
      @if (loading()) { <div class="empty" style="padding:24px">Loading…</div> }
      @else {
        <div style="overflow-x:auto"><table class="grid-table" style="margin:0;border:none">
          <thead><tr><th>Representative</th><th>Type</th><th>Phone</th><th>Goal</th><th>Target</th><th>Duration</th><th>Salary</th><th>{{ 'assigned_labs' | t : 'Assigned Labs' }}</th><th></th></tr></thead>
          <tbody>
            @for (r of filtered(); track r.id) {
              <tr>
                <td><b style="color:var(--slate-900)">{{ r.fullName }}</b><div class="small muted">{{ sub(r) }}</div></td>
                <td><span class="badge" [class]="r.type === 'Collector' ? 'b-info' : 'b-pur'">{{ r.type }}</span></td>
                <td class="mono">{{ r.phone ?? '—' }}</td>
                <td>{{ r.goalType ?? '—' }}</td>
                <td class="mono">{{ r.target | number:'1.0-0' }}<span class="small muted"> {{ r.metric }}</span></td>
                <td>{{ r.goalDuration }}</td>
                <td class="mono">EGP {{ r.salary | number:'1.0-0' }}</td>
                <td class="mono" style="font-weight:700">{{ r.assignedCount }}</td>
                <td class="actions">@if (canEdit()) { <button class="btn btn-mini btn-s" (click)="openEdit(r)" [disabled]="busy()">Edit</button> }</td>
              </tr>
            } @empty { <tr><td colspan="9" class="empty" style="text-align:center;padding:24px">—</td></tr> }
          </tbody>
        </table></div>
      }
    </div>
  `,
  styles: [`.actions{display:flex;gap:6px}.pill{cursor:pointer}.field label{display:block;font:600 11px var(--ui);color:var(--slate-600);margin-bottom:4px}`],
})
export class RepsComponent {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<RepListItem[]>([]);
  readonly typeOptions = TYPES;
  readonly typeFilters = ['All', ...TYPES];
  readonly employment = EMPLOYMENT;
  readonly type = signal('All');
  readonly showForm = signal(false);
  readonly editId = signal<string | null>(null);
  private editRowVersion = 0;

  f = this.blank();

  readonly filtered = computed(() => this.items());

  constructor() { this.load(); }

  canEdit(): boolean { return this.auth.has('AddReps') || this.auth.has('ManageReps') || this.auth.has('UpdateReps'); }
  sub(r: RepListItem): string { return [r.governorate, r.area, r.employmentType].filter(Boolean).join(' · '); }
  setType(t: string): void { this.type.set(t); this.load(); }

  private blank() {
    return { fullName: '', type: 'Collector', employmentType: 'Full-time', phone: '', governorate: '', area: '', branch: '', goalDuration: 'Monthly', goalType: '', target: 0, metric: '', salary: 0 };
  }

  load(): void {
    this.loading.set(true);
    const params: Record<string, string | number> = { pageSize: 500 };
    if (this.type() !== 'All') params['type'] = this.type();
    this.api.get<PagedResult<RepListItem>>('/reps', params).subscribe({
      next: (r) => { this.items.set(r.items); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  openNew(): void { this.editId.set(null); this.f = this.blank(); this.showForm.set(true); }

  openEdit(r: RepListItem): void {
    this.api.get<RepDetail>(`/reps/${r.id}`).subscribe({
      next: (d) => {
        this.editId.set(d.id); this.editRowVersion = d.rowVersion;
        this.f = {
          fullName: d.fullName, type: d.type, employmentType: d.employmentType ?? 'Full-time', phone: d.phone ?? '',
          governorate: d.governorate ?? '', area: d.area ?? '', branch: d.branch ?? '',
          goalDuration: d.goalDuration, goalType: d.goalType ?? '', target: d.target, metric: d.metric ?? '', salary: d.salary,
        };
        this.showForm.set(true);
      },
    });
  }

  submit(): void {
    if (!this.f.fullName.trim()) return;
    this.busy.set(true);
    const body: Record<string, unknown> = {
      fullName: this.f.fullName.trim(), type: this.f.type, goalDuration: this.f.goalDuration,
      salary: this.f.salary || 0, target: this.f.target || 0,
      goalType: this.f.goalType || null, metric: this.f.metric || null,
      phone: this.f.phone || null, governorate: this.f.governorate || null, area: this.f.area || null,
      branch: this.f.branch || null, employmentType: this.f.employmentType || null,
    };
    const id = this.editId();
    const req = id
      ? this.api.put(`/reps/${id}`, { ...body, id, rowVersion: this.editRowVersion })
      : this.api.post('/reps', body);
    req.subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.f = this.blank(); this.load(); },
      error: () => this.busy.set(false),
    });
  }
}
