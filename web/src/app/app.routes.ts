import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { LoginPage } from './pages/login/login-page';
import { PlaceholderPage } from './pages/placeholder-page';
import { Shell } from './shell/shell';

export const routes: Routes = [
  { path: 'login', component: LoginPage, title: 'Sign in · Numera' },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () => import('./pages/dashboard/dashboard-page').then((m) => m.DashboardPage),
        title: 'Dashboard · Numera',
      },
      {
        path: 'orders',
        loadComponent: () => import('./pages/orders/orders-page').then((m) => m.OrdersPage),
        title: 'Orders · Numera',
      },
      {
        path: 'import',
        loadComponent: () => import('./pages/import/import-page').then((m) => m.ImportPage),
        title: 'Import · Numera',
      },
      {
        path: 'settings',
        component: PlaceholderPage,
        title: 'Settings · Numera',
        data: { heading: 'Settings', description: 'Demo data reset arrives in milestone M11.' },
      },
    ],
  },
  { path: '**', redirectTo: '' },
];

