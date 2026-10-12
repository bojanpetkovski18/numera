import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
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

@Component({ template: '<h1>Page A</h1><button type="button">in page</button>' })
class PageA {}

@Component({ template: '<h1>Page B</h1>' })
class PageB {}

describe('Shell keyboard and focus', () => {
  let root: HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '', component: Shell, children: [{ path: 'a', component: PageA }, { path: 'b', component: PageB }] }])],
    });
    const harness = await RouterTestingHarness.create('/a');
    root = harness.fixture.nativeElement as HTMLElement;
    document.body.appendChild(root); // focus only works on attached elements
  });

  afterEach(() => root.remove());

  const settle = async () => {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  it('the first element is a skip link that moves focus to the main content without changing the URL', async () => {
    const skip = root.querySelector('a.skip-link') as HTMLAnchorElement;
    expect(root.querySelector('a')).toBe(skip);
    expect(skip.textContent).toBe('Skip to main content');

    skip.click();
    await settle();

    expect(document.activeElement?.id).toBe('main-content');
    expect(TestBed.inject(Router).url).toBe('/a');
  });

  it('moving to another page puts focus on its content; query-only changes leave focus alone', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/b');
    await settle();
    expect(document.activeElement?.id).toBe('main-content');

    const inPage = root.querySelector('nav a') as HTMLAnchorElement;
    inPage.focus();
    await router.navigateByUrl('/b?sort=region');
    await settle();
    expect(document.activeElement).toBe(inPage);
  });

  it('arriving in the shell from another page (signing in) puts focus on the content', async () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: PageB },
          { path: '', component: Shell, children: [{ path: 'a', component: PageA }] },
        ]),
      ],
    });
    root.remove();
    const harness = await RouterTestingHarness.create('/login');
    root = harness.fixture.nativeElement as HTMLElement;
    document.body.appendChild(root);

    await harness.navigateByUrl('/a');
    await settle();

    expect(document.activeElement?.id).toBe('main-content');
  });

  it('Escape closes the phone menu and returns focus to the menu button', async () => {
    const menu = root.querySelector('.menu-button') as HTMLButtonElement;
    menu.style.display = 'inline-block'; // jsdom ignores the phone-width media query that shows this button
    menu.click();
    await settle();
    expect(menu.getAttribute('aria-expanded')).toBe('true');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(menu.getAttribute('aria-expanded')).toBe('false');
    expect(root.querySelector('nav')?.classList.contains('open')).toBe(false);
    expect(document.activeElement).toBe(menu);
  });
});
