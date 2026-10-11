import { Granularity, toIsoDate } from '../format/format';

export type DatePreset = 'last30' | 'last90' | 'ytd' | 'all' | 'custom';

export const DATE_PRESETS: readonly { readonly value: DatePreset; readonly label: string }[] = [
  { value: 'last30', label: 'Last 30 days' },
  { value: 'last90', label: 'Last 90 days' },
  { value: 'ytd', label: 'Year to date' },
  { value: 'all', label: 'All time' },
  { value: 'custom', label: 'Custom range' },
];

/** The shared dashboard/grid filter. Lists mean "any of"; empty means "all". */
export interface FilterState {
  readonly preset: DatePreset;
  /** Only used when preset is 'custom'. yyyy-MM-dd. */
  readonly from: string | null;
  readonly to: string | null;
  readonly regions: readonly string[];
  readonly channels: readonly string[];
  readonly categories: readonly string[];
  readonly granularity: Granularity;
}

export const DEFAULT_FILTER: FilterState = {
  preset: 'all',
  from: null,
  to: null,
  regions: [],
  channels: [],
  categories: [],
  granularity: 'month',
};

export interface DateRange {
  readonly from: string | null;
  readonly to: string | null;
}

/** Resolves the preset against today's date. Presets end today and include it. */
export function resolveRange(state: FilterState, today: Date): DateRange {
  const end = toIsoDate(today);
  switch (state.preset) {
    case 'last30':
      return { from: toIsoDate(addDays(today, -29)), to: end };
    case 'last90':
      return { from: toIsoDate(addDays(today, -89)), to: end };
    case 'ytd':
      return { from: `${today.getFullYear()}-01-01`, to: end };
    case 'custom':
      return { from: state.from, to: state.to };
    default:
      return { from: null, to: null };
  }
}

/** Query parameters for the metrics API (repeated keys for lists). */
export function toApiParams(state: FilterState, today: Date): Record<string, string | string[]> {
  const range = resolveRange(state, today);
  const params: Record<string, string | string[]> = {};
  if (range.from) params['from'] = range.from;
  if (range.to) params['to'] = range.to;
  if (state.regions.length) params['region'] = [...state.regions];
  if (state.channels.length) params['channel'] = [...state.channels];
  if (state.categories.length) params['category'] = [...state.categories];
  return params;
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const PRESETS = new Set<DatePreset>(['last30', 'last90', 'ytd', 'all', 'custom']);
const GRANULARITIES = new Set<Granularity>(['day', 'week', 'month']);

/** Minimal read-only view of Angular's ParamMap, so this stays a pure function. */
export interface QueryParamSource {
  get(name: string): string | null;
  getAll(name: string): string[];
}

/**
 * Reads the filter from the URL. Unknown or malformed values fall back to the defaults rather than failing,
 * because URLs are shared and edited by hand. Explicit from/to imply a custom range.
 */
export function fromQueryParams(params: QueryParamSource): FilterState {
  const from = validDate(params.get('from'));
  const to = validDate(params.get('to'));
  const presetParam = params.get('preset') as DatePreset | null;
  const hasCustomDates = from !== null || to !== null;
  const preset: DatePreset = hasCustomDates
    ? 'custom'
    : presetParam && PRESETS.has(presetParam) && presetParam !== 'custom'
      ? presetParam
      : 'all';
  const granularityParam = params.get('granularity') as Granularity | null;

  return {
    preset,
    from: preset === 'custom' ? from : null,
    to: preset === 'custom' ? (from && to && to < from ? null : to) : null,
    regions: cleanList(params.getAll('region')),
    channels: cleanList(params.getAll('channel')),
    categories: cleanList(params.getAll('category')),
    granularity: granularityParam && GRANULARITIES.has(granularityParam) ? granularityParam : 'month',
  };
}

/**
 * Writes the filter to URL query params. Defaults are omitted (null removes a param when merged by the router),
 * so the plain dashboard URL stays clean.
 */
export function toQueryParams(state: FilterState): Record<string, string | string[] | null> {
  const custom = state.preset === 'custom';
  return {
    preset: custom || state.preset === 'all' ? null : state.preset,
    from: custom && state.from ? state.from : null,
    to: custom && state.to ? state.to : null,
    region: state.regions.length ? [...state.regions] : null,
    channel: state.channels.length ? [...state.channels] : null,
    category: state.categories.length ? [...state.categories] : null,
    granularity: state.granularity === 'month' ? null : state.granularity,
  };
}

export function sameFilter(a: FilterState, b: FilterState): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

function validDate(value: string | null): string | null {
  if (!value || !ISO_DATE.test(value)) {
    return null;
  }
  const [y, m, d] = value.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  return date.getFullYear() === y && date.getMonth() === m - 1 && date.getDate() === d ? value : null;
}

function cleanList(values: string[]): string[] {
  return [...new Set(values.map((v) => v.trim()).filter((v) => v.length > 0 && v.length <= 60))].slice(0, 20);
}

function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}