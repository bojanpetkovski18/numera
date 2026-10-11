import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { SESSION_STORAGE_KEY } from './auth.service';

function storeSession(expiresInMs: number): void {
  sessionStorage.setItem(
    SESSION_STORAGE_KEY,
    JSON.stringify({ accessToken: 'token', expiresAt: new Date(Date.now() + expiresInMs).toISOString() }),
  );
}

function runGuard(url: string): boolean | UrlTree {
  const state = { url } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => authGuard({} as ActivatedRouteSnapshot, state)) as boolean | UrlTree;
}

describe('authGuard', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  afterEach(() => sessionStorage.clear());

  it('redirects unauthenticated users to login and remembers the requested url', () => {
    const result = runGuard('/orders?region=Europe');

    expect(result).toBeInstanceOf(UrlTree);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Forders%3Fregion%3DEurope');
  });

  it('allows a user with an unexpired token', () => {
    storeSession(60_000);

    expect(runGuard('/dashboard')).toBe(true);
  });

  it('redirects when the stored token has expired', () => {
    storeSession(-1_000);

    expect(runGuard('/dashboard')).toBeInstanceOf(UrlTree);
  });
});