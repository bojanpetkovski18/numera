import { FilterState } from '../../core/filters/filter-state';
import { formatCurrency, formatDate, formatInteger, formatKpiCurrency, formatPercent } from '../../core/format/format';
import { COMPARISON_NAMES } from './comparison';
import { AskMetric, AskRequest } from './metrics.models';

export const ASK_EXAMPLES: readonly string[] = [
  'How did revenue in Q3 2026 compare with the previous quarter?',
  'Which region had the most orders in 2026?',
  'Show weekly revenue in Europe for the last three months of data.',
];

const METRIC_LABELS: Readonly<Record<AskMetric, string>> = {
  revenue: 'Revenue',
  orders: 'Orders',
  average_order_value: 'Average order value',
  refund_rate: 'Refund rate',
};

/** "Revenue", "Orders by week", "Revenue by region". */
export function requestTitle(request: AskRequest): string {
  const metric = METRIC_LABELS[request.metric];
  if (request.view === 'over_time') {
    return `${metric} by ${request.granularity ?? 'month'}`;
  }
  return request.view === 'by_dimension' ? `${metric} by ${request.dimension ?? 'region'}` : metric;
}

export function formatMetric(metric: AskMetric, value: number): string {
  switch (metric) {
    case 'revenue':
      return formatKpiCurrency(value);
    case 'orders':
      return formatInteger(value);
    case 'average_order_value':
      return formatCurrency(value);
    default:
      return formatPercent(value);
  }
}

/** The dashboard endpoint that runs a request (after the user edits it, without asking the model again). */
export function requestQuery(request: AskRequest): { url: string; params: Record<string, string | string[]> } {
  const params: Record<string, string | string[]> = {};
  if (request.from) params['from'] = request.from;
  if (request.to) params['to'] = request.to;
  if (request.regions.length) params['region'] = [...request.regions];
  if (request.channels.length) params['channel'] = [...request.channels];
  if (request.categories.length) params['category'] = [...request.categories];
  if (request.compare !== 'none' && request.view !== 'by_dimension') params['compare'] = request.compare;
  switch (request.view) {
    case 'over_time':
      return { url: '/api/metrics/timeseries', params: { ...params, granularity: request.granularity ?? 'month' } };
    case 'by_dimension':
      return { url: '/api/metrics/breakdown', params: { ...params, by: request.dimension ?? 'region' } };
    default:
      return { url: '/api/metrics/summary', params };
  }
}

export interface RequestChip {
  readonly label: string;
  /** The request without this part; dropping the dates also drops the comparison, which needs them. */
  readonly without: AskRequest;
}

/** The parts of a request the user can remove: the dates, each filter value, the comparison. */
export function requestChips(request: AskRequest): RequestChip[] {
  const chips: RequestChip[] = [];
  if (request.from || request.to) {
    const label =
      request.from && request.to ? `${formatDate(request.from)} to ${formatDate(request.to)}`
      : request.from ? `From ${formatDate(request.from)}`
      : `Up to ${formatDate(request.to as string)}`;
    chips.push({ label, without: { ...request, from: null, to: null, compare: 'none' } });
  }
  for (const field of ['regions', 'channels', 'categories'] as const) {
    for (const value of request[field]) {
      chips.push({ label: value, without: { ...request, [field]: request[field].filter((v) => v !== value) } });
    }
  }
  if (request.compare !== 'none' && request.view !== 'by_dimension') {
    chips.push({ label: `vs ${COMPARISON_NAMES[request.compare]}`, without: { ...request, compare: 'none' } });
  }
  return chips;
}

/** The request as the dashboard's filter, for "Show on the dashboard". */
export function requestFilter(request: AskRequest, current: FilterState): FilterState {
  const dated = request.from !== null || request.to !== null;
  return {
    preset: dated ? 'custom' : 'all',
    from: request.from,
    to: request.to,
    regions: [...request.regions],
    channels: [...request.channels],
    categories: [...request.categories],
    granularity: request.view === 'over_time' && request.granularity ? request.granularity : current.granularity,
    compare: request.view === 'by_dimension' ? current.compare : request.compare,
  };
}
