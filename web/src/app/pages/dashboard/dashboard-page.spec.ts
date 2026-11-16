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
    for (const r of pending(http, '/api/metrics/drivers')) {
      r.flush({
        by: r.request.params.get('by'),
        comparison: { mode: r.request.params.get('compare'), from: '2026-04-14', to: '2026-07-13' },
        currentRevenue: 0,
        comparisonRevenue: 0,
        change: 0,
        relativeChange: null,
        drivers: [],
      });
    }
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

  it('selects restore values from the URL even though the options load afterwards', async () => {
    await open('/dashboard?region=Europe&channel=Mobile%20App&preset=last90');

    // filter-options is still pending here; flush it last
    for (const r of pending(http, '/api/metrics/summary')) r.flush(SUMMARY);
    await settleAll();
    for (const r of pending(http, '/api/filter-options')) r.flush(OPTIONS);
    await settleAll();

    expect(root().querySelector<HTMLSelectElement>('#f-regions')!.value).toBe('Europe');
    expect(root().querySelector<HTMLSelectElement>('#f-channels')!.value).toBe('Mobile App');
    expect(root().querySelector<HTMLSelectElement>('#f-categories')!.value).toBe('');
    expect(root().querySelector<HTMLSelectElement>('#f-preset')!.value).toBe('last90');
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

  it('"Compare to" is disabled with a reason for all time, and a choice reaches the URL, the KPIs and the chart', async () => {
    await open('/dashboard');
    flushAll();
    await settleAll();

    const compare = root().querySelector<HTMLSelectElement>('#f-compare')!;
    expect(compare.disabled).toBe(true);
    expect(compare.value).toBe('none');
    expect(compare.getAttribute('aria-describedby')).toBe('f-compare-hint');
    expect(root().querySelector('#f-compare-hint')?.textContent).toBe('Choose a date range with a start and an end to compare it.');

    TestBed.inject(FilterStore).update({ preset: 'last90' });
    await settleAll();
    flushAll();
    await settleAll();
    expect(compare.disabled).toBe(false);
    expect(root().querySelector('#f-compare-hint')).toBeNull();

    compare.value = 'previous';
    compare.dispatchEvent(new Event('change'));
    await settleAll();

    expect(TestBed.inject(Router).url).toContain('compare=previous');
    expect(pending(http, '/api/metrics/summary')[0].request.params.get('compare')).toBe('previous');
    expect(pending(http, '/api/metrics/timeseries')[0].request.params.get('compare')).toBe('previous');
    // The breakdown charts are not compared, so they do not reload.
    expect(pending(http, '/api/metrics/breakdown')).toEqual([]);
  });

  it('restores the comparison from the URL', async () => {
    await open('/dashboard?preset=ytd&compare=year');
    flushAll();
    await settleAll();

    expect(TestBed.inject(FilterStore).compare()).toBe('year');
    expect(root().querySelector<HTMLSelectElement>('#f-compare')!.value).toBe('year');
  });

  it('shows "What changed?" only while a comparison is active', async () => {
    await open('/dashboard?preset=ytd');
    flushAll();
    await settleAll();
    expect(root().querySelector('app-what-changed')).toBeNull();

    TestBed.inject(FilterStore).update({ compare: 'previous' });
    await settleAll();
    expect(root().querySelector('app-what-changed')).not.toBeNull();
    expect(pending(http, '/api/metrics/drivers').map((r) => r.request.params.get('by'))).toEqual(['region']);
    flushAll(); // answer the compared summary and chart before the next change cancels them
    await settleAll();

    TestBed.inject(FilterStore).update({ preset: 'all' }); // no longer comparable: the panel goes
    await settleAll();
    expect(root().querySelector('app-what-changed')).toBeNull();
  });
});
