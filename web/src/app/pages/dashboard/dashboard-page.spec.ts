import { Component } from '@angular/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { FilterStore } from '../../core/filters/filter.store';
import { DashboardPage } from './dashboard-page';
import { GROUPS, POINTS, SUMMARY, dashboardTestProviders, pending, settle } from './testing';

@Component({ imports: [RouterOutlet], template: '<router-outlet />' })
class Host {}

const OPTIONS = {
  regions: ['Asia-Pacific', 'Europe'],
  channels: ['Mobile App', 'Online Store'],
  categories: ['Blends', 'Equipment'],
  firstOrderDate: '2024-10-01',
  lastOrderDate: '2026-09-30',
};

describe('DashboardPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<Host>;
  const root = () => fixture.nativeElement as HTMLElement;

  async function open(url: string): Promise<void> {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [...dashboardTestProviders(), provideRouter([{ path: 'dashboard', component: DashboardPage }])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    await TestBed.inject(Router).navigateByUrl(url);
    await settleAll();
  }

  async function settleAll(): Promise<void> {
    await settle(fixture);
  }

  function flushAll(): void {
    for (const r of pending(http, '/api/filter-options')) r.flush(OPTIONS);
    for (const r of pending(http, '/api/metrics/summary')) r.flush(SUMMARY);
    for (const r of pending(http, '/api/metrics/timeseries')) r.flush({ granularity: r.request.params.get('granularity'), points: POINTS });
    for (const r of pending(http, '/api/metrics/breakdown')) r.flush({ by: r.request.params.get('by'), groups: GROUPS });
  }

  afterEach(() => {
    flushAll();
    http.verify();
  });

  it('restores the filter from the URL and sends it to every widget', async () => {
    await open('/dashboard?from=2025-01-01&to=2025-03-31&region=Europe&granularity=week');

    const store = TestBed.inject(FilterStore);
    expect(store.preset()).toBe('custom');
    expect(store.regions()).toEqual(['Europe']);

    // match() removes what it returns, so collect each list once.
    const summary = pending(http, '/api/metrics/summary');
    const series = pending(http, '/api/metrics/timeseries');
    const breakdowns = pending(http, '/api/metrics/breakdown');
    const metricRequests = [...summary, ...series, ...breakdowns];
    expect(metricRequests.length).toBe(4);
    for (const r of metricRequests) {
      expect(r.request.params.get('from')).toBe('2025-01-01');
      expect(r.request.params.get('to')).toBe('2025-03-31');
      expect(r.request.params.getAll('region')).toEqual(['Europe']);
    }
    expect(series[0].request.params.get('granularity')).toBe('week');
    expect(breakdowns.map((r) => r.request.params.get('by')).sort()).toEqual(['category', 'region']);
    summary[0].flush(SUMMARY);
    series[0].flush({ granularity: 'week', points: POINTS });
    for (const r of breakdowns) r.flush({ by: r.request.params.get('by'), groups: GROUPS });
  });

  it('writes filter changes back to the URL', async () => {
    await open('/dashboard');
    flushAll();
    await settleAll();

    TestBed.inject(FilterStore).update({ preset: 'ytd', channels: ['Mobile App'] });
    await settleAll();

    const url = TestBed.inject(Router).url;
    expect(url).toContain('preset=ytd');
    expect(url).toContain('channel=Mobile%20App');
  });

  it('changing a filter in the filter bar refetches every widget with the new value', async () => {
    await open('/dashboard');
    flushAll();
    await settleAll();

    const region = root().querySelector<HTMLSelectElement>('#f-regions')!;
    expect(Array.from(region.options).map((o) => o.textContent?.trim())).toEqual(['All regions', 'Asia-Pacific', 'Europe']);
    region.value = 'Asia-Pacific';
    region.dispatchEvent(new Event('change'));
    await settleAll();

    const requests = [
      ...pending(http, '/api/metrics/summary'),
      ...pending(http, '/api/metrics/timeseries'),
      ...pending(http, '/api/metrics/breakdown'),
    ];
    expect(requests.length).toBe(4);
    expect(requests.every((r) => r.request.params.getAll('region')?.join() === 'Asia-Pacific')).toBe(true);
    expect(TestBed.inject(Router).url).toContain('region=Asia-Pacific');
  });

  it('the custom preset shows date inputs and reset clears every filter', async () => {
    await open('/dashboard?preset=last90&category=Blends');
    flushAll();
    await settleAll();

    const preset = root().querySelector<HTMLSelectElement>('#f-preset')!;
    preset.value = 'custom';
    preset.dispatchEvent(new Event('change'));
    await settleAll();
    flushAll();
    await settleAll();

    expect(root().querySelector<HTMLInputElement>('#f-from')?.value).toBe('2026-07-14');
    expect(root().querySelector<HTMLInputElement>('#f-to')?.value).toBe('2026-10-11');

    root().querySelector<HTMLButtonElement>('.actions button')!.click();
    await settleAll();

    const store = TestBed.inject(FilterStore);
    expect(store.preset()).toBe('all');
    expect(store.categories()).toEqual([]);
    expect(TestBed.inject(Router).url).toBe('/dashboard');
  });
});