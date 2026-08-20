import { AfterViewChecked, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet, Router } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { UiService } from '../core/ui.service';
import { RealtimeService } from '../core/realtime.service';
import { NotificationStore } from '../core/notification.store';
import { IconsService } from '../core/icons.service';
import { TranslatePipe } from '../core/i18n';

interface NavItem { id: string; key: string; icon: string; path: string; privilege?: string; }
interface NavGroup { titleKey: string; items: NavItem[]; }

/** App shell replicating the reference platform: header + grouped, collapsible sidebar (Lucide icons). */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <div id="app">
      <div class="app-header">
        <div class="header-brand">
          <button class="header-icon-btn btn-collapse-sidebar" (click)="collapsed.set(!collapsed())" title="Toggle Sidebar">
            <i data-lucide="menu"></i>
          </button>
          <div class="header-logo-container"><img src="logo.png" alt="Logo"></div>
          <span class="header-title">{{ 'laboratory_marketing_platform' | t }}</span>
        </div>
        <div class="header-actions">
          <button class="header-icon-btn" (click)="go('/dashboard')" [title]="'dashboard' | t"><i data-lucide="home"></i></button>
          <button class="header-icon-btn" (click)="go('/daily')" [title]="'daily' | t"><i data-lucide="check-square"></i></button>
          <button class="header-icon-btn" (click)="go('/notifications')" title="Notifications" style="position:relative">
            <i data-lucide="bell"></i>
            @if (notes.unread() > 0) { <span class="header-badge-dot"></span> }
          </button>
          <div class="header-lang-toggle">
            <span class="header-lang-item" [class.active]="ui.lang() === 'en'" (click)="ui.lang.set('en')">EN</span>
            <span class="header-lang-item" [class.active]="ui.lang() === 'ar'" (click)="ui.lang.set('ar')">العربية</span>
          </div>
          <button class="header-icon-btn" (click)="ui.toggleTheme()" title="Toggle Dark/Light Mode">
            <i [attr.data-lucide]="ui.theme() === 'dark' ? 'sun' : 'moon'"></i>
          </button>
          <div class="header-avatar-circle" [title]="auth.username()">{{ initials() }}</div>
          <span class="header-username-text">{{ auth.username() }}</span>
          <button class="header-icon-btn" (click)="changePassword()" [title]="'change_password' | t"><i data-lucide="key"></i></button>
          <button class="header-icon-btn" (click)="logout()" [title]="'signout' | t"><i data-lucide="log-out"></i></button>
        </div>
      </div>

      <div class="app-body">
        <nav class="side" [class.collapsed]="collapsed()" aria-label="Main navigation">
          <div id="nav">
            @for (g of groups; track g.titleKey; let gi = $index) {
              @if (visibleItems(g).length) {
                <div class="sidebar-group-header" [class.collapsed]="isGroupCollapsed(gi)" (click)="toggleGroup(gi)">
                  <span>{{ g.titleKey | t }}</span>
                  <i data-lucide="chevron-down"></i>
                </div>
                <div class="sidebar-group-content" [class.collapsed]="isGroupCollapsed(gi)"
                     [style.max-height]="isGroupCollapsed(gi) ? '0px' : (visibleItems(g).length * 44) + 'px'">
                  @for (item of visibleItems(g); track item.id) {
                    <a class="nav-item" [routerLink]="item.path" routerLinkActive="on"
                       [routerLinkActiveOptions]="{ exact: item.path === '/dashboard' }">
                      <i [attr.data-lucide]="item.icon" class="nav-icon"></i>
                      <span class="nav-label" style="margin-inline-start:10px;margin-inline-end:10px">{{ item.key | t }}</span>
                    </a>
                  }
                </div>
              }
            }
          </div>
        </nav>
        <main class="main" [class.collapsed]="collapsed()" id="main" tabindex="-1"><router-outlet /></main>
      </div>
    </div>
  `,
})
export class ShellComponent implements AfterViewChecked, OnDestroy {
  readonly auth = inject(AuthService);
  readonly ui = inject(UiService);
  readonly rt = inject(RealtimeService);
  readonly notes = inject(NotificationStore);
  private readonly icons = inject(IconsService);
  private readonly router = inject(Router);

  readonly collapsed = signal(false);
  private readonly collapsedGroups = signal<Record<number, boolean>>({ 3: true, 4: true });

  readonly groups: NavGroup[] = [
    { titleKey: 'core_operations', items: [
      { id: 'dashboard', key: 'dashboard', icon: 'layout-dashboard', path: '/dashboard' },
      { id: 'daily', key: 'daily', icon: 'check-square', path: '/daily', privilege: 'ViewDailyFollowup' },
      { id: 'transfers', key: 'transfers', icon: 'truck', path: '/transfers', privilege: 'ViewTransfers' },
      { id: 'labcheckin', key: 'labcheckin', icon: 'check-circle', path: '/labcheckin', privilege: 'ConfirmTransfers' },
      { id: 'sample_tracking', key: 'sample_tracking', icon: 'milestone', path: '/sampletracking', privilege: 'SampleTracking' },
      { id: 'outsource_samples', key: 'outsource_samples', icon: 'external-link', path: '/outsource-samples', privilege: 'OutsourceSamples' },
    ]},
    { titleKey: 'statistics', items: [
      { id: 'labstats', key: 'labstats', icon: 'bar-chart-2', path: '/labstats', privilege: 'ViewLabStats' },
      { id: 'teststats', key: 'teststats', icon: 'bar-chart-3', path: '/test-statistics', privilege: 'ViewTeststats' },
      { id: 'reports', key: 'reports', icon: 'trending-up', path: '/reports', privilege: 'ViewReports' },
      { id: 'rep_intervals', key: 'rep_intervals', icon: 'clock', path: '/rep-intervals', privilege: 'ViewReports' },
    ]},
    { titleKey: 'field_and_marketing', items: [
      { id: 'marketing', key: 'marketing', icon: 'map-pin', path: '/marketing', privilege: 'ViewMarketing' },
      { id: 'complaints', key: 'complaints', icon: 'alert-circle', path: '/complaints', privilege: 'ViewComplaints' },
    ]},
    { titleKey: 'b2b_network', items: [
      { id: 'labs', key: 'labs', icon: 'flask-conical', path: '/labs' },
      { id: 'reps', key: 'reps', icon: 'users', path: '/reps', privilege: 'ViewReps' },
      { id: 'groups', key: 'groups', icon: 'folder-tree', path: '/test-groups', privilege: 'ViewTeststats' },
      { id: 'testsetup', key: 'testsetup', icon: 'flask-conical', path: '/test-setups', privilege: 'ViewTeststats' },
      { id: 'loyalty', key: 'loyalty', icon: 'award', path: '/loyalty', privilege: 'ManageLoyalty' },
      { id: 'commissions', key: 'commissions', icon: 'dollar-sign', path: '/commissions', privilege: 'ManageCommissions' },
    ]},
    { titleKey: 'system_and_admin', items: [
      { id: 'users', key: 'users', icon: 'settings', path: '/users', privilege: 'ManageUsers' },
      { id: 'roles', key: 'roles', icon: 'shield', path: '/roles', privilege: 'ManageUsers' },
      { id: 'setup', key: 'setup', icon: 'sliders', path: '/setup', privilege: 'SetupRefs' },
      { id: 'integration', key: 'oracle_integration', icon: 'database', path: '/integration', privilege: 'OracleIntegration' },
      { id: 'notifications', key: 'notifications_mgmt', icon: 'bell', path: '/notifications' },
      { id: 'sessions', key: 'active_sessions', icon: 'activity', path: '/sessions' },
      { id: 'audit', key: 'audit_trail', icon: 'clipboard-list', path: '/audit', privilege: 'ManageUsers' },
    ]},
  ];

  readonly initials = computed(() => {
    const n = this.auth.username().trim();
    if (!n) return '?';
    const parts = n.split(/\s+/);
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || n[0].toUpperCase();
  });

  constructor() { void this.rt.start(); }

  ngAfterViewChecked(): void { this.icons.render(); }

  visibleItems(g: NavGroup): NavItem[] {
    return g.items.filter((i) => !i.privilege || this.auth.has(i.privilege));
  }
  isGroupCollapsed(idx: number): boolean { return !!this.collapsedGroups()[idx]; }
  toggleGroup(idx: number): void { this.collapsedGroups.update((c) => ({ ...c, [idx]: !c[idx] })); }

  go(path: string): void { void this.router.navigateByUrl(path); }

  changePassword(): void {
    const oldPassword = window.prompt('Current password:');
    if (!oldPassword) return;
    const newPassword = window.prompt('New password (min 8 chars):');
    if (!newPassword) return;
    this.auth.changePassword(oldPassword, newPassword).subscribe({
      next: () => window.alert('Password changed.'),
      error: (e) => window.alert(e?.error?.detail ?? 'Change failed.'),
    });
  }

  logout(): void { this.auth.logout(); void this.router.navigateByUrl('/login'); }

  ngOnDestroy(): void { void this.rt.stop(); }
}
