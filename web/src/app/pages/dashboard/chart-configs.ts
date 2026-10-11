import { ChartConfiguration } from 'chart.js';
import { Granularity, formatCompactCurrency, formatCurrency, formatInteger, formatPeriod } from '../../core/format/format';
import { BreakdownGroup, TimeseriesPoint } from './metrics.models';

export interface ChartColors {
  readonly primary: string;
  readonly primarySoft: string;
  readonly grid: string;
  readonly text: string;
}

export const FALLBACK_COLORS: ChartColors = { primary: '#2f5d50', primarySoft: 'rgba(47, 93, 80, 0.12)', grid: '#e2dad0', text: '#6b6159' };

/** Reads chart colors from the design tokens so charts follow the theme. */
export function readChartColors(): ChartColors {
  const style = getComputedStyle(document.documentElement);
  const token = (name: string, fallback: string) => style.getPropertyValue(name).trim() || fallback;
  const primary = token('--color-primary', FALLBACK_COLORS.primary);
  return {
    primary,
    primarySoft: `color-mix(in srgb, ${primary} 14%, transparent)`,
    grid: token('--color-border', FALLBACK_COLORS.grid),
    text: token('--color-muted', FALLBACK_COLORS.text),
  };
}

function reducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export function revenueChartConfig(
  points: readonly TimeseriesPoint[],
  granularity: Granularity,
  colors: ChartColors,
): ChartConfiguration<'line'> {
  const dense = points.length > 60;
  return {
    type: 'line',
    data: {
      labels: points.map((p) => formatPeriod(p.periodStart, granularity)),
      datasets: [
        {
          label: 'Revenue',
          data: points.map((p) => p.revenue),
          borderColor: colors.primary,
          backgroundColor: colors.primarySoft,
          borderWidth: 2,
          fill: true,
          tension: 0.25,
          pointRadius: dense ? 0 : 3,
          pointHoverRadius: 5,
          pointBackgroundColor: colors.primary,
        },
      ],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: reducedMotion() ? false : { duration: 300 },
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (item) => {
              const point = points[item.dataIndex];
              return [`Revenue: ${formatCurrency(point.revenue)}`, `Orders: ${formatInteger(point.orders)}`];
            },
          },
        },
      },
      scales: {
        x: { grid: { display: false }, ticks: { color: colors.text, maxRotation: 0, autoSkip: true, maxTicksLimit: 8 } },
        y: {
          beginAtZero: true,
          grid: { color: colors.grid },
          border: { display: false },
          ticks: { color: colors.text, callback: (value) => formatCompactCurrency(Number(value)), maxTicksLimit: 6 },
        },
      },
    },
  };
}

export function breakdownChartConfig(groups: readonly BreakdownGroup[], colors: ChartColors): ChartConfiguration<'bar'> {
  return {
    type: 'bar',
    data: {
      labels: groups.map((g) => g.key),
      datasets: [
        {
          label: 'Revenue',
          data: groups.map((g) => g.revenue),
          backgroundColor: colors.primary,
          borderRadius: 4,
          maxBarThickness: 28,
        },
      ],
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      animation: reducedMotion() ? false : { duration: 300 },
      plugins: {
        legend: { display: false },
        tooltip: {
          callbacks: {
            label: (item) => {
              const group = groups[item.dataIndex];
              return [`Revenue: ${formatCurrency(group.revenue)}`, `Orders: ${formatInteger(group.orders)}`];
            },
          },
        },
      },
      scales: {
        x: {
          beginAtZero: true,
          grid: { color: colors.grid },
          border: { display: false },
          ticks: { color: colors.text, callback: (value) => formatCompactCurrency(Number(value)), maxTicksLimit: 5 },
        },
        y: { grid: { display: false }, ticks: { color: colors.text } },
      },
    },
  };
}

/** One-sentence text alternative for a chart. */
export function describeSeries(points: readonly TimeseriesPoint[], granularity: Granularity): string {
  if (points.length === 0) {
    return 'Revenue over time: no data.';
  }
  const total = points.reduce((sum, p) => sum + p.revenue, 0);
  const peak = points.reduce((best, p) => (p.revenue > best.revenue ? p : best), points[0]);
  return (
    `Revenue by ${granularity} from ${formatPeriod(points[0].periodStart, granularity)} to ` +
    `${formatPeriod(points[points.length - 1].periodStart, granularity)}: ${points.length} periods, ` +
    `total ${formatCurrency(total)}, highest ${formatCurrency(peak.revenue)} in ${formatPeriod(peak.periodStart, granularity)}.`
  );
}

export function describeBreakdown(title: string, groups: readonly BreakdownGroup[]): string {
  if (groups.length === 0) {
    return `${title}: no data.`;
  }
  const parts = groups.map((g) => `${g.key} ${formatCurrency(g.revenue)}`);
  return `${title}: ${parts.join(', ')}.`;
}