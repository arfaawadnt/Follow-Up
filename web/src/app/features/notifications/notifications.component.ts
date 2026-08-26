import { Component, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { TranslatePipe } from '../../core/i18n';

interface Preference { eventKey: string; system: boolean; mail: boolean; whatsApp: boolean; }
interface Gateway { name: string; enabled: boolean; maskedSecret: string; }
interface DeliveryLog { id: string; channel: string; recipient: string; eventKey: string; status: string; attempts: number; lastError: string | null; }
type Tab = 'preferences' | 'gateways' | 'logs';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <div class="pagehead">
      <div><div class="breadcrumbs">Home / {{ 'notifications_control_panel' | t : 'Notifications Control Panel' }}</div><h1>{{ 'notifications_control_panel' | t : 'Notifications Control Panel' }}</h1></div>
      @if (tab() === 'preferences') { <div class="pagehead-actions"><button class="btn btn-p" [disabled]="!dirty().size || busy()" (click)="saveSettings()">{{ 'save_settings' | t : 'Save Settings' }}</button></div> }
    </div>

    <div class="tabs" style="display:flex;gap:6px;margin-bottom:14px">
      <button class="tab" [class.on]="tab() === 'preferences'" (click)="setTab('preferences')">{{ 'my_preferences' | t : 'My Preferences' }}</button>
      <button class="tab" [class.on]="tab() === 'gateways'" (click)="setTab('gateways')">{{ 'mail_and_whatsapp_gateways' | t : 'Mail & WhatsApp Gateways' }}</button>
      <button class="tab" [class.on]="tab() === 'logs'" (click)="setTab('logs')">{{ 'delivery_monitor' | t : 'Delivery Monitor' }}</button>
    </div>

    @if (tab() === 'preferences') {
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>{{ 'alert_type' | t : 'Alert Type' }}</th><th style="text-align:center">{{ 'in_app_alerts' | t : 'In-App Alerts' }}</th><th style="text-align:center">{{ 'email_alerts' | t : 'Email Alerts' }}</th><th style="text-align:center">{{ 'whatsapp_alerts' | t : 'WhatsApp Alerts' }}</th></tr></thead>
        <tbody>
          @for (p of prefs(); track p.eventKey) {
            <tr><td class="mono small">{{ p.eventKey }}</td>
              <td style="text-align:center"><input type="checkbox" [checked]="p.system" (change)="togglePref(p, 'system', $event)"></td>
              <td style="text-align:center"><input type="checkbox" [checked]="p.mail" (change)="togglePref(p, 'mail', $event)"></td>
              <td style="text-align:center"><input type="checkbox" [checked]="p.whatsApp" (change)="togglePref(p, 'whatsApp', $event)"></td></tr>
          } @empty { <tr><td colspan="4" class="empty" style="text-align:center;padding:24px">—</td></tr> }
        </tbody>
      </table></div>
    }

    @if (tab() === 'gateways') {
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>Gateway</th><th>Status</th><th>Secret</th></tr></thead>
        <tbody>@for (g of gateways(); track g.name) { <tr><td>{{ g.name }}</td><td><span class="badge" [class]="g.enabled?'b-ok':'b-neu'">{{ g.enabled ? 'Enabled' : 'Disabled' }}</span></td><td class="mono small">{{ g.maskedSecret }}</td></tr> } @empty { <tr><td colspan="3" class="empty" style="text-align:center;padding:24px">—</td></tr> }</tbody>
      </table></div>
    }

    @if (tab() === 'logs') {
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>Channel</th><th>Recipient</th><th>Event</th><th>Status</th><th>Attempts</th><th></th></tr></thead>
        <tbody>
          @for (l of logs(); track l.id) {
            <tr><td>{{ l.channel }}</td><td class="small">{{ l.recipient }}</td><td class="mono small">{{ l.eventKey }}</td>
              <td><span class="badge" [class]="l.status==='Sent'?'b-ok':l.status==='Failed'?'b-bad':'b-warn'">{{ l.status }}</span>@if (l.lastError) { <div class="small muted">{{ l.lastError }}</div> }</td>
              <td class="mono">{{ l.attempts }}</td>
              <td>@if (l.status === 'Failed') { <button class="btn btn-mini btn-s" (click)="retry(l)">Retry</button> }</td></tr>
          } @empty { <tr><td colspan="6" class="empty" style="text-align:center;padding:24px">—</td></tr> }
        </tbody>
      </table></div>
    }
  `,
  styles: [`
    .tab{background:var(--white);border:1px solid var(--slate-300);color:var(--slate-700);border-radius:var(--r-btn);padding:7px 16px;font:600 12.5px var(--ui);cursor:pointer}
    .tab.on{background:var(--primary-blue);color:#fff;border-color:var(--primary-blue)}
    .empty{color:var(--slate-500)}
  `],
})
export class NotificationsComponent {
  private readonly api = inject(ApiService);
  readonly tab = signal<Tab>('preferences');
  readonly prefs = signal<Preference[]>([]);
  readonly gateways = signal<Gateway[]>([]);
  readonly logs = signal<DeliveryLog[]>([]);
  readonly dirty = signal<Set<string>>(new Set());
  readonly busy = signal(false);

  constructor() { this.loadPrefs(); }

  setTab(t: Tab): void {
    this.tab.set(t);
    if (t === 'preferences' && !this.prefs().length) this.loadPrefs();
    if (t === 'gateways' && !this.gateways().length) this.api.get<Gateway[]>('/notifications/gateways').subscribe({ next: (r) => this.gateways.set(r) });
    if (t === 'logs') this.api.get<DeliveryLog[]>('/notifications/logs').subscribe({ next: (r) => this.logs.set(r) });
  }
  loadPrefs(): void { this.api.get<Preference[]>('/notifications/preferences').subscribe({ next: (r) => { this.prefs.set(r); this.dirty.set(new Set()); } }); }
  togglePref(p: Preference, field: 'system' | 'mail' | 'whatsApp', e: Event): void {
    const val = (e.target as HTMLInputElement).checked;
    this.prefs.update((list) => list.map((x) => x.eventKey === p.eventKey ? { ...x, [field]: val } : x));
    this.dirty.update((s) => new Set(s).add(p.eventKey));
  }
  saveSettings(): void {
    const changed = this.prefs().filter((p) => this.dirty().has(p.eventKey));
    if (!changed.length) return;
    this.busy.set(true);
    forkJoin(changed.map((p) => this.api.put('/notifications/preferences', { eventKey: p.eventKey, system: p.system, mail: p.mail, whatsApp: p.whatsApp })))
      .subscribe({ next: () => { this.dirty.set(new Set()); this.busy.set(false); }, error: () => this.busy.set(false) });
  }
  retry(l: DeliveryLog): void { this.api.post(`/notifications/logs/${l.id}/retry`, {}).subscribe({ next: () => this.setTab('logs') }); }
}
