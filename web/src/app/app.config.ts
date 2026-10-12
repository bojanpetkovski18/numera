import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, ErrorHandler, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withInMemoryScrolling, withNavigationErrorHandler } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AppErrorHandler, GlobalErrors } from './core/errors/global-errors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: ErrorHandler, useClass: AppErrorHandler },
    provideRouter(
      routes,
      // A new page starts at the top (a drill-down from low on the dashboard must not open the grid mid-way down);
      // back/forward restores the position. In-page URL updates opt out (see syncFiltersWithUrl).
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
      // A page whose code fails to load (connection lost, new version deployed) gets the reload banner.
      withNavigationErrorHandler((error) => inject(GlobalErrors).report(error.error)),
    ),
    provideHttpClient(withInterceptors([authInterceptor])),
  ],
};
