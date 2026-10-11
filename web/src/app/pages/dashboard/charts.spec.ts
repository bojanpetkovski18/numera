import { Component } from '@angular/core';
import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterStore } from '../../core/filters/filter.store';
import { BreakdownChart } from './breakdown-chart';
import { RevenueChart } from './revenue-chart';
import { GROUPS, POINTS, SERVER_ERROR, createdCharts, dashboardTestProviders, pending, settle } from './testing';

describe('RevenueChart', () => {
  let fixture: ComponentFixture<RevenueChart>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const URL = '/api/metrics/timeseries';

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [RevenueChart], providers: dashboardTestProviders() });
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

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [RegionBreakdownHost], providers: dashboardTestProviders() });
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
});