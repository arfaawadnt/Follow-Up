import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';

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
      { path: 'labs', loadComponent: () => import('./features/labs/labs.component').then((m) => m.LabsComponent) },
      { path: 'labs/new', loadComponent: () => import('./features/labs/lab-create.component').then((m) => m.LabCreateComponent) },
      { path: 'labs/:id', loadComponent: () => import('./features/labs/lab-detail.component').then((m) => m.LabDetailComponent) },
      { path: 'reps', loadComponent: () => import('./features/reps/reps.component').then((m) => m.RepsComponent) },
      { path: 'daily', loadComponent: () => import('./features/daily/daily.component').then((m) => m.DailyComponent) },
      { path: 'marketing', loadComponent: () => import('./features/marketing/marketing.component').then((m) => m.MarketingComponent) },
      { path: 'complaints', loadComponent: () => import('./features/complaints/complaints.component').then((m) => m.ComplaintsComponent) },
      { path: 'reports', loadComponent: () => import('./features/reports/reports.component').then((m) => m.ReportsComponent) },
      { path: 'notifications', loadComponent: () => import('./features/notifications/notifications.component').then((m) => m.NotificationsComponent) },
      { path: 'users', loadComponent: () => import('./features/users/users.component').then((m) => m.UsersComponent) },
      { path: 'setup', loadComponent: () => import('./features/setup/setup.component').then((m) => m.SetupComponent) },
    ],
  },
  { path: '**', redirectTo: '' },
];
