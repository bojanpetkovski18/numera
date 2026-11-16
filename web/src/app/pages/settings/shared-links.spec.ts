import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SharedLinks } from './shared-links';

const LINK = { id: 'l1', viewId: 'v1', viewName: 'Europe Q3', createdAt: '2026-11-16T10:00:00Z', expiresAt: '2026-11-23T10:00:00Z' };

describe('SharedLinks', () => {
  let fixture: ComponentFixture<SharedLinks>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  async function render(): Promise<void> {
    TestBed.configureTestingModule({ imports: [SharedLinks], providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SharedLinks);
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('lists the active links and revokes one', async () => {
    await render();
    expect(el().textContent).toContain('Loading shared links');
    http.expectOne('/api/shares').flush([LINK]);
    await settle();

    const item = el().querySelector('li')!;
    expect(item.querySelector('.name')?.textContent).toBe('Europe Q3');
    expect(item.querySelector('.muted')?.textContent).toBe('Created Nov 16, 2026 · works until Nov 23, 2026');
    expect(item.querySelector('button')?.textContent).toBe('Revoke link to Europe Q3');

    el().querySelector<HTMLButtonElement>('li button')!.click();
    http.expectOne((r) => r.url === '/api/shares/l1' && r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(el().querySelector('li')).toBeNull();
    expect(el().textContent).toContain('No active links.');
    expect(el().querySelector('[role="status"]')?.textContent).toBe('The link to Europe Q3 no longer works.');
  });

  it('shows a retry when the links cannot be loaded', async () => {
    await render();
    http.expectOne('/api/shares').flush({}, { status: 500, statusText: 'Server Error' });
    await settle();

    el().querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    http.expectOne('/api/shares').flush([]);
    await settle();
    expect(el().textContent).toContain('No active links.');
  });
});
