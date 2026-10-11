import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { ImportPage } from './pages/import/import-page';
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
        component: PlaceholderPage,
        title: 'Dashboard · Numera',
        data: { heading: 'Dashboard', description: 'KPIs, charts and the date filter arrive in milestone M07.' },
      },
      {
        path: 'orders',
        component: PlaceholderPage,
        title: 'Orders · Numera',
        data: { heading: 'Orders', description: 'The filterable orders grid arrives in milestone M08.' },
      },
      { path: 'import', component: ImportPage, title: 'Import · Numera' },
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

