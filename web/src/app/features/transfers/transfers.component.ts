import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { PagedResult, RepListItem, TransferItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-transfers',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, TranslatePipe],
  template: `
    <h1 class="display page-title">{{ 'nav.transfers' | t }}</h1>
    <p class="lede">Checked-in samples awaiting hand-off to a transfer representative.</p>

    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">{{ 'common.loading' | t }}</div> }
      @if (!loading()) {
        <table class="app">
          <thead><tr><th>Lab</th><th>Name</th><th>Visit date</th><th>{{ 'daily.samples' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (t of items(); track t.visitId) {
              <tr>
                <td class="client-code mono">{{ t.labDisplayCode }}</td>
                <td>{{ t.labName }}</td>
                <td>{{ t.visitDate | date:'mediumDate' }}</td>
                <td class="mono">{{ t.sampleCount ?? '—' }}</td>
                <td class="actions">
                  @if (auth.has('ConfirmTransfers')) {
                    <button class="btn btn-mini btn-p" (click)="openFor(t)" [disabled]="busy()">Confirm transfer</button>
                  }
                </td>
              </tr>
              @if (active()?.visitId === t.visitId) {
                <tr class="formrow"><td colspan="5">
                  <form class="tform" [formGroup]="form" (ngSubmit)="confirm()">
                    @if (formError()) { <div class="inline-banner inline-banner-error">{{ formError() }}</div> }
                    <div class="frow">
                      <div class="field"><label>Transfer rep <span class="req">*</span></label>
                        <select formControlName="transferRepId"><option value="">— select —</option>
                          @for (r of reps(); track r.id) { <option [value]="r.id">{{ r.fullName }}</option> }
                        </select></div>
                      <div class="field"><label>Driver name <span class="req">*</span></label><input formControlName="driverName"></div>
                      <div class="field"><label>Driver mobile <span class="req">*</span></label><input formControlName="driverMobile"></div>
                      <div class="field"><label>Car plate</label><input formControlName="carPlate"></div>
                    </div>
                    <div class="fbtns">
                      <button class="btn btn-mini btn-p" type="submit" [disabled]="form.invalid || busy()">Confirm</button>
                      <button class="btn btn-mini btn-s" type="button" (click)="active.set(null)">Cancel</button>
                    </div>
                  </form>
                </td></tr>
              }
            } @empty { <tr><td colspan="5" class="empty">Nothing awaiting transfer.</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`
    .page-title { font-size:22px; margin:0 0 4px; } .lede { color:var(--slate-500); font-size:13px; margin:0 0 16px; }
    .empty { color:var(--slate-500); text-align:center; padding:24px; }
    .actions { display:flex; gap:6px; } .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
    .formrow td { background: var(--filter-bg); padding:14px 16px; }
    .frow { display:flex; gap:14px; flex-wrap:wrap; }
    .field { flex:1; min-width:180px; } .field label { display:block; font:600 11.5px var(--ui); color:var(--slate-600); margin-bottom:4px; }
    .field input, .field select { width:100%; border:1px solid var(--slate-300); border-radius:var(--r-input); padding:7px 9px; font-size:12.5px; background:var(--white); color:var(--slate-900); }
    .req { color: var(--danger, #dc2626); } .fbtns { display:flex; gap:8px; margin-top:12px; }
  `],
})
export class TransfersComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<TransferItem[]>([]);
  readonly reps = signal<RepListItem[]>([]);
  readonly active = signal<TransferItem | null>(null);
  readonly formError = signal<string | null>(null);

  readonly form = this.fb.group({
    transferRepId: this.fb.control('', Validators.required),
    driverName: this.fb.control('', Validators.required),
    driverMobile: this.fb.control('', Validators.required),
    carPlate: this.fb.control(''),
  });

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<TransferItem[]>('/transfers').subscribe({
      next: (t) => { this.items.set(t); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  openFor(t: TransferItem): void {
    this.active.set(t);
    this.formError.set(null);
    this.form.reset({ transferRepId: '', driverName: '', driverMobile: '', carPlate: '' });
    if (this.reps().length === 0) {
      this.api.get<PagedResult<RepListItem>>('/reps', { pageSize: 500 }).subscribe({ next: (r) => this.reps.set(r.items) });
    }
  }

  confirm(): void {
    const t = this.active();
    if (!t || this.form.invalid) return;
    this.busy.set(true);
    this.formError.set(null);
    const v = this.form.getRawValue();
    this.api.post('/transfers/confirm', { visitId: t.visitId, ...v, carPlate: v.carPlate || null }).subscribe({
      next: () => { this.busy.set(false); this.active.set(null); this.load(); },
      error: (err) => { this.busy.set(false); this.formError.set(err?.error?.detail ?? 'Confirm failed.'); },
    });
  }
}
