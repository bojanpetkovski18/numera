import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FilterStore } from '../../core/filters/filter.store';
import { RevenueDrivers } from './metrics.models';
import { SERVER_ERROR, dashboardTestProviders, pending, settle } from './testing';
import { WhatChanged } from './what-changed';

const URL = '/api/metrics/drivers';

const REGIONS: RevenueDrivers = {
  by: 'region',
  comparison: { mode: 'previous', from: '2025-01-29', to: '2025-02-28' },
  currentRevenue: 130,
  comparisonRevenue: 125,
  change: 5,
  relativeChange: 0.04,
  drivers: [
    { key: 'Europe', currentRevenue: 100, comparisonRevenue: 40, contribution: 60, shareOfChange: 12 },
    { key: 'North America', currentRevenue: 30, comparisonRevenue: 60, contribution: -30, shareOfChange: -6 },
    { key: 'Latin America', currentRevenue: 0, comparisonRevenue: 25, contribution: -25, shareOfChange: -5 },
  ],
};

describe('WhatChanged', () => {
  let fixture: ComponentFixture<WhatChanged>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const tabs = () => Array.from(el().querySelectorAll<HTMLButtonElement>('[role="tab"]'));

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [WhatChanged], providers: [...dashboardTestProviders(), provideRouter([{ path: 'orders', children: [] }])] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(FilterStore).update({ preset: 'custom', from: '2025-03-01', to: '2025-03-31', channels: ['Online Store'], compare: 'previous' });
    fixture = TestBed.createComponent(WhatChanged);
    await settle(fixture);
  });

  afterEach(() => http.verify());

  async function flush(body: RevenueDrivers = REGIONS): Promise<void> {
    pending(http, URL)[0].flush(body);
    await settle(fixture);
  }

  it('asks for the region drivers with the filter and the comparison, then explains the change', async () => {
    expect(el().textContent).toContain('Loading what changed');
    const [request] = pending(http, URL);
    expect(request.request.params.get('by')).toBe('region');
    expect(request.request.params.get('compare')).toBe('previous');
    expect(request.request.params.get('from')).toBe('2025-03-01');
    expect(request.request.params.getAll('channel')).toEqual(['Online Store']);
    request.flush(REGIONS);
    await settle(fixture);

    expect(el().querySelector('.summary')?.textContent).toBe(
      'Revenue rose $5.00 (+4.0%) vs the previous period. The biggest increase was in Europe (+$60.00).',
    );
    const rows = Array.from(el().querySelectorAll('.driver')).map((li) => [
      li.querySelector('.name')?.textContent?.replace(/\s+/g, ' ').trim(),
      li.querySelector('.amount')?.textContent?.trim(),
      li.querySelector('.amount')?.className.replace('amount', '').trim(),
      (li.querySelector('.bar') as HTMLElement).style.width,
      li.querySelector('.detail')?.textContent?.replace(/\s+/g, ' ').trim(),
    ]);
    expect(rows).toEqual([
      ['Europe: this period\'s orders', '+$60.00', 'up', '50%', '$100.00 vs $40.00 · Previous period orders for Europe'],
      ['North America: this period\'s orders', '-$30.00', 'down', '25%', '$30.00 vs $60.00 · Previous period orders for North America'],
      ['Latin America: this period\'s orders', '-$25.00', 'down', '20.85%', '$0.00 vs $25.00 · Previous period orders for Latin America'],
    ]);
  });

  it('links each group to its rows in this period and in the comparison period', async () => {
    await flush();

    const [current, comparison] = Array.from(el().querySelectorAll<HTMLAnchorElement>('.driver')[1].querySelectorAll('a'));
    expect(decodeURIComponent(current.getAttribute('href')!)).toBe(
      '/orders?from=2025-03-01&to=2025-03-31&region=North America&channel=Online Store&status=completed',
    );
    expect(decodeURIComponent(comparison.getAttribute('href')!)).toBe(
      '/orders?from=2025-01-29&to=2025-02-28&region=North America&channel=Online Store&status=completed',
    );
  });

  it('switches dimension by click and by arrow keys, as tabs', async () => {
    await flush();
    expect(tabs().map((t) => [t.textContent?.trim(), t.getAttribute('aria-selected'), t.tabIndex])).toEqual([
      ['Region', 'true', 0],
      ['Channel', 'false', -1],
      ['Category', 'false', -1],
    ]);

    tabs()[2].click();
    await settle(fixture);
    expect(pending(http, URL)[0].request.params.get('by')).toBe('category');

    tabs()[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await settle(fixture);
    expect(pending(http, URL)[0].request.params.get('by')).toBe('region'); // wraps around
    expect(tabs().map((t) => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
    expect(el().querySelector('[role="tabpanel"]')?.getAttribute('aria-labelledby')).toBe('changed-tab-region');
  });

  it('says when nothing changed and when there is no revenue at all', async () => {
    await flush({ ...REGIONS, change: 0, relativeChange: 0, drivers: [] });

    expect(el().querySelector('.summary')?.textContent).toBe('Revenue was unchanged vs the previous period.');
    expect(el().textContent).toContain('No completed revenue in either period.');
    expect(el().querySelector('.driver')).toBeNull();
  });

  it('shows an error with a working retry', async () => {
    pending(http, URL)[0].flush({ detail: 'Database unavailable.' }, SERVER_ERROR);
    await settle(fixture);

    const alert = el().querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('The change could not be explained. Database unavailable.');
    (alert?.querySelector('button') as HTMLButtonElement).click();
    await settle(fixture);
    await flush();
    expect(el().querySelectorAll('.driver').length).toBe(3);
  });
});
