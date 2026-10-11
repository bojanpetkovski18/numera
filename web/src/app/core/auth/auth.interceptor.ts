import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

const LOGIN_URL = '/api/auth/login';

/** Attaches the bearer token to API calls and sends the user back to login when the API says 401. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const isApiCall = request.url.startsWith('/api/');
  const token = isApiCall ? auth.token() : null;
  const outgoing = token === null ? request : request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        isApiCall &&
        request.url !== LOGIN_URL
      ) {
        auth.handleUnauthorized();
      }
      return throwError(() => error);
    }),
  );
};
