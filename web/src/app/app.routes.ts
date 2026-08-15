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
      { path: 'complaints', loadComponent: () => import('./features/complaints/complaints.component').then((m) => m.ComplaintsComponent) },
    ],
  },
  { path: '**', redirectTo: '' },
];
