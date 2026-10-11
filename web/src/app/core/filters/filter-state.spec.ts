import { convertToParamMap } from '@angular/router';
import { DEFAULT_FILTER, FilterState, fromQueryParams, resolveRange, toApiParams, toQueryParams } from './filter-state';

const today = new Date(2026, 9, 11); // 11 Oct 2026, local

function params(query: Record<string, string | string[]>) {
  return convertToParamMap(query);
}

describe('filter state', () => {
  it('resolves presets against today, inclusive', () => {
    expect(resolveRange({ ...DEFAULT_FILTER, preset: 'last30' }, today)).toEqual({ from: '2026-09-12', to: '2026-10-11' });
    expect(resolveRange({ ...DEFAULT_FILTER, preset: 'last90' }, today)).toEqual({ from: '2026-07-14', to: '2026-10-11' });
    expect(resolveRange({ ...DEFAULT_FILTER, preset: 'ytd' }, today)).toEqual({ from: '2026-01-01', to: '2026-10-11' });
    expect(resolveRange(DEFAULT_FILTER, today)).toEqual({ from: null, to: null });
    expect(resolveRange({ ...DEFAULT_FILTER, preset: 'custom', from: '2025-01-01', to: null }, today)).toEqual({ from: '2025-01-01', to: null });
  });

  it('builds API params with repeated keys and no empty values', () => {
    const state: FilterState = { ...DEFAULT_FILTER, preset: 'ytd', regions: ['Europe', 'Asia-Pacific'], categories: ['Blends'] };

    expect(toApiParams(state, today)).toEqual({
      from: '2026-01-01',
      to: '2026-10-11',
      region: ['Europe', 'Asia-Pacific'],
      category: ['Blends'],
    });
    expect(toApiParams(DEFAULT_FILTER, today)).toEqual({});
  });

  it('round-trips through URL query params', () => {
    const states: FilterState[] = [
      DEFAULT_FILTER,
      { ...DEFAULT_FILTER, preset: 'last90', granularity: 'week' },
      { ...DEFAULT_FILTER, preset: 'custom', from: '2025-01-01', to: '2025-03-31', regions: ['Europe'], channels: ['Mobile App'], categories: ['Blends', 'Equipment'] },
    ];

    for (const state of states) {
      const query = Object.fromEntries(
        Object.entries(toQueryParams(state)).filter((entry): entry is [string, string | string[]] => entry[1] !== null),
      );
      expect(fromQueryParams(params(query))).toEqual(state);
    }
  });

  it('keeps the default dashboard URL clean', () => {
    expect(Object.values(toQueryParams(DEFAULT_FILTER)).every((v) => v === null)).toBe(true);
  });

  it('treats explicit dates as a custom range', () => {
    const state = fromQueryParams(params({ preset: 'last30', from: '2025-02-01' }));

    expect(state.preset).toBe('custom');
    expect(state.from).toBe('2025-02-01');
    expect(state.to).toBeNull();
  });

  it('ignores malformed or unknown values instead of failing', () => {
    const state = fromQueryParams(
      params({ preset: 'forever', from: '2025-02-30', to: 'soon', granularity: 'year', region: ['', '  Europe ', 'Europe'] }),
    );

    expect(state).toEqual({ ...DEFAULT_FILTER, regions: ['Europe'] });
  });

  it('drops a "to" date that is before "from"', () => {
    const state = fromQueryParams(params({ from: '2025-03-01', to: '2025-01-01' }));

    expect(state.from).toBe('2025-03-01');
    expect(state.to).toBeNull();
  });
});