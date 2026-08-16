import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../../core/api.service';
import { NotificationStore } from '../../core/notification.store';
import { NotificationItem } from '../../core/models';
import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [DatePipe, TranslatePipe],
  template: `
    <div class="head">
      <h1 class="display page-title">{{ 'notifications.title' | t }}</h1>
      <button class="btn btn-s" (click)="markAll()">Mark all read</button>
    </div>
    <div class="dcard"><div class="cbody">
      @if (loading()) { {{ 'common.loading' | t }} }
      @for (n of items(); track n.id) {
        <div class="note" [class.unread]="!n.isRead" (click)="read(n)">
          <div class="t">{{ n.title }}</div>
          <div class="b">{{ n.body }}</div>
          <div class="m mono">{{ n.createdAt | date:'short' }}</div>
        </div>
      } @empty { @if (!loading()) { <p class="empty">{{ 'common.empty' | t }}</p> } }
    </div></div>
  `,
  styles: [`
    .head{display:flex;justify-content:space-between;align-items:center;margin-bottom:16px}.page-title{font-size:22px;margin:0}
    .note{padding:12px 14px;border-bottom:1px solid var(--slate-150);cursor:pointer}
    .note.unread{background:var(--primary-blue-light)}
    .note .t{font-weight:600;font-size:13px}.note .b{color:var(--slate-700);font-size:12.5px}.note .m{color:var(--slate-500);font-size:11px;margin-top:2px}
    .empty{color:var(--slate-500)}
  `],
})
export class NotificationsComponent {
  private readonly api = inject(ApiService);
  private readonly store = inject(NotificationStore);
  readonly loading = signal(true);
  readonly items = signal<NotificationItem[]>([]);
  constructor() { this.load(); }
  load(): void {
    this.loading.set(true);
    this.api.get<NotificationItem[]>('/notifications').subscribe({
      next: (n) => { this.items.set(n); this.loading.set(false); this.store.refresh(); }, error: () => this.loading.set(false),
    });
  }
  read(n: NotificationItem): void { if (!n.isRead) this.api.post(`/notifications/${n.id}/read`).subscribe({ next: () => this.load() }); }
  markAll(): void { this.api.post('/notifications/read-all').subscribe({ next: () => this.load() }); }
}
