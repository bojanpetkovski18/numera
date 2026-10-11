import { Granularity } from '../../core/format/format';

export interface MetricsSummary {
  readonly revenue: number;
  readonly orders: number;
  readonly averageOrderValue: number;
  readonly refundRate: number;
  readonly completedLines: number;
  readonly refundedLines: number;
}

export interface TimeseriesPoint {
  readonly periodStart: string;
  readonly revenue: number;
  readonly orders: number;
}

export interface RevenueTimeseries {
  readonly granularity: Granularity;
  readonly points: readonly TimeseriesPoint[];
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

export interface FilterOptions {
  readonly regions: readonly string[];
  readonly channels: readonly string[];
  readonly categories: readonly string[];
  readonly firstOrderDate: string | null;
  readonly lastOrderDate: string | null;
}