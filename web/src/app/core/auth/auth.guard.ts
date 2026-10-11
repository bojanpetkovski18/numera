import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Sends unauthenticated users to the login page, remembering the page they asked for. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.isSignedIn() || router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
