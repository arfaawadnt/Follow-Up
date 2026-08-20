import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { NotificationStore } from '../../core/notification.store';
import { NotificationItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

interface Preference { eventKey: string; system: boolean; mail: boolean; whatsApp: boolean; }
interface Gateway { name: string; enabled: boolean; maskedSecret: string; }
interface DeliveryLog { id: string; channel: string; recipient: string; eventKey: string; status: string; attempts: number; lastError: string | null; }
type Tab = 'feed' | 'preferences' | 'gateways' | 'logs';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [DatePipe, TranslatePipe],
  template: `
    <div class="pagehead"><div><div class="breadcrumbs">Home / {{ 'notifications_mgmt' | t : 'Notifications' }}</div><h1>{{ 'notifications_mgmt' | t : 'Notifications' }}</h1></div></div>

    <div class="tabs" style="display:flex;gap:6px;margin-bottom:14px">
      <button class="tab" [class.on]="tab() === 'feed'" (click)="tab.set('feed')">{{ 'notifications' | t : 'Feed' }}</button>
      <button class="tab" [class.on]="tab() === 'preferences'" (click)="setTab('preferences')">{{ 'preferences' | t : 'Preferences' }}</button>
      <button class="tab" [class.on]="tab() === 'gateways'" (click)="setTab('gateways')">{{ 'gateways' | t : 'Gateways' }}</button>
      <button class="tab" [class.on]="tab() === 'logs'" (click)="setTab('logs')">{{ 'notification_logs' | t : 'Delivery logs' }}</button>
    </div>

    @if (tab() === 'feed') {
      <div class="card"><div class="cbody" style="padding:0">
        <div style="display:flex;justify-content:flex-end;padding:10px 14px;border-bottom:1px solid var(--slate-150)"><button class="btn btn-s" (click)="markAll()">Mark all read</button></div>
        @for (n of feed(); track n.id) {
          <div class="note" [class.unread]="!n.isRead" (click)="read(n)"><div class="t">{{ n.title }}</div><div class="b">{{ n.body }}</div><div class="m mono">{{ n.createdAt | date:'short' }}</div></div>
        } @empty { <p class="empty" style="padding:20px">{{ 'common.empty' | t : 'Nothing to show.' }}</p> }
      </div></div>
    }

    @if (tab() === 'preferences') {
      <div class="card" style="padding:0;overflow:hidden"><table class="grid-table" style="margin:0;border:none">
        <thead><tr><th>Event</th><th style="text-align:center">System</th><th style="text-align:center">Mail</th><th style="text-align:center">WhatsApp</th></tr></thead>
        <tbody>
          @for (p of prefs(); track p.eventKey) {
            <tr><td class="mono small">{{ p.eventKey }}</td>
              <td style="text-align:center"><input type="checkbox" [checked]="p.system" (change)="savePref(p, 'system', $event)"></td>
              <td style="text-align:center"><input type="checkbox" [checked]="p.mail" (change)="savePref(p, 'mail', $event)"></td>
              <td style="text-align:center"><input type="checkbox" [checked]="p.whatsApp" (change)="savePref(p, 'whatsApp', $event)"></td></tr>
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
    .note{padding:12px 14px;border-bottom:1px solid var(--slate-150);cursor:pointer}.note.unread{background:var(--primary-blue-light)}
    .note .t{font-weight:600;font-size:13px}.note .b{color:var(--slate-700);font-size:12.5px}.note .m{color:var(--slate-500);font-size:11px;margin-top:2px}.empty{color:var(--slate-500)}
  `],
})
export class NotificationsComponent {
  private readonly api = inject(ApiService);
  private readonly store = inject(NotificationStore);
  readonly tab = signal<Tab>('feed');
  readonly feed = signal<NotificationItem[]>([]);
  readonly prefs = signal<Preference[]>([]);
  readonly gateways = signal<Gateway[]>([]);
  readonly logs = signal<DeliveryLog[]>([]);

  constructor() { this.loadFeed(); }

  setTab(t: Tab): void {
    this.tab.set(t);
    if (t === 'preferences' && !this.prefs().length) this.api.get<Preference[]>('/notifications/preferences').subscribe({ next: (r) => this.prefs.set(r) });
    if (t === 'gateways' && !this.gateways().length) this.api.get<Gateway[]>('/notifications/gateways').subscribe({ next: (r) => this.gateways.set(r) });
    if (t === 'logs') this.api.get<DeliveryLog[]>('/notifications/logs').subscribe({ next: (r) => this.logs.set(r) });
  }
  loadFeed(): void { this.api.get<NotificationItem[]>('/notifications').subscribe({ next: (n) => { this.feed.set(n); this.store.refresh(); } }); }
  read(n: NotificationItem): void { if (!n.isRead) this.api.post(`/notifications/${n.id}/read`, {}).subscribe({ next: () => this.loadFeed() }); }
  markAll(): void { this.api.post('/notifications/read-all', {}).subscribe({ next: () => this.loadFeed() }); }
  savePref(p: Preference, field: 'system' | 'mail' | 'whatsApp', e: Event): void {
    const val = (e.target as HTMLInputElement).checked;
    const body = { eventKey: p.eventKey, system: p.system, mail: p.mail, whatsApp: p.whatsApp, [field]: val };
    this.api.put('/notifications/preferences', body).subscribe({ next: () => { (p as unknown as Record<string, boolean>)[field] = val; } });
  }
  retry(l: DeliveryLog): void { this.api.post(`/notifications/logs/${l.id}/retry`, {}).subscribe({ next: () => this.setTab('logs') }); }
}
