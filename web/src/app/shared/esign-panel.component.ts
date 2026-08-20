import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

interface SignatureVerification {
  signed: boolean; stillValid: boolean; signerUsername: string | null;
  meaning: string | null; signedAt: string | null; signedVersion: number | null;
}

const MEANINGS = ['Authorship', 'Review', 'Approval', 'Verification', 'Execution'];

/**
 * Reusable electronic-signature panel (21 CFR Part 11 style): shows the current signature status for a
 * (module, recordId) and lets an authenticated user apply a signature by re-entering their password.
 * The password is sent only to the sign endpoint, which re-authenticates server-side.
 */
@Component({
  selector: 'app-esign-panel',
  standalone: true,
  imports: [FormsModule, DatePipe],
  template: `
    <div class="esign">
      @if (loading()) { <span class="muted">Checking signature…</span> }
      @if (!loading() && status(); as s) {
        @if (s.signed) {
          <div class="status" [class.invalid]="!s.stillValid">
            <span class="ico">{{ s.stillValid ? '✓' : '⚠' }}</span>
            <span>
              <strong>{{ s.meaning }}</strong> by {{ s.signerUsername }} · {{ s.signedAt | date:'short' }}
              @if (!s.stillValid) { <em class="warn"> — record changed since signing (v{{ s.signedVersion }})</em> }
            </span>
          </div>
        } @else { <span class="muted">Not signed.</span> }
      }

      @if (!signing()) {
        <button class="btn btn-mini btn-s" (click)="signing.set(true)">{{ status()?.signed ? 'Re-sign' : 'Sign' }}</button>
      } @else {
        <div class="signform">
          @if (error()) { <div class="inline-banner inline-banner-error">{{ error() }}</div> }
          <select [(ngModel)]="meaning" class="in">@for (m of meanings; track m) { <option>{{ m }}</option> }</select>
          <input class="in" placeholder="Reason (optional)" [(ngModel)]="reason">
          <input class="in" type="password" placeholder="Your password" [(ngModel)]="password" autocomplete="off">
          <button class="btn btn-mini btn-p" [disabled]="!password || busy()" (click)="sign()">Confirm</button>
          <button class="btn btn-mini btn-s" [disabled]="busy()" (click)="cancel()">Cancel</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .esign { display:flex; gap:10px; align-items:center; flex-wrap:wrap; }
    .muted { color:var(--slate-500); font-size:12px; }
    .status { display:flex; gap:6px; align-items:center; font-size:12px; color:var(--slate-700); }
    .status .ico { color:#166534; font-weight:700; } .status.invalid .ico { color:#b45309; }
    .warn { color:#b45309; font-style:normal; }
    .signform { display:flex; gap:6px; align-items:center; flex-wrap:wrap; }
    .in { border:1px solid var(--slate-300); border-radius:var(--r-input); padding:5px 8px; font-size:12px; background:var(--white); color:var(--slate-900); }
    .btn-mini { padding:4px 9px; font-size:11px; border-radius:var(--r-btn); }
    .inline-banner { flex-basis:100%; }
  `],
})
export class EsignPanelComponent implements OnInit {
  @Input({ required: true }) module = '';
  @Input({ required: true }) recordId = '';

  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly signing = signal(false);
  readonly status = signal<SignatureVerification | null>(null);
  readonly error = signal<string | null>(null);
  readonly meanings = MEANINGS;

  meaning = MEANINGS[2]; // Approval
  reason = '';
  password = '';

  ngOnInit(): void { this.verify(); }

  verify(): void {
    this.loading.set(true);
    this.api.get<SignatureVerification>(`/esign/${this.module}/${this.recordId}`).subscribe({
      next: (s) => { this.status.set(s); this.loading.set(false); },
      error: () => { this.loading.set(false); },
    });
  }

  sign(): void {
    if (!this.password) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.post('/esign/sign', {
      module: this.module, recordId: this.recordId, meaning: this.meaning,
      reason: this.reason || null, password: this.password,
    }).subscribe({
      next: () => { this.busy.set(false); this.cancel(); this.verify(); },
      error: (err) => { this.busy.set(false); this.error.set(err?.error?.detail ?? 'Signature rejected (check your password).'); },
    });
  }

  cancel(): void { this.signing.set(false); this.password = ''; this.reason = ''; this.error.set(null); }
}
