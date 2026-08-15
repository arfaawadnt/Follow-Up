import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-lab-create',
  standalone: true,
  imports: [ReactiveFormsModule, TranslatePipe],
  template: `
    <h1 class="display page-title">{{ 'labs.new' | t }}</h1>
    <form class="dcard" [formGroup]="form" (ngSubmit)="submit()">
      <div class="cbody">
        @if (error()) { <div class="inline-banner inline-banner-error">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'labs.code' | t }} <span class="req">*</span></label><input formControlName="code"></div>
          <div class="field"><label>{{ 'labs.name' | t }} <span class="req">*</span></label><input formControlName="name"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'labs.segment' | t }}</label>
            <select formControlName="segment"><option>A</option><option>B</option><option>C</option></select></div>
          <div class="field"><label>{{ 'labs.governorate' | t }}</label><input formControlName="governorate"></div>
        </div>
        <div class="row">
          <div class="field"><label>Work days (comma)</label><input formControlName="workDays" placeholder="Sunday,Tuesday"></div>
          <div class="field"><label>Visit times (comma)</label><input formControlName="visitTimes" placeholder="09:00,14:30"></div>
        </div>
      </div>
      <div class="foot">
        <button class="btn btn-p" type="submit" [disabled]="form.invalid || busy()">{{ 'action.create' | t }}</button>
        <button class="btn btn-s" type="button" (click)="cancel()">{{ 'action.cancel' | t }}</button>
      </div>
    </form>
  `,
  styles: [`
    .page-title{font-size:22px;margin:0 0 16px}
    .row{display:flex;gap:16px;flex-wrap:wrap}.field{flex:1;min-width:220px;max-width:none}
    .foot{display:flex;gap:10px;justify-content:flex-end;padding:14px 18px;border-top:1px solid var(--slate-150);background:var(--filter-bg)}
  `],
})
export class LabCreateComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.group({
    code: this.fb.control('', Validators.required),
    name: this.fb.control('', Validators.required),
    segment: this.fb.control('C'),
    governorate: this.fb.control(''),
    workDays: this.fb.control(''),
    visitTimes: this.fb.control(''),
  });

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.error.set(null);
    const v = this.form.getRawValue();
    const split = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean);
    this.api.post<{ id: string }>('/labs', {
      code: v.code, name: v.name, segment: v.segment, governorate: v.governorate || null,
      workDays: split(v.workDays), visitTimes: split(v.visitTimes),
    }).subscribe({
      next: () => void this.router.navigate(['/labs']),
      error: (err) => { this.busy.set(false); this.error.set(err?.error?.detail ?? 'Create failed.'); },
    });
  }
  cancel(): void { void this.router.navigate(['/labs']); }
}
