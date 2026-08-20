import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

const ph = (titleKey: string) => ({
  loadComponent: () => import('./features/placeholder/placeholder.component').then((m) => m.PlaceholderComponent),
  data: { titleKey },
});

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: '',
    loadComponent: () => import('./layout/shell.component').then((m) => m.ShellComponent),
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent) },

      // Core operations
      { path: 'daily', loadComponent: () => import('./features/daily/daily.component').then((m) => m.DailyComponent) },
      { path: 'transfers', loadComponent: () => import('./features/transfers/transfers.component').then((m) => m.TransfersComponent) },
      { path: 'labcheckin', loadComponent: () => import('./features/labcheckin/labcheckin.component').then((m) => m.LabCheckInComponent) },
      { path: 'sampletracking', loadComponent: () => import('./features/sampletracking/sampletracking.component').then((m) => m.SampleTrackingComponent) },
      { path: 'outsource-samples', loadComponent: () => import('./features/outsource/outsource.component').then((m) => m.OutsourceComponent) },

      // Statistics
      { path: 'labstats', ...ph('labstats') },
      { path: 'test-statistics', ...ph('teststats') },
      { path: 'reports', loadComponent: () => import('./features/reports/reports.component').then((m) => m.ReportsComponent) },
      { path: 'rep-intervals', loadComponent: () => import('./features/repintervals/repintervals.component').then((m) => m.RepIntervalsComponent) },

      // Field & marketing
      { path: 'marketing', loadComponent: () => import('./features/marketing/marketing.component').then((m) => m.MarketingComponent) },
      { path: 'complaints', loadComponent: () => import('./features/complaints/complaints.component').then((m) => m.ComplaintsComponent) },

      // B2B network
      { path: 'labs', loadComponent: () => import('./features/labs/labs.component').then((m) => m.LabsComponent) },
      { path: 'labs/new', loadComponent: () => import('./features/labs/lab-create.component').then((m) => m.LabCreateComponent) },
      { path: 'labs/:id', loadComponent: () => import('./features/labs/lab-detail.component').then((m) => m.LabDetailComponent) },
      { path: 'reps', loadComponent: () => import('./features/reps/reps.component').then((m) => m.RepsComponent) },
      { path: 'test-groups', ...ph('groups') },
      { path: 'test-setups', ...ph('testsetup') },
      { path: 'loyalty', loadComponent: () => import('./features/loyalty/loyalty.component').then((m) => m.LoyaltyComponent) },
      { path: 'commissions', ...ph('commissions') },

      // System & admin
      { path: 'users', loadComponent: () => import('./features/users/users.component').then((m) => m.UsersComponent) },
      { path: 'roles', ...ph('roles') },
      { path: 'setup', loadComponent: () => import('./features/setup/setup.component').then((m) => m.SetupComponent) },
      { path: 'integration', loadComponent: () => import('./features/integration/integration.component').then((m) => m.IntegrationComponent) },
      { path: 'notifications', loadComponent: () => import('./features/notifications/notifications.component').then((m) => m.NotificationsComponent) },
      { path: 'sessions', ...ph('active_sessions') },
      { path: 'audit', ...ph('audit_trail') },
    ],
  },
  { path: '**', redirectTo: '' },
];
