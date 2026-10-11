import { Component } from '@angular/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { FilterStore } from '../../core/filters/filter.store';
import { BreakdownChart } from './breakdown-chart';
import { RevenueChart } from './revenue-chart';
import { GROUPS, POINTS, SERVER_ERROR, clickChartItem, createdCharts, dashboardTestProviders, highlighted, pending, settle } from './testing';

const ROUTES = [{ path: 'orders', children: [] }];

function key(target: Element, name: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
  target.dispatchEvent(event);
  return event;
}

describe('RevenueChart', () => {
  let fixture: ComponentFixture<RevenueChart>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const URL = '/api/metrics/timeseries';

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [RevenueChart], providers: [...dashboardTestProviders(), provideRouter(ROUTES)] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RevenueChart);
    await settle(fixture);
  });

  afterEach(() => http.verify());

  it('shows a skeleton while loading, then the chart and a data table', async () => {
    expect(el().querySelector('.skeleton')).not.toBeNull();

    const [request] = pending(http, URL);
    expect(request.request.params.get('granularity')).toBe('month');
    request.flush({ granularity: 'month', points: POINTS });
    await settle(fixture);

    expect(el().querySelector('canvas')?.getAttribute('aria-label')).toContain('Revenue by month from Jan 2025 to Feb 2025');
    expect(createdCharts.at(-1)?.data.labels).toEqual(['Jan 2025', 'Feb 2025']);
    const rows = Array.from(el().querySelectorAll('tbody tr')).map((r) =>
      Array.from(r.children).map((cell) => cell.textContent?.trim()),
    );
    expect(rows).toEqual([
      ['Jan 2025', '$95.00', '2'],
      ['Feb 2025', '$30.00', '1'],
    ]);
    expect(el().querySelector('.widget-subtitle')?.textContent).toBe('2 months · highest Jan 2025, $95.00');
  });

  it('shows an empty state when every period is zero', async () => {
    pending(http, URL)[0].flush({ granularity: 'month', points: [{ periodStart: '2025-01-01', revenue: 0, orders: 0 }] });
    await settle(fixture);

    expect(el().textContent).toContain('No data for these filters.');
    expect(el().querySelector('canvas')).toBeNull();
  });

  it('shows an error with retry', async () => {
    pending(http, URL)[0].flush({ detail: 'The range has more than 1,000 days.' }, { status: 400, statusText: 'Bad Request' });
    await settle(fixture);

    expect(el().querySelector('[role="alert"]')?.textContent).toContain('The range has more than 1,000 days.');
    (el().querySelector('[role="alert"] button') as HTMLButtonElement).click();
    await settle(fixture);
    pending(http, URL)[0].flush({ granularity: 'month', points: POINTS });
  });

  it('the day/week/month toggle requests the new granularity and marks the pressed button', async () => {
    pending(http, URL)[0].flush({ granularity: 'month', points: POINTS });
    await settle(fixture);

    const week = Array.from(el().querySelectorAll('.toggle button')).find((b) => b.textContent?.trim() === 'Week') as HTMLButtonElement;
    week.click();
    await settle(fixture);

    const [request] = pending(http, URL);
    expect(request.request.params.get('granularity')).toBe('week');
    expect(week.getAttribute('aria-pressed')).toBe('true');
    expect(TestBed.inject(FilterStore).granularity()).toBe('week');
    request.flush({ granularity: 'week', points: POINTS });
  });

  describe('drill-down', () => {
    async function load(granularity: string, points: { periodStart: string; revenue: number; orders: number }[]): Promise<void> {
      await settle(fixture);
      for (const r of pending(http, URL).filter((r) => !r.cancelled)) r.flush({ granularity, points });
      await settle(fixture);
    }

    it('clicking a week opens its orders Monday to Sunday, across the month end, keeping the other filters', async () => {
      TestBed.inject(FilterStore).update({ regions: ['Europe'], granularity: 'week' });
      await load('week', [
        { periodStart: '2025-03-24', revenue: 10, orders: 1 },
        { periodStart: '2025-03-31', revenue: 20, orders: 2 },
      ]);

      clickChartItem(1);
      await settle(fixture);

      expect(TestBed.inject(Router).url).toBe('/orders?from=2025-03-31&to=2025-04-06&region=Europe&status=completed');
    });

    it('the data table links each month to its orders, ending on the last day of the month', async () => {
      await load('month', [
        { periodStart: '2024-02-01', revenue: 10, orders: 1 },
        { periodStart: '2024-12-01', revenue: 20, orders: 2 },
      ]);

      const links = Array.from(el().querySelectorAll<HTMLAnchorElement>('tbody th a')).map((a) => a.getAttribute('href'));
      expect(links).toEqual([
        '/orders?from=2024-02-01&to=2024-02-29&status=completed',
        '/orders?from=2024-12-01&to=2024-12-31&status=completed',
      ]);
    });

    it('a preset range is clipped to the period: the first month of "last 30 days" starts 29 days ago', async () => {
      TestBed.inject(FilterStore).update({ preset: 'last30' }); // today is Oct 11, 2026 in tests
      await load('month', [
        { periodStart: '2026-09-01', revenue: 10, orders: 1 },
        { periodStart: '2026-10-01', revenue: 20, orders: 2 },
      ]);

      clickChartItem(0);
      await settle(fixture);

      expect(TestBed.inject(Router).url).toBe('/orders?from=2026-09-12&to=2026-09-30&status=completed');
    });

    it('works by keyboard: arrows move between periods with an announcement, Enter opens the orders', async () => {
      await load('day', [
        { periodStart: '2025-01-01', revenue: 95, orders: 2 },
        { periodStart: '2025-01-02', revenue: 30, orders: 1 },
      ]);
      const canvas = el().querySelector('canvas')!;
      expect(canvas.getAttribute('tabindex')).toBe('0');
      expect(document.getElementById(canvas.getAttribute('aria-describedby')!)?.textContent).toContain('arrow keys to choose a day');

      expect(key(canvas, 'ArrowRight').defaultPrevented).toBe(true);
      key(canvas, 'ArrowRight');
      key(canvas, 'ArrowRight'); // stays on the last one
      fixture.detectChanges();
      expect(highlighted).toEqual([0, 1]);
      expect(el().querySelector('[aria-live]')?.textContent).toContain('Jan 2, 2025: $30.00, 1 orders. Press Enter to open.');

      key(canvas, 'Enter');
      await settle(fixture);
      expect(TestBed.inject(Router).url).toBe('/orders?from=2025-01-02&to=2025-01-02&status=completed');
    });
  });
});

@Component({
  imports: [BreakdownChart],
  template: `<app-breakdown-chart by="region" title="Revenue by region" />`,
})
class RegionBreakdownHost {}

describe('BreakdownChart', () => {
  let fixture: ComponentFixture<RegionBreakdownHost>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const URL = '/api/metrics/breakdown';
  const REGIONS = [
    { key: 'Europe', revenue: 80, orders: 3 },
    { key: 'Latin America', revenue: 45, orders: 1 },
  ];

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [RegionBreakdownHost], providers: [...dashboardTestProviders(), provideRouter(ROUTES)] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RegionBreakdownHost);
    await settle(fixture);
  });

  afterEach(() => http.verify());

  it('requests its dimension with the shared filter and renders bars plus a table', async () => {
    expect(el().querySelector('.skeleton')).not.toBeNull();
    const [request] = pending(http, URL);
    expect(request.request.params.get('by')).toBe('region');
    request.flush({ by: 'region', groups: GROUPS });
    await settle(fixture);

    expect(el().querySelector('canvas')?.getAttribute('aria-label')).toBe('Revenue by region: Blends $80.00, Equipment $45.00.');
    expect(Array.from(el().querySelectorAll('tbody th')).map((th) => th.textContent)).toEqual(['Blends', 'Equipment']);
  });

  it('shows an empty state', async () => {
    pending(http, URL)[0].flush({ by: 'region', groups: [] });
    await settle(fixture);

    expect(el().textContent).toContain('No data for these filters.');
  });

  it('shows an error with retry', async () => {
    pending(http, URL)[0].flush({}, SERVER_ERROR);
    await settle(fixture);

    expect(el().querySelector('[role="alert"]')?.textContent).toContain('Revenue by region could not be loaded.');
    (el().querySelector('[role="alert"] button') as HTMLButtonElement).click();
    await settle(fixture);
    pending(http, URL)[0].flush({ by: 'region', groups: GROUPS });
  });

  it('refetches when the category filter changes', async () => {
    pending(http, URL)[0].flush({ by: 'region', groups: GROUPS });
    await settle(fixture);

    TestBed.inject(FilterStore).update({ categories: ['Blends'] });
    await settle(fixture);

    const [request] = pending(http, URL);
    expect(request.request.params.getAll('category')).toEqual(['Blends']);
    request.flush({ by: 'region', groups: GROUPS });
  });

  describe('drill-down', () => {
    beforeEach(async () => {
      TestBed.inject(FilterStore).update({ preset: 'ytd', categories: ['Blends'], channels: ['Wholesale'], granularity: 'week' });
      await settle(fixture);
      for (const r of pending(http, URL).filter((r) => !r.cancelled)) r.flush({ by: 'region', groups: REGIONS });
      await settle(fixture);
    });

    it('clicking a bar opens the orders for that region and keeps the date, channel and category filters', async () => {
      clickChartItem(1);
      await settle(fixture);

      expect(TestBed.inject(Router).url).toBe(
        '/orders?preset=ytd&region=Latin%20America&channel=Wholesale&category=Blends&status=completed',
      );
    });

    it('the data table links each region to the same drill-down', () => {
      expect(Array.from(el().querySelectorAll<HTMLAnchorElement>('tbody th a')).map((a) => a.getAttribute('href'))).toEqual([
        '/orders?preset=ytd&region=Europe&channel=Wholesale&category=Blends&status=completed',
        '/orders?preset=ytd&region=Latin%20America&channel=Wholesale&category=Blends&status=completed',
      ]);
    });

    it('works by keyboard: End selects the last bar, Space opens it, Escape clears the highlight', async () => {
      const canvas = el().querySelector('canvas')!;
      key(canvas, 'End');
      key(canvas, 'Escape');
      key(canvas, 'End');
      fixture.detectChanges();
      expect(highlighted).toEqual([1, null, 1]);
      expect(el().querySelector('[aria-live]')?.textContent).toContain('Latin America: $45.00, 1 orders');

      key(canvas, ' ');
      await settle(fixture);
      expect(TestBed.inject(Router).url).toContain('/orders?preset=ytd&region=Latin%20America');
    });

    it('Enter does nothing until an item has been chosen', async () => {
      key(el().querySelector('canvas')!, 'Enter');
      await settle(fixture);
      expect(TestBed.inject(Router).url).toBe('/');
    });
  });
});
