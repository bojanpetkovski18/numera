import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterStore } from '../../core/filters/filter.store';
import { KpiCards } from './kpi-cards';
import { EMPTY_SUMMARY, SERVER_ERROR, SUMMARY, dashboardTestProviders, pending, settle } from './testing';

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