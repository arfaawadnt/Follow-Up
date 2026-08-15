import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { UiService } from '../../core/ui.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="wrap">
      <form class="dcard panel" [formGroup]="form" (ngSubmit)="submit()">
        <div class="brand"><span class="logo">FU</span><b>Follow-Up</b><small>Laboratory Marketing Platform</small></div>

        @if (error()) { <div class="inline-banner inline-banner-error">{{ error() }}</div> }

        <div class="field">
          <label>Username <span class="req">*</span></label>
          <input formControlName="username" autocomplete="username" autofocus>
        </div>
        <div class="field">
          <label>Password <span class="req">*</span></label>
          <input type="password" formControlName="password" autocomplete="current-password">
        </div>

        <button class="btn btn-p" type="submit" [disabled]="form.invalid || loading()">
          {{ loading() ? 'Signing in…' : 'Sign in' }}
        </button>
        <button class="btn btn-s theme" type="button" (click)="ui.toggleTheme()">◐ Theme</button>
      </form>
    </div>
  `,
  styles: [`
    .wrap { min-height: 100vh; display: grid; place-items: center; background:
      radial-gradient(1000px 500px at 15% -10%, rgba(0,120,212,.10), transparent 60%), var(--canvas); }
    .panel { width: 360px; padding: 28px; }
    .brand { display: flex; flex-direction: column; align-items: center; gap: 4px; margin-bottom: 20px; }
    .brand .logo { background: var(--primary-blue); color: #fff; border-radius: var(--r-md); padding: 8px 14px; font: 800 18px var(--disp); }
    .brand b { font: 800 20px var(--disp); color: var(--slate-900); margin-top: 8px; }
    .brand small { color: var(--slate-500); }
    .field { max-width: none; }
    .btn-p { width: 100%; justify-content: center; margin-top: 6px; }
    .theme { width: 100%; justify-content: center; margin-top: 8px; }
  `],
})
export class LoginComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly ui = inject(UiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.group({
    username: this.fb.control('', Validators.required),
    password: this.fb.control('', Validators.required),
  });

  submit(): void {
    if (this.form.invalid) return;
    this.loading.set(true);
    this.error.set(null);
    const { username, password } = this.form.getRawValue();
    this.auth.login(username, password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/dashboard';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.detail ?? 'Sign-in failed. Check your credentials.');
      },
    });
  }
}
