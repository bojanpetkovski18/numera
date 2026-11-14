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
