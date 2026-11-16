import { Component } from '@angular/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { FilterStore } from '../../core/filters/filter.store';
import { COMPARED_POINTS, COMPARED_SUMMARY, GROUPS, dashboardTestProviders, pending, settle } from '../dashboard/testing';
import { SharedPage } from './shared-page';

@Component({ imports: [RouterOutlet], template: '<router-outlet />' })
class Host {}

const TOKEN = 'A'.repeat(43);
const BASE = `/api/shared/${TOKEN}`;

const INFO = {
  name: 'Europe Q3',
  preset: 'custom',
  from: '2026-07-01',
  to: '2026-09-30',
  regions: ['Europe'],
  channels: [],
  categories: [],
  granularity: 'week',
  comparison: { mode: 'previous', from: '2026-03-31', to: '2026-06-30' },
  expiresAt: '2026-11-23T10:00:00+00:00',
};

describe('SharedPage', () => {
  let fixture: ComponentFixture<Host>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  async function open(): Promise<void> {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [...dashboardTestProviders(), provideRouter([{ path: 'shared/:token', component: SharedPage }])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    await TestBed.inject(Router).navigateByUrl(`/shared/${TOKEN}`);
    await settle(fixture);
  }

  function flushWidgets(): void {
    for (const r of pending(http, `${BASE}/summary`)) r.flush(COMPARED_SUMMARY);
    for (const r of pending(http, `${BASE}/timeseries`)) {
      r.flush({ granularity: r.request.params.get('granularity'), points: COMPARED_POINTS, comparison: INFO.comparison });
    }
    for (const r of pending(http, `${BASE}/breakdown`)) r.flush({ by: r.request.params.get('by'), groups: GROUPS });
    for (const r of pending(http, `${BASE}/drivers`)) {
      r.flush({ by: 'region', comparison: INFO.comparison, currentRevenue: 125, comparisonRevenue: 70, change: 55, relativeChange: 0.7857, drivers: [
        { key: 'Europe', currentRevenue: 125, comparisonRevenue: 70, contribution: 55, shareOfChange: 1 },
      ] });
    }
  }

  afterEach(() => {
    flushWidgets();
    http.verify();
  });

  it('describes the view and shows its figures from the shared endpoints only', async () => {
    await open();
    expect(root().textContent).toContain('Loading the shared dashboard');

    http.expectOne(BASE).flush(INFO);
    await settle(fixture);
    await settle(fixture);

    expect(root().querySelector('h1')?.textContent).toBe('Europe Q3');
    expect(root().querySelector('.page-header p')?.textContent).toBe('Jul 1, 2026 to Sep 30, 2026 · Europe · compared with the previous period');
    expect(root().textContent).toContain('This link works until Nov 23, 2026.');
    // Its own filter store holds the view, so the widgets compare and group as the view does.
    const store = fixture.debugElement.query(By.directive(SharedPage)).injector.get(FilterStore);
    expect(store.filter()).toEqual(expect.objectContaining({ preset: 'custom', from: '2026-07-01', regions: ['Europe'], granularity: 'week', compare: 'previous' }));
    expect(pending(http, `${BASE}/timeseries`).at(-1)?.request.params.get('granularity')).toBe('week');
    expect(http.match((r) => r.url.startsWith('/api/metrics')).length).toBe(0);
  });

  it('is read-only: no links to the orders grid and no selectable charts', async () => {
    await open();
    http.expectOne(BASE).flush(INFO);
    await settle(fixture);
    await settle(fixture);
    flushWidgets();
    await settle(fixture);

    expect(root().querySelector('app-kpi-cards article')).not.toBeNull();
    expect(root().querySelector('app-what-changed .driver')).not.toBeNull();
    expect(root().querySelectorAll('a').length).toBe(0);
    expect(root().querySelector('canvas[tabindex]')).toBeNull();
    expect(root().textContent).not.toContain('to see its completed order lines');
  });

  it("keeps the visitor's own chart grouping instead of putting the view's back", async () => {
    await open();
    http.expectOne(BASE).flush(INFO);
    await settle(fixture);
    await settle(fixture);
    flushWidgets();
    await settle(fixture);

    Array.from(root().querySelectorAll<HTMLButtonElement>('app-revenue-chart button')).find((b) => b.textContent?.trim() === 'Month')!.click();
    await settle(fixture);
    await settle(fixture);

    const store = fixture.debugElement.query(By.directive(SharedPage)).injector.get(FilterStore);
    expect(store.granularity()).toBe('month');
    expect(pending(http, `${BASE}/timeseries`).map((r) => r.request.params.get('granularity'))).toEqual(['month']);
  });

  it('says plainly when the link no longer works', async () => {
    await open();

    http.expectOne(BASE).flush(
      { title: 'Link not available', detail: 'This link has expired, was revoked or does not exist.' },
      { status: 404, statusText: 'Not Found' },
    );
    await settle(fixture);

    expect(root().querySelector('h1')?.textContent).toBe('This link no longer works');
    expect(root().textContent).toContain('Ask the person who shared it for a new link.');
    expect(root().querySelector('app-kpi-cards')).toBeNull();
  });

  it('offers a retry when the server cannot be reached', async () => {
    await open();

    http.expectOne(BASE).flush({ detail: 'Too many requests.' }, { status: 429, statusText: 'Too Many Requests' });
    await settle(fixture);
    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Too many requests.');

    root().querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    await settle(fixture);
    http.expectOne(BASE).flush({ ...INFO, comparison: null, from: null, to: null, preset: 'all' });
    await settle(fixture);
    await settle(fixture);
    expect(root().querySelector('.page-header p')?.textContent).toBe('All data · Europe');
    expect(root().querySelector('app-what-changed')).toBeNull();
  });
});
