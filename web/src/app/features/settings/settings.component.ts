import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { RetentionDto, SettingDto } from '../../core/models';

interface EditableSetting extends SettingDto { draft: string; dirty: boolean; }

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [FormsModule],
  template: `
    <h1 class="display page-title">Settings</h1>
    @if (banner()) { <div class="inline-banner" [class.inline-banner-error]="bannerError()">{{ banner() }}</div> }

    <div class="dcard"><div class="cbody">
      <h3 class="sec">Application settings</h3>
      @if (loading()) { <p class="muted">Loading…</p> }
      @if (!loading()) {
        <table class="app">
          <thead><tr><th>Key</th><th>Value</th><th></th></tr></thead>
          <tbody>
            @for (s of settings(); track s.key) {
              <tr>
                <td class="mono">{{ s.key }}@if (s.isSecret) { <span class="secret">secret</span> }</td>
                <td>
                  <input class="val" [type]="s.isSecret ? 'password' : 'text'" [(ngModel)]="s.draft" (ngModelChange)="s.dirty = true"
                         [placeholder]="s.isSecret ? '••••••••' : ''">
                </td>
                <td><button class="btn btn-mini btn-p" [disabled]="!s.dirty || busy()" (click)="save(s)">Save</button></td>
              </tr>
            } @empty { <tr><td colspan="3" class="muted">No settings.</td></tr> }
          </tbody>
        </table>
        <p class="hint">Secrets are write-only — the current value is never returned; leave blank to keep it unchanged.</p>
      }
    </div></div>

    <div class="dcard" style="margin-top:16px"><div class="cbody">
      <h3 class="sec">Data retention</h3>
      @if (retention(); as r) {
        <div class="retrow">
          <label>Retention window (days, min 30)</label>
          <input type="number" min="30" [(ngModel)]="retentionDays" class="daysin">
          <button class="btn btn-mini btn-p" [disabled]="busy()" (click)="saveRetention()">Save</button>
          <button class="btn btn-mini btn-s" [disabled]="busy()" (click)="runRetention()">Run purge now</button>
          <span class="state">{{ r.enabled ? 'Enabled' : 'Disabled' }}@if (r.days) { · currently {{ r.days }}d }</span>
        </div>
      }
    </div></div>
  `,
  styles: [`
    .page-title { font-size:22px; margin:0 0 16px; }
    .sec { font:700 12px var(--ui); text-transform:uppercase; letter-spacing:.04em; color:var(--slate-500); margin:0 0 12px; }
    .muted { color:var(--slate-500); font-size:12.5px; }
    .val { width:100%; max-width:360px; border:1px solid var(--slate-300); border-radius:var(--r-input); padding:6px 9px; font-size:12.5px; background:var(--white); color:var(--slate-900); }
    .secret { margin-inline-start:8px; font:600 10px var(--ui); color:#854d0e; background:#fef9c3; padding:1px 6px; border-radius:8px; text-transform:uppercase; }
    .btn-mini { padding:4px 10px; font-size:11.5px; border-radius:var(--r-btn); }
    .hint { color:var(--slate-500); font-size:11.5px; margin-top:10px; }
    .retrow { display:flex; gap:10px; align-items:center; flex-wrap:wrap; }
    .retrow label { font:600 12px var(--ui); color:var(--slate-600); }
    .daysin { width:90px; border:1px solid var(--slate-300); border-radius:var(--r-input); padding:6px 9px; background:var(--white); color:var(--slate-900); }
    .state { color:var(--slate-500); font-size:12px; }
  `],
})
export class SettingsComponent {
  private readonly api = inject(ApiService);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly settings = signal<EditableSetting[]>([]);
  readonly retention = signal<RetentionDto | null>(null);
  readonly banner = signal<string | null>(null);
  readonly bannerError = signal(false);
  retentionDays = 90;

  constructor() { this.load(); this.loadRetention(); }

  load(): void {
    this.loading.set(true);
    this.api.get<SettingDto[]>('/settings').subscribe({
      next: (s) => { this.settings.set(s.map((x) => ({ ...x, draft: x.isSecret ? '' : (x.value ?? ''), dirty: false }))); this.loading.set(false); },
      error: () => { this.loading.set(false); this.setBanner('Could not load settings.', true); },
    });
  }

  loadRetention(): void {
    this.api.get<RetentionDto>('/setup/retention').subscribe({
      next: (r) => { this.retention.set(r); if (r.days) this.retentionDays = r.days; },
    });
  }

  save(s: EditableSetting): void {
    this.busy.set(true);
    this.api.put(`/settings/${encodeURIComponent(s.key)}`, { value: s.draft, isSecret: s.isSecret }).subscribe({
      next: () => { this.busy.set(false); s.dirty = false; this.setBanner(`${s.key} saved.`, false); },
      error: (err) => { this.busy.set(false); this.setBanner(err?.error?.detail ?? 'Save failed.', true); },
    });
  }

  saveRetention(): void {
    this.busy.set(true);
    this.api.put('/setup/retention', { days: this.retentionDays }).subscribe({
      next: () => { this.busy.set(false); this.setBanner('Retention updated.', false); this.loadRetention(); },
      error: (err) => { this.busy.set(false); this.setBanner(err?.error?.detail ?? 'Update failed (min 30 days).', true); },
    });
  }

  runRetention(): void {
    this.busy.set(true);
    this.api.post('/setup/retention/run').subscribe({
      next: () => { this.busy.set(false); this.setBanner('Retention purge executed.', false); },
      error: (err) => { this.busy.set(false); this.setBanner(err?.error?.detail ?? 'Run failed.', true); },
    });
  }

  private setBanner(msg: string, error: boolean): void { this.banner.set(msg); this.bannerError.set(error); }
}
