import { Injectable, effect, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { RealtimeService } from './realtime.service';
import { NotificationItem } from './models';

/**
 * Shared unread-count store for the header badge. Refreshes on login and on every real-time hint
 * (RealtimeService.tick), so a fanned-out notification bumps the badge without a page reload.
 */
@Injectable({ providedIn: 'root' })
export class NotificationStore {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly rt = inject(RealtimeService);

  readonly unread = signal(0);

  constructor() {
    // Re-fetch whenever the real-time tick advances (or auth flips), but only while signed in.
    effect(() => {
      this.rt.tick();
      if (this.auth.isAuthenticated()) this.refresh();
      else this.unread.set(0);
    });
  }

  refresh(): void {
    this.api.get<NotificationItem[]>('/notifications', { unreadOnly: true }).subscribe({
      next: (n) => this.unread.set(n.length),
      error: () => {},
    });
  }
}
