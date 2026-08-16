import { Component, OnDestroy, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { UiService } from '../core/ui.service';
import { RealtimeService } from '../core/realtime.service';
import { NotificationStore } from '../core/notification.store';
import { TranslatePipe } from '../core/i18n';

interface NavItem { key: string; path: string; privilege?: string; }

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <header class="appbar">
      <span class="logo">FU</span>
      <span class="ttl">{{ 'app.title' | t }}</span>
      <span class="spacer"></span>
      @if (rt.connected()) { <span class="live" title="Real-time connected">●</span> }
      <button class="tbtn" (click)="ui.toggleLang()">{{ ui.lang() === 'en' ? 'العربية' : 'English' }}</button>
      <button class="tbtn" (click)="ui.toggleTheme()">◐ {{ ui.theme() === 'light' ? 'Dark' : 'Light' }}</button>
      <span class="user">{{ auth.username() }} · {{ auth.roleName() }}</span>
      <button class="tbtn" (click)="auth.logout()">{{ 'action.signout' | t }}</button>
    </header>

    <div class="body">
      <nav class="rail">
        @for (item of visibleNav(); track item.path) {
          <a class="nav-item" [routerLink]="item.path" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: item.path === '/dashboard' }">
            <span class="dot"></span>{{ item.key | t }}
            @if (item.path === '/notifications' && notes.unread() > 0) {
              <span class="badge-count">{{ notes.unread() > 99 ? '99+' : notes.unread() }}</span>
            }
          </a>
        }
      </nav>
      <main class="content"><router-outlet /></main>
    </div>
  `,
  styles: [`
    .appbar { position: sticky; top: 0; z-index: 60; height: var(--header-h); display: flex; align-items: center; gap: 16px;
      padding: 0 22px; background: var(--primary-blue); color: #fff; box-shadow: var(--shadow-sm); }
    .logo { background: #fff; color: var(--primary-blue); border-radius: var(--r-md); padding: 5px 11px; font: 800 14px var(--disp); letter-spacing: -.02em; }
    .ttl { font: 600 15px var(--disp); padding-inline-start: 16px; border-inline-start: 1px solid rgba(255,255,255,.3); }
    .spacer { flex: 1; }
    .live { color: #6ee7a8; font-size: 10px; }
    .user { font-size: 12.5px; color: rgba(255,255,255,.9); }
    .tbtn { background: rgba(255,255,255,.16); border: 1px solid rgba(255,255,255,.25); color: #fff; border-radius: 20px;
      padding: 6px 14px; font: 600 12.5px var(--ui); cursor: pointer; }
    .tbtn:hover { background: rgba(255,255,255,.28); }
    .body { display: flex; min-height: calc(100vh - var(--header-h)); }
    .rail { width: var(--rail-w); background: var(--white); border-inline-end: 1px solid var(--slate-150); padding: 12px; }
    .nav-item { display: flex; align-items: center; gap: 12px; padding: 8px 12px; border-radius: var(--r-md); font: 500 13px var(--ui);
      color: var(--slate-700); margin-bottom: 2px; border-inline-start: 3px solid transparent; cursor: pointer; }
    .nav-item:hover { background: var(--slate-100); color: var(--slate-900); }
    .nav-item.on { background: var(--primary-blue-light); color: var(--primary-blue); font-weight: 600; border-inline-start-color: var(--primary-blue); }
    .nav-item .dot { width: 16px; height: 16px; border-radius: 4px; background: currentColor; opacity: .55; }
    .nav-item .badge-count { margin-inline-start: auto; background: var(--danger, #dc2626); color: #fff; font: 700 10.5px var(--ui);
      min-width: 18px; height: 18px; padding: 0 5px; border-radius: 9px; display: inline-flex; align-items: center; justify-content: center; }
    .content { flex: 1; padding: 24px 28px 60px; }
  `],
})
export class ShellComponent implements OnDestroy {
  readonly auth = inject(AuthService);
  readonly ui = inject(UiService);
  readonly rt = inject(RealtimeService);
  readonly notes = inject(NotificationStore);

  private readonly nav: NavItem[] = [
    { key: 'nav.dashboard', path: '/dashboard' },
    { key: 'nav.labs', path: '/labs' },
    { key: 'nav.reps', path: '/reps', privilege: 'ViewReps' },
    { key: 'nav.daily', path: '/daily', privilege: 'ViewDailyFollowup' },
    { key: 'nav.transfers', path: '/transfers', privilege: 'ViewTransfers' },
    { key: 'nav.labcheckin', path: '/labcheckin', privilege: 'ConfirmTransfers' },
    { key: 'nav.sampletracking', path: '/sampletracking', privilege: 'SampleTracking' },
    { key: 'nav.marketing', path: '/marketing', privilege: 'ViewMarketing' },
    { key: 'nav.complaints', path: '/complaints', privilege: 'ViewComplaints' },
    { key: 'nav.reports', path: '/reports', privilege: 'ViewReports' },
    { key: 'nav.notifications', path: '/notifications' },
    { key: 'nav.setup', path: '/setup', privilege: 'SetupRefs' },
    { key: 'nav.users', path: '/users', privilege: 'ManageUsers' },
  ];

  constructor() {
    void this.rt.start();
  }

  visibleNav(): NavItem[] {
    return this.nav.filter((n) => !n.privilege || this.auth.has(n.privilege));
  }

  ngOnDestroy(): void {
    void this.rt.stop();
  }
}
