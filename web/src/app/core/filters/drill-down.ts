import { Granularity, parseIsoDate, toIsoDate } from '../format/format';
import { FilterState, resolveRange, toQueryParams } from './filter-state';

export type DrillDimension = 'category' | 'region' | 'channel';

/**
 * The charts show completed revenue, so a drill-down opens the grid on the completed lines that make up the clicked
 * value. The status shows as a removable filter on the grid, so refunded and cancelled lines are one click away.
 */
export const DRILL_STATUS = 'completed';

const LIST_FIELD = { category: 'categories', region: 'regions', channel: 'channels' } as const;

/** First and last day of the period starting on `periodStart`. Weeks run Monday to Sunday, as in the API. */
export function periodBounds(periodStart: string, granularity: Granularity): { from: string; to: string } {
  const end = parseIsoDate(periodStart);
  if (granularity === 'week') {
    end.setDate(end.getDate() + 6);
  } else if (granularity === 'month') {
    end.setMonth(end.getMonth() + 1, 0); // day 0 of the next month is the last day of this one
  }
  return { from: periodStart, to: toIsoDate(end) };
}

/**
 * The filter narrowed to one chart period. The API cuts periods at calendar boundaries, so the first and last
 * periods can extend past the active range; the drill-down keeps only the overlap, which is exactly the data
 * the chart point was computed from. Presets become explicit dates.
 */
export function drillToPeriod(filter: FilterState, today: Date, periodStart: string, granularity: Granularity): FilterState {
  const range = resolveRange(filter, today);
  const period = periodBounds(periodStart, granularity);
  return {
    ...filter,
    preset: 'custom',
    from: range.from !== null && range.from > period.from ? range.from : period.from,
    to: range.to !== null && range.to < period.to ? range.to : period.to,
  };
}

/** The filter narrowed to one breakdown group. Other dimensions and the date range are kept. */
export function drillToGroup(filter: FilterState, by: DrillDimension, key: string): FilterState {
  return { ...filter, [LIST_FIELD[by]]: [key] };
}

/** Query params for the orders grid showing the rows behind a drilled filter. */
export function drillQueryParams(filter: FilterState): Record<string, string | string[]> {
  return { ...withoutNulls({ ...toQueryParams(filter), granularity: null }), status: DRILL_STATUS };
}

/** Query params for a link that opens the dashboard with this filter. */
export function dashboardQueryParams(filter: FilterState): Record<string, string | string[]> {
  return withoutNulls(toQueryParams(filter));
}

function withoutNulls(params: Record<string, string | string[] | null>): Record<string, string | string[]> {
  const result: Record<string, string | string[]> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value !== null) {
      result[key] = value;
    }
  }
  return result;
}
