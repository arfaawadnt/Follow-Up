import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { UiService } from '../../core/ui.service';
import { TranslatePipe } from '../../core/i18n';

/** Split-screen login replicating the reference platform (:5080) — brand panel + sign-in card. */
@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="login-container" [attr.dir]="ui.lang() === 'ar' ? 'rtl' : 'ltr'">
      <div class="login-left">
        <div class="login-logo-header-band">
          <img src="logo.png" alt="Follow-Up Logo" style="height:48px;width:auto;max-width:100%">
        </div>
        <div class="login-left-content">
          <h1 class="login-catchy-title">{{ 'connect_title' | t }}</h1>
          <div class="login-line"></div>
          <p class="login-catchy-desc">{{ 'desc_left' | t }}</p>
          <div class="login-features">
            <div class="login-feature-item">
              <div class="login-feature-icon-circle">🤝</div>
              <div class="login-feature-text-val">{{ 'feat1' | t }}</div>
            </div>
            <div class="login-feature-item">
              <div class="login-feature-icon-circle">📈</div>
              <div class="login-feature-text-val">{{ 'feat2' | t }}</div>
            </div>
            <div class="login-feature-item">
              <div class="login-feature-icon-circle">✅</div>
              <div class="login-feature-text-val">{{ 'feat3' | t }}</div>
            </div>
          </div>
        </div>
        <div style="font-size:11px;opacity:.6;text-align:start">&copy; {{ year }} Mega Laboratory. All rights reserved.</div>
      </div>

      <div class="login-right">
        <div style="position:absolute;top:20px;inset-inline-end:20px">
          <select class="lang-selector" [ngModel]="ui.lang()" (ngModelChange)="ui.lang.set($event)">
            <option value="en">English</option>
            <option value="ar">العربية</option>
          </select>
        </div>

        <form class="login-card" (ngSubmit)="submit()">
          <h2>{{ 'welcome' | t }}</h2>
          <p class="login-subtitle">{{ 'signin_desc' | t }}</p>

          <div class="login-input-wrapper">
            <span class="input-icon">👤</span>
            <input [(ngModel)]="username" name="username" autocomplete="username"
                   [attr.aria-label]="'username' | t" [placeholder]="'username' | t">
          </div>
          <div class="login-input-wrapper">
            <span class="input-icon">🔒</span>
            <input [type]="showPw() ? 'text' : 'password'" [(ngModel)]="password" name="password"
                   autocomplete="current-password" [attr.aria-label]="'password' | t" [placeholder]="'password' | t">
            <button type="button" class="password-toggle" [attr.aria-label]="'show_password' | t" (click)="showPw.set(!showPw())">👁️</button>
          </div>

          @if (error()) { <div class="err" style="margin:0 0 16px">{{ error() }}</div> }

          <button class="login-btn" type="submit" [disabled]="loading()">
            {{ loading() ? ('signing_in' | t : 'Signing in…') : ('signin' | t) }}
          </button>
        </form>

        <div class="login-right-footer"><div>🔒 {{ 'secure_platform' | t }}</div></div>
      </div>
    </div>
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly ui = inject(UiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPw = signal(false);
  readonly year = new Date().getFullYear();
  username = '';
  password = '';

  submit(): void {
    if (!this.username.trim() || !this.password) { this.error.set('Enter your username and password.'); return; }
    this.loading.set(true);
    this.error.set(null);
    this.auth.login(this.username.trim(), this.password).subscribe({
      next: () => {
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/dashboard';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(err?.error?.detail ?? err?.error?.title ?? 'Sign-in failed. Check your credentials.');
      },
    });
  }
}
