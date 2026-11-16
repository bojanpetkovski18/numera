import { Granularity } from '../../core/format/format';

export interface MetricsSummary {
  readonly revenue: number;
  readonly orders: number;
  readonly averageOrderValue: number;
  readonly refundRate: number;
  readonly completedLines: number;
  readonly refundedLines: number;
  /** Present (with `changes`) when the request asked for a comparison. */
  readonly comparison?: SummaryComparison | null;
  readonly changes?: KpiChanges | null;
}

export type ComparisonModeName = 'previous' | 'year';

export interface SummaryComparison {
  readonly mode: ComparisonModeName;
  readonly from: string;
  readonly to: string;
  readonly revenue: number;
  readonly orders: number;
  readonly averageOrderValue: number;
  readonly refundRate: number;
  readonly completedLines: number;
  readonly refundedLines: number;
}

/** Relative changes as fractions (0.124 = +12.4%); null when the comparison value is 0. */
export interface KpiChanges {
  readonly revenue: number | null;
  readonly orders: number | null;
  readonly averageOrderValue: number | null;
  /** Difference in percentage points, as a fraction (0.012 = +1.2 points). */
  readonly refundRateDifference: number | null;
}

export interface TimeseriesPoint {
  readonly periodStart: string;
  readonly revenue: number;
  readonly orders: number;
  /** The comparison period's values for the days lined up with this period; null or absent without a comparison. */
  readonly comparisonRevenue?: number | null;
  readonly comparisonOrders?: number | null;
}

export interface ComparisonDates {
  readonly mode: ComparisonModeName;
  readonly from: string;
  readonly to: string;
}

export interface RevenueTimeseries {
  readonly granularity: Granularity;
  readonly points: readonly TimeseriesPoint[];
  readonly comparison?: ComparisonDates | null;
}

export interface BreakdownGroup {
  readonly key: string;
  readonly revenue: number;
  readonly orders: number;
}

export interface RevenueBreakdown {
  readonly by: 'category' | 'region' | 'channel';
  readonly groups: readonly BreakdownGroup[];
}

export type DriverDimension = 'region' | 'channel' | 'category';

/** One group's revenue in both periods; contribution = current - comparison. */
export interface Driver {
  readonly key: string;
  readonly currentRevenue: number;
  readonly comparisonRevenue: number;
  readonly contribution: number;
  /** contribution / total change; null when the total change is 0. */
  readonly shareOfChange: number | null;
}

export interface RevenueDrivers {
  readonly by: DriverDimension;
  readonly comparison: ComparisonDates;
  readonly currentRevenue: number;
  readonly comparisonRevenue: number;
  readonly change: number;
  /** null when the comparison revenue is 0. */
  readonly relativeChange: number | null;
  /** Largest absolute contribution first. */
  readonly drivers: readonly Driver[];
}

export type AskMetric = 'revenue' | 'orders' | 'average_order_value' | 'refund_rate';
export type AskView = 'total' | 'over_time' | 'by_dimension';

/** The request a question was turned into, in the dashboard's terms. */
export interface AskRequest {
  readonly metric: AskMetric;
  readonly view: AskView;
  readonly granularity: Granularity | null;
  readonly dimension: DriverDimension | null;
  readonly from: string | null;
  readonly to: string | null;
  readonly regions: readonly string[];
  readonly channels: readonly string[];
  readonly categories: readonly string[];
  readonly compare: 'none' | ComparisonModeName;
}

/** The answer: the request and its result (as the dashboard endpoints return it), or why it could not be answered. */
export interface AskAnswer {
  readonly status: 'answered' | 'cannot_answer';
  readonly request: AskRequest | null;
  readonly result: MetricsSummary | RevenueTimeseries | RevenueBreakdown | null;
  readonly reason: string | null;
}
