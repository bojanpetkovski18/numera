import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ChartConfiguration } from 'chart.js';
import { TODAY } from '../../core/filters/filter.store';
import { CHART_FACTORY } from '../../shared/chart/chart-factory';
import { BreakdownGroup, MetricsSummary, TimeseriesPoint } from './metrics.models';

/** Charts created during a test (Chart.js needs a real canvas, which jsdom does not have). */
export const createdCharts: ChartConfiguration[] = [];
/** Items highlighted by keyboard navigation, in order (null = cleared). */
export const highlighted: (number | null)[] = [];

export function dashboardTestProviders(): (Provider | EnvironmentProviders)[] {
  createdCharts.length = 0;
  highlighted.length = 0;
  return [
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: TODAY, useValue: () => new Date(2026, 9, 11) },
    {
      provide: CHART_FACTORY,
      useValue: (_canvas: HTMLCanvasElement, config: ChartConfiguration) => {
        createdCharts.push(config);
        return {
          update: (next: ChartConfiguration) => createdCharts.push(next),
          highlight: (index: number | null) => highlighted.push(index),
          destroy: () => undefined,
        };
      },
    },
  ];
}

/** Simulates a click on chart item `index` through the chart's own click handler. */
export function clickChartItem(index: number): void {
  const config = createdCharts.at(-1);
  const onClick = config?.options?.onClick;
  if (!onClick) {
    throw new Error('The chart has no click handler.');
  }
  // Only the elements' indexes are read; the rest of Chart.js's event objects is not needed here.
  const elements = [{ datasetIndex: 0, index }] as unknown as Parameters<typeof onClick>[1];
  onClick.call(undefined as never, {} as Parameters<typeof onClick>[0], elements, {} as Parameters<typeof onClick>[2]);
}

/**
 * Runs effects and change detection. Deliberately not `whenStable()`: with zoneless change detection an
 * in-flight `httpResource` request is a pending task, so waiting for stability before flushing it never ends.
 */
export async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  TestBed.tick();
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

export function pending(http: HttpTestingController, url: string): TestRequest[] {
  return http.match((r) => r.url === url);
}

export const SUMMARY: MetricsSummary = {
  revenue: 10376613.65,
  orders: 110579,
  averageOrderValue: 93.84,
  refundRate: 0.0468,
  completedLines: 184931,
  refundedLines: 9075,
};

export const EMPTY_SUMMARY: MetricsSummary = { revenue: 0, orders: 0, averageOrderValue: 0, refundRate: 0, completedLines: 0, refundedLines: 0 };

export const POINTS: TimeseriesPoint[] = [
  { periodStart: '2025-01-01', revenue: 95, orders: 2 },
  { periodStart: '2025-02-01', revenue: 30, orders: 1 },
];

export const GROUPS: BreakdownGroup[] = [
  { key: 'Blends', revenue: 80, orders: 3 },
  { key: 'Equipment', revenue: 45, orders: 1 },
];

export const SERVER_ERROR = { status: 500, statusText: 'Server Error' };