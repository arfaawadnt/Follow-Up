import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { SampleTracking } from '../../core/models';

@Component({
  selector: 'app-sampletracking',
  standalone: true,
  imports: [DatePipe, FormsModule, ReactiveFormsModule],
  template: `
    <div class="head">
      <h1 class="display page-title">Sample Tracking</h1>
      <div class="tools">
        <input type="date" [(ngModel)]="date" (change)="load()">
        @if (auth.has('SampleTracking')) { <button class="btn btn-p" (click)="showForm.set(!showForm())">{{ showForm() ? 'Close' : 'New entry' }}</button> }
      </div>
    </div>

    @if (showForm()) {
      <form class="dcard formcard" [formGroup]="form" (ngSubmit)="submit()">
        <div class="cbody frow">
          @if (formError()) { <div class="inline-banner inline-banner-error" style="flex-basis:100%">{{ formError() }}</div> }
          <div class="field"><label>Area <span class="req">*</span></label><input formControlName="area"></div>
          <div class="field"><label>Count <span class="req">*</span></label><input type="number" min="1" formControlName="count"></div>
          <div class="fbtns">
            <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">Add</button>
          </div>
        </div>
      </form>
    }

    <div class="dcard"><div class="cbody" style="padding:0">
      @if (loading()) { <div class="cbody">Loading…</div> }
      @if (!loading()) {
        <table class="app">
          <thead><tr><th>Area</th><th>Count</th><th>Data entry</th><th>Review</th><th>Sort</th><th>State</th><th></th></tr></thead>
          <tbody>
            @for (s of items(); track s.id) {
              <tr>
                <td>{{ s.area }}</td>
                <td class="mono">{{ s.count }}</td>
                <td>{{ s.dataEntryBy ?? '—' }}</td>
                <td>{{ s.reviewBy ?? '—' }}</td>
                <td>{{ s.sortBy ?? '—' }}</td>
                <td>@if (s.isComplete) { <span class="badge b-ok">Complete</span> } @else { <span class="badge b-wait">In progress</span> }</td>
                <td class="actions">
                  @if (auth.has('SampleTracking') && !s.isComplete) {
                    @if (!s.reviewBy) { <button class="btn btn-mini btn-t" (click)="advance(s, 'Review')" [disabled]="busy()">Review</button> }
                    @if (s.reviewBy && !s.sortBy) { <button class="btn btn-mini btn-t" (click)="advance(s, 'Sort')" [disabled]="busy()">Sort</button> }
                  }
                </td>
              </tr>
            } @empty { <tr><td colspan="7" class="empty">No entries for this date.</td></tr> }
          </tbody>
        </table>
      }
    </div></div>
  `,
  styles: [`
    .head { display:flex; justify-content:space-between; align-items:center; margin-bottom:16px; }
    .page-title { font-size:22px; margin:0; } .tools { display:flex; gap:10px; align-items:center; }
    input[type=date] { border:1px solid var(--slate-300); border-radius:var(--r-input); padding:7px 10px; background:var(--white); color:var(--slate-900); }
    .empty { color:var(--slate-500); text-align:center; padding:24px; }
    .formcard { margin-bottom:16px; } .frow { display:flex; gap:16px; align-items:flex-end; flex-wrap:wrap; }
    .field { flex:0 1 220px; } .field label { display:block; font:600 12px var(--ui); color:var(--slate-600); margin-bottom:5px; }
    .field input { width:100%; border:1px solid var(--slate-300); border-radius:var(--r-input); padding:8px 10px; font-size:13px; background:var(--white); color:var(--slate-900); }
    .req { color: var(--danger, #dc2626); }
    .actions { display:flex; gap:6px; } .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
    .b-ok { background:#dcfce7; color:#166534; } .b-wait { background:#fef9c3; color:#854d0e; }
  `],
})
export class SampleTrackingComponent {
  private readonly api = inject(ApiService);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly auth = inject(AuthService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly items = signal<SampleTracking[]>([]);
  readonly showForm = signal(false);
  readonly formError = signal<string | null>(null);
  date = new Date().toISOString().slice(0, 10);

  readonly form = this.fb.group({
    area: this.fb.control('', Validators.required),
    count: this.fb.control(1, [Validators.required, Validators.min(1)]),
  });

  constructor() { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.get<SampleTracking[]>('/sample-tracking', { date: this.date }).subscribe({
      next: (s) => { this.items.set(s); this.loading.set(false); }, error: () => this.loading.set(false),
    });
  }

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.formError.set(null);
    this.api.post('/sample-tracking', { ...this.form.getRawValue(), date: this.date }).subscribe({
      next: () => { this.busy.set(false); this.showForm.set(false); this.form.patchValue({ area: '', count: 1 }); this.load(); },
      error: (err) => { this.busy.set(false); this.formError.set(err?.error?.detail ?? 'Add failed.'); },
    });
  }

  advance(s: SampleTracking, step: 'Review' | 'Sort'): void {
    this.busy.set(true);
    this.api.post(`/sample-tracking/${s.id}/advance`, { step }).subscribe({
      next: () => { this.busy.set(false); this.load(); }, error: () => this.busy.set(false),
    });
  }
}
