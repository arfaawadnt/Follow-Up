import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { IconsService } from '../../core/icons.service';
import { TranslatePipe } from '../../core/i18n';

interface OracleConfig {
  enabled: boolean; intervalHours: number; allowListedQueries: string[];
  lastSyncAt: string | null; lastStatus: string | null;
}

@Component({
  selector: 'app-integration',
  standalone: true,
  imports: [FormsModule, DatePipe, TranslatePipe],
  template: `
    <div class="hrow" style="margin-bottom:20px">
      <div style="flex:1">
        <h2 style="font-size:20px;font-weight:700;color:var(--slate-900);margin:0;display:flex;align-items:center;gap:8px">
          <i data-lucide="database" style="width:24px;height:24px;color:var(--primary-blue)"></i>
          {{ 'oracle_database_integration' | t : 'Oracle Database Integration' }}
        </h2>
        <p class="muted small" style="margin:4px 0 0">{{ 'configure_connection_settings_custom_queri' | t : 'Configure the scheduled sync with the Oracle LIS.' }}</p>
      </div>
    </div>

    @if (banner()) { <div class="inline-banner" [class.inline-banner-error]="bannerError()">{{ banner() }}</div> }

    @if (c(); as c) {
      <div style="display:grid;grid-template-columns:3fr 2fr;gap:24px">
        <div class="card" style="padding:20px;display:flex;flex-direction:column;gap:16px">
          <h3 style="font-size:14px;font-weight:600;color:var(--slate-800);margin:0 0 8px;border-bottom:1px solid var(--slate-100);padding-bottom:8px">{{ 'integration_configuration' | t : 'Integration configuration' }}</h3>

          <div class="field" style="display:flex;align-items:center;gap:10px">
            <input type="checkbox" [(ngModel)]="enabled" style="width:18px;height:18px;cursor:pointer">
            <label style="font-weight:600;margin:0">{{ 'enable_automated_scheduled_sync' | t : 'Enable automated scheduled sync' }}</label>
          </div>

          <div class="field">
            <label style="font-weight:600;margin-bottom:6px;display:block;color:var(--slate-700)">{{ 'oracle_connection_string' | t : 'Oracle connection string' }}</label>
            <input class="input" disabled placeholder="{{ 'configured_on_the_server_not_displayed' | t : 'Configured on the server (never displayed)' }}"
                   style="width:100%;font-family:monospace;font-size:12px;background:var(--slate-100);color:var(--slate-500)">
          </div>

          <div class="field">
            <label style="font-weight:600;margin-bottom:6px;display:block;color:var(--slate-700)">{{ 'sync_interval_hours' | t : 'Sync interval (hours)' }}</label>
            <input class="input" type="number" min="1" [(ngModel)]="intervalHours" style="width:140px">
          </div>

          @if (auth.has('OracleIntegration')) {
            <div><button class="btn btn-p" [disabled]="busy()" (click)="save()">{{ 'save' | t : 'Save' }}</button></div>
          }
        </div>

        <div style="display:flex;flex-direction:column;gap:16px">
          <div class="card" style="padding:20px">
            <h3 style="font-size:14px;font-weight:600;color:var(--slate-800);margin:0 0 12px">{{ 'sync_status' | t : 'Sync status' }}</h3>
            <div class="hrow"><span class="muted small">{{ 'last_sync' | t : 'Last sync' }}</span><span class="mono" style="flex:1;text-align:end">{{ c.lastSyncAt ? (c.lastSyncAt | date:'short') : ('never_executed' | t : 'Never executed') }}</span></div>
            <div class="hrow"><span class="muted small">{{ 'status' | t }}</span><span style="flex:1;text-align:end"><span class="badge" [class]="statusClass(c.lastStatus)">{{ c.lastStatus ?? '—' }}</span></span></div>
            @if (auth.has('OracleIntegration')) {
              <button class="btn btn-s" style="margin-top:12px;width:100%" [disabled]="busy()" (click)="syncNow()"><i data-lucide="refresh-cw" style="width:14px;height:14px;margin-inline-end:6px"></i>{{ 'sync_now' | t : 'Sync now' }}</button>
            }
          </div>
          <div class="card" style="padding:20px">
            <h3 style="font-size:14px;font-weight:600;color:var(--slate-800);margin:0 0 12px">{{ 'allow_listed_queries' | t : 'Allow-listed queries' }}</h3>
            @if (c.allowListedQueries.length) {
              <ul style="margin:0;padding-inline-start:18px">@for (q of c.allowListedQueries; track q) { <li class="mono small" style="margin-bottom:4px">{{ q }}</li> }</ul>
            } @else { <p class="muted small" style="margin:0">—</p> }
          </div>
        </div>
      </div>
    }
  `,
})
export class IntegrationComponent {
  private readonly api = inject(ApiService);
  private readonly icons = inject(IconsService);
  readonly auth = inject(AuthService);
  readonly busy = signal(false);
  readonly c = signal<OracleConfig | null>(null);
  readonly banner = signal<string | null>(null);
  readonly bannerError = signal(false);
  enabled = false; intervalHours = 24;

  constructor() { this.load(); }

  load(): void {
    this.api.get<OracleConfig>('/integration/config').subscribe({
      next: (c) => { this.c.set(c); this.enabled = c.enabled; this.intervalHours = c.intervalHours; this.icons.render(); },
    });
  }
  statusClass(s: string | null): string { return s === 'Success' ? 'b-ok' : s === 'Failed' ? 'b-bad' : 'b-neu'; }

  save(): void {
    this.busy.set(true); this.banner.set(null);
    this.api.post('/integration/config', { enabled: this.enabled, intervalHours: this.intervalHours }).subscribe({
      next: () => { this.busy.set(false); this.set('Configuration saved.', false); this.load(); },
      error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Save failed.', true); },
    });
  }
  syncNow(): void {
    this.busy.set(true); this.banner.set(null);
    this.api.post('/integration/sync-now').subscribe({
      next: () => { this.busy.set(false); this.set('Sync triggered.', false); this.load(); },
      error: (e) => { this.busy.set(false); this.set(e?.error?.detail ?? 'Sync failed.', true); },
    });
  }
  private set(msg: string, err: boolean): void { this.banner.set(msg); this.bannerError.set(err); }
}
