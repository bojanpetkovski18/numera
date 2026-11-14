import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterStore } from '../../core/filters/filter.store';
import { KpiCards } from './kpi-cards';
import { COMPARED_SUMMARY, EMPTY_SUMMARY, SERVER_ERROR, SUMMARY, dashboardTestProviders, pending, settle } from './testing';

describe('KpiCards', () => {
  let fixture: ComponentFixture<KpiCards>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [KpiCards], providers: dashboardTestProviders() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(KpiCards);
    await settle(fixture);
  });

  afterEach(() => http.verify());

  it('shows skeletons while loading', () => {
    expect(el().querySelectorAll('.skeleton').length).toBeGreaterThan(0);
    expect(el().textContent).toContain('Loading key figures');
    pending(http, '/api/metrics/summary')[0].flush(SUMMARY);
  });

  it('shows the four KPIs, formatted', async () => {
    pending(http, '/api/metrics/summary')[0].flush(SUMMARY);
    await settle(fixture);

    const cards = Array.from(el().querySelectorAll('article')).map((a) => [
      a.querySelector('.label')?.textContent?.trim(),
      a.querySelector('.value')?.textContent?.trim(),
      a.querySelector('.detail')?.textContent?.trim(),
    ]);
    expect(cards).toEqual([
      ['Revenue', '$10,376,614', 'Completed orders, after discounts'],
      ['Orders', '110,579', '184,931 completed lines'],
      ['Average order value', '$93.84', 'Revenue per order'],
      ['Refund rate', '4.7%', '9,075 of 194,006 lines refunded'],
    ]);
  });

  it('shows an empty state when nothing matches', async () => {
    pending(http, '/api/metrics/summary')[0].flush(EMPTY_SUMMARY);
    await settle(fixture);

    expect(el().textContent).toContain('No data for these filters.');
    expect(el().querySelectorAll('article').length).toBe(0);
  });

  it('shows an error with a working retry', async () => {
    pending(http, '/api/metrics/summary')[0].flush({ detail: 'Database unavailable.' }, SERVER_ERROR);
    await settle(fixture);

    const alert = el().querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Key figures could not be loaded. Database unavailable.');

    (alert?.querySelector('button') as HTMLButtonElement).click();
    await settle(fixture);
    pending(http, '/api/metrics/summary')[0].flush(SUMMARY);
    await settle(fixture);

    expect(el().querySelectorAll('article').length).toBe(4);
  });

  it('requests new figures when a filter changes', async () => {
    pending(http, '/api/metrics/summary')[0].flush(SUMMARY);
    await settle(fixture);

    TestBed.inject(FilterStore).update({ preset: 'last30', regions: ['Europe'] });
    await settle(fixture);

    const [request] = pending(http, '/api/metrics/summary');
    expect(request.request.params.get('from')).toBe('2026-09-12');
    expect(request.request.params.get('to')).toBe('2026-10-11');
    expect(request.request.params.getAll('region')).toEqual(['Europe']);
    request.flush(SUMMARY);
  });
});
describe('KpiCards with a comparison', () => {
  let fixture: ComponentFixture<KpiCards>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [KpiCards], providers: dashboardTestProviders() });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(FilterStore).update({ preset: 'custom', from: '2025-03-01', to: '2025-03-31', compare: 'previous' });
    fixture = TestBed.createComponent(KpiCards);
    await settle(fixture);
  });

  afterEach(() => http.verify());

  it('asks for the comparison and shows each change, its direction and the earlier value', async () => {
    const [request] = pending(http, '/api/metrics/summary');
    expect(request.request.params.get('compare')).toBe('previous');
    request.flush(COMPARED_SUMMARY);
    await settle(fixture);

    expect(el().querySelector('.compared')?.textContent?.trim()).toBe('Compared with Jan 29, 2025 to Feb 28, 2025 (previous period).');
    const cards = Array.from(el().querySelectorAll('article')).map((a) => {
      const change = a.querySelector('.change');
      return [
        a.querySelector('.label')?.textContent?.trim(),
        change?.querySelector('[aria-hidden="true"]')?.textContent?.trim(),
        change?.querySelector('.visually-hidden')?.textContent?.trim(),
        change?.className.replace('change', '').trim(),
        a.querySelector('.detail')?.textContent?.trim(),
      ];
    });
    expect(cards).toEqual([
      ['Revenue', '▲ +5.6%', 'Up 5.6%', 'good', 'Previous period: $90.00'],
      ['Orders', '■ 0.0%', 'No change, 0.0%', '', 'Previous period: 2'],
      ['Average order value', '▲ +5.6%', 'Up 5.6%', 'good', 'Previous period: $45.00'],
      // A falling refund rate is good news.
      ['Refund rate', '▼ -8.3 pts', 'Down 8.3 pts', 'good', 'Previous period: 33.3%'],
    ]);
    expect(el().querySelector('.vs')?.textContent).toBe('vs previous period');
  });

  it('says when there is nothing to compare with', async () => {
    pending(http, '/api/metrics/summary')[0].flush({
      ...COMPARED_SUMMARY,
      comparison: { ...COMPARED_SUMMARY.comparison!, mode: 'year', revenue: 0, orders: 0, averageOrderValue: 0, refundRate: 0, completedLines: 0, refundedLines: 0 },
      changes: { revenue: null, orders: null, averageOrderValue: null, refundRateDifference: null },
    });
    await settle(fixture);

    const changes = Array.from(el().querySelectorAll('.change')).map((c) => c.textContent?.trim());
    expect(changes).toEqual(Array(4).fill('Nothing to compare with'));
    expect(el().querySelector('.detail')?.textContent?.trim()).toBe('Same period last year: $0.00');
  });

  it('drops the comparison when the range can no longer be compared', async () => {
    pending(http, '/api/metrics/summary')[0].flush(COMPARED_SUMMARY);
    await settle(fixture);

    TestBed.inject(FilterStore).update({ preset: 'all', from: null, to: null });
    await settle(fixture);

    const [request] = pending(http, '/api/metrics/summary');
    expect(request.request.params.has('compare')).toBe(false);
    request.flush(SUMMARY);
    await settle(fixture);
    expect(el().querySelector('.change')).toBeNull();
    expect(el().querySelector('.compared')).toBeNull();
  });
});
