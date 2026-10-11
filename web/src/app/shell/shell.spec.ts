import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../app.routes';
import { SESSION_STORAGE_KEY } from '../core/auth/auth.service';
import { Shell } from './shell';

describe('Shell', () => {
  beforeEach(() => {
    sessionStorage.setItem(
      SESSION_STORAGE_KEY,
      JSON.stringify({ accessToken: 't', expiresAt: new Date(Date.now() + 60_000).toISOString() }),
    );
    TestBed.configureTestingModule({ imports: [Shell], providers: [provideRouter(routes)] });
  });

  it('renders the primary navigation with all pages', async () => {
    const harness = await RouterTestingHarness.create('/dashboard');
    const links = Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll('nav[aria-label="Primary"] li a'),
    ).map((a) => a.textContent?.trim());

    expect(links).toEqual(['Dashboard', 'Orders', 'Import', 'Settings']);
  });

  it('navigates between placeholder pages and marks the active link', async () => {
    const harness = await RouterTestingHarness.create('/dashboard');
    const root = harness.routeNativeElement as HTMLElement;
    expect(root.querySelector('h1')?.textContent).toContain('Dashboard');

    await harness.navigateByUrl('/orders');
    expect(root.querySelector('h1')?.textContent).toContain('Orders');
    expect(root.querySelector('a.active')?.getAttribute('aria-current')).toBe('page');
  });
});
