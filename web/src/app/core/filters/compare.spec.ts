import { convertToParamMap } from '@angular/router';
import { drillQueryParams } from './drill-down';
import { DEFAULT_FILTER, FilterState, compareParam, compareUnavailableReason, fromQueryParams, toApiParams, toQueryParams } from './filter-state';

const today = new Date(2026, 9, 11); // 11 Oct 2026, local

describe('comparison setting', () => {
  it('is read from and written to the URL, and left out when there is none', () => {
    expect(fromQueryParams(convertToParamMap({ compare: 'previous' })).compare).toBe('previous');
    expect(fromQueryParams(convertToParamMap({ compare: 'year' })).compare).toBe('year');
    expect(fromQueryParams(convertToParamMap({ compare: 'quarter' })).compare).toBe('none');
    expect(fromQueryParams(convertToParamMap({})).compare).toBe('none');

    expect(toQueryParams({ ...DEFAULT_FILTER, compare: 'year' })['compare']).toBe('year');
    expect(toQueryParams(DEFAULT_FILTER)['compare']).toBeNull();
  });

  it('is sent to the API only for a range with a start and an end', () => {
    const previous: FilterState = { ...DEFAULT_FILTER, compare: 'previous' };

    expect(compareParam({ ...previous, preset: 'last90' }, today)).toBe('previous');
    expect(compareParam({ ...previous, preset: 'custom', from: '2025-01-01', to: '2025-03-31' }, today)).toBe('previous');
    expect(compareParam(previous, today)).toBeNull(); // all time
    expect(compareParam({ ...previous, preset: 'custom', from: '2025-01-01', to: null }, today)).toBeNull();
    expect(compareParam({ ...DEFAULT_FILTER, preset: 'last90' }, today)).toBeNull(); // no comparison chosen
  });

  it('explains why a range cannot be compared', () => {
    expect(compareUnavailableReason(DEFAULT_FILTER, today)).toBe('Choose a date range with a start and an end to compare it.');
    expect(compareUnavailableReason({ ...DEFAULT_FILTER, preset: 'ytd' }, today)).toBeNull();
  });

  it('is a dashboard setting: not a filter for the grid, and not carried into a drill-down', () => {
    const state: FilterState = { ...DEFAULT_FILTER, preset: 'last30', compare: 'year' };

    expect(toApiParams(state, today)).toEqual({ from: '2026-09-12', to: '2026-10-11' });
    expect(drillQueryParams(state)).not.toHaveProperty('compare');
  });
});
