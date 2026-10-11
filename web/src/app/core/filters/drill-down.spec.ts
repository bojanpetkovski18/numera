import { dashboardQueryParams, drillQueryParams, drillToGroup, drillToPeriod, periodBounds } from './drill-down';
import { DEFAULT_FILTER, FilterState } from './filter-state';

const TODAY = new Date(2026, 9, 11); // Sun Oct 11, 2026

describe('periodBounds', () => {
  it('a day is just that day', () => {
    expect(periodBounds('2026-02-28', 'day')).toEqual({ from: '2026-02-28', to: '2026-02-28' });
  });

  it('a week runs Monday to Sunday, across month and year ends', () => {
    expect(periodBounds('2026-09-28', 'week')).toEqual({ from: '2026-09-28', to: '2026-10-04' });
    expect(periodBounds('2024-12-30', 'week')).toEqual({ from: '2024-12-30', to: '2025-01-05' });
  });

  it('a month ends on its last day, including February in leap and common years', () => {
    expect(periodBounds('2024-02-01', 'month')).toEqual({ from: '2024-02-01', to: '2024-02-29' });
    expect(periodBounds('2025-02-01', 'month')).toEqual({ from: '2025-02-01', to: '2025-02-28' });
    expect(periodBounds('2025-04-01', 'month')).toEqual({ from: '2025-04-01', to: '2025-04-30' });
    expect(periodBounds('2025-12-01', 'month')).toEqual({ from: '2025-12-01', to: '2025-12-31' });
  });
});

describe('drillToPeriod', () => {
  const europe: FilterState = { ...DEFAULT_FILTER, regions: ['Europe'], channels: ['Wholesale'], granularity: 'week' };

  it('with no date filter, uses the whole period and keeps the other filters', () => {
    expect(drillToPeriod(europe, TODAY, '2025-03-03', 'week')).toEqual({
      ...europe,
      preset: 'custom',
      from: '2025-03-03',
      to: '2025-03-09',
    });
  });

  it('clips the first and last periods to the active range, so the rows match the chart point', () => {
    const custom: FilterState = { ...europe, preset: 'custom', from: '2025-03-05', to: '2025-03-20' };
    // Mar 3-9 is the first week shown, Mar 17-23 the last.
    expect(drillToPeriod(custom, TODAY, '2025-03-03', 'week')).toMatchObject({ from: '2025-03-05', to: '2025-03-09' });
    expect(drillToPeriod(custom, TODAY, '2025-03-10', 'week')).toMatchObject({ from: '2025-03-10', to: '2025-03-16' });
    expect(drillToPeriod(custom, TODAY, '2025-03-17', 'week')).toMatchObject({ from: '2025-03-17', to: '2025-03-20' });
  });

  it('turns a preset into explicit dates: the current month of "last 30 days" ends today', () => {
    const last30: FilterState = { ...DEFAULT_FILTER, preset: 'last30' };
    // last 30 days on Oct 11, 2026 = Sep 12 to Oct 11
    expect(drillToPeriod(last30, TODAY, '2026-09-01', 'month')).toMatchObject({ preset: 'custom', from: '2026-09-12', to: '2026-09-30' });
    expect(drillToPeriod(last30, TODAY, '2026-10-01', 'month')).toMatchObject({ preset: 'custom', from: '2026-10-01', to: '2026-10-11' });
  });

  it('keeps an open-ended range open only where the period allows', () => {
    const fromOnly: FilterState = { ...DEFAULT_FILTER, preset: 'custom', from: '2025-01-15', to: null };
    expect(drillToPeriod(fromOnly, TODAY, '2025-01-01', 'month')).toMatchObject({ from: '2025-01-15', to: '2025-01-31' });
  });
});

describe('drillToGroup', () => {
  it('narrows that dimension to the clicked key and keeps everything else', () => {
    const filter: FilterState = { ...DEFAULT_FILTER, preset: 'ytd', regions: ['Europe'], categories: ['Blends', 'Equipment'] };
    expect(drillToGroup(filter, 'category', 'Blends')).toEqual({ ...filter, categories: ['Blends'] });
    expect(drillToGroup(filter, 'region', 'Europe')).toEqual(filter);
    expect(drillToGroup(filter, 'channel', 'Wholesale')).toEqual({ ...filter, channels: ['Wholesale'] });
  });
});

describe('query params', () => {
  const filter: FilterState = { ...DEFAULT_FILTER, preset: 'custom', from: '2025-03-03', to: '2025-03-09', regions: ['Europe'], granularity: 'week' };

  it('the grid gets the filter and the completed status, without the chart granularity', () => {
    expect(drillQueryParams(filter)).toEqual({ from: '2025-03-03', to: '2025-03-09', region: ['Europe'], status: 'completed' });
    expect(drillQueryParams({ ...DEFAULT_FILTER, preset: 'last90' })).toEqual({ preset: 'last90', status: 'completed' });
  });

  it('the dashboard link keeps the filter and the granularity', () => {
    expect(dashboardQueryParams(filter)).toEqual({ from: '2025-03-03', to: '2025-03-09', region: ['Europe'], granularity: 'week' });
    expect(dashboardQueryParams(DEFAULT_FILTER)).toEqual({});
  });
});
