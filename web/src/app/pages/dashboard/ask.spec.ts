import { DEFAULT_FILTER } from '../../core/filters/filter-state';
import { formatMetric, requestChips, requestFilter, requestQuery, requestTitle } from './ask';
import { AskRequest } from './metrics.models';

const Q3_EUROPE: AskRequest = {
  metric: 'revenue',
  view: 'total',
  granularity: null,
  dimension: null,
  from: '2026-07-01',
  to: '2026-09-30',
  regions: ['Europe'],
  channels: ['Online Store'],
  categories: [],
  compare: 'previous',
};

describe('ask helpers', () => {
  it('titles a request by metric and view', () => {
    expect(requestTitle(Q3_EUROPE)).toBe('Revenue');
    expect(requestTitle({ ...Q3_EUROPE, metric: 'orders', view: 'over_time', granularity: 'week' })).toBe('Orders by week');
    expect(requestTitle({ ...Q3_EUROPE, view: 'by_dimension', dimension: 'category' })).toBe('Revenue by category');
  });

  it('formats each metric its own way', () => {
    expect(formatMetric('revenue', 369664.68)).toBe('$369,665');
    expect(formatMetric('orders', 4007)).toBe('4,007');
    expect(formatMetric('average_order_value', 92.25)).toBe('$92.25');
    expect(formatMetric('refund_rate', 0.0382)).toBe('3.8%');
  });

  it('runs a request through the dashboard endpoint for its view', () => {
    expect(requestQuery(Q3_EUROPE)).toEqual({
      url: '/api/metrics/summary',
      params: { from: '2026-07-01', to: '2026-09-30', region: ['Europe'], channel: ['Online Store'], compare: 'previous' },
    });
    expect(requestQuery({ ...Q3_EUROPE, view: 'over_time', granularity: 'week', compare: 'none' })).toEqual({
      url: '/api/metrics/timeseries',
      params: { from: '2026-07-01', to: '2026-09-30', region: ['Europe'], channel: ['Online Store'], granularity: 'week' },
    });
    // A split by group has no comparison.
    expect(requestQuery({ ...Q3_EUROPE, view: 'by_dimension', dimension: 'channel', from: null, to: null, regions: [], channels: [] })).toEqual({
      url: '/api/metrics/breakdown',
      params: { by: 'channel' },
    });
  });

  it('offers each part of the request as a removable chip; removing the dates removes the comparison too', () => {
    const chips = requestChips(Q3_EUROPE);

    expect(chips.map((c) => c.label)).toEqual(['Jul 1, 2026 to Sep 30, 2026', 'Europe', 'Online Store', 'vs previous period']);
    expect(chips[0].without).toEqual({ ...Q3_EUROPE, from: null, to: null, compare: 'none' });
    expect(chips[1].without.regions).toEqual([]);
    expect(chips[3].without.compare).toBe('none');
    expect(requestChips({ ...Q3_EUROPE, from: null, to: null, regions: [], channels: [], compare: 'none' })).toEqual([]);
  });

  it('becomes the dashboard filter, keeping the grouping and comparison where the request has none', () => {
    const current = { ...DEFAULT_FILTER, granularity: 'day' as const, compare: 'year' as const };

    expect(requestFilter(Q3_EUROPE, current)).toEqual({
      preset: 'custom', from: '2026-07-01', to: '2026-09-30', regions: ['Europe'], channels: ['Online Store'], categories: [],
      granularity: 'day', compare: 'previous',
    });
    const byWeek = requestFilter({ ...Q3_EUROPE, view: 'over_time', granularity: 'week' }, current);
    expect(byWeek.granularity).toBe('week');
    const allTimeByGroup = requestFilter({ ...Q3_EUROPE, view: 'by_dimension', from: null, to: null }, current);
    expect([allTimeByGroup.preset, allTimeByGroup.compare]).toEqual(['all', 'year']);
  });
});
