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

/**
 * The revenue line, plus a dashed, muted line for the comparison period when `comparisonTitle` is given (the API then
 * returns comparison values on every point). Only the current line is filled; both share the click target per period.
 */
export function revenueChartConfig(
  points: readonly TimeseriesPoint[],
  granularity: Granularity,
  colors: ChartColors,
  comparisonTitle: string | null = null,
): ChartConfiguration<'line'> {
  const dense = points.length > 60;
  const comparison: ChartConfiguration<'line'>['data']['datasets'] = comparisonTitle
    ? [
        {
          label: comparisonTitle,
          data: points.map((p) => p.comparisonRevenue ?? 0),
          borderColor: colors.text,
          borderDash: [6, 4],
          borderWidth: 2,
          fill: false,
          tension: 0.25,
          pointRadius: 0,
          pointHoverRadius: 4,
          pointBackgroundColor: colors.text,
          order: 1, // drawn behind the current line
        },
      ]
    : [];
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
          order: 0,
        },
        ...comparison,
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
              return item.datasetIndex === 0
                ? [`Revenue: ${formatCurrency(point.revenue)}`, `Orders: ${formatInteger(point.orders)}`]
                : `${comparisonTitle}: ${formatCurrency(point.comparisonRevenue ?? 0)}, ${formatInteger(point.comparisonOrders ?? 0)} orders`;
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
      // The whole row of a bar is its target, so short bars are as easy to hover and click as long ones.
      interaction: { mode: 'index', axis: 'y', intersect: false },
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
export function describeSeries(points: readonly TimeseriesPoint[], granularity: Granularity, comparisonTitle: string | null = null): string {
  if (points.length === 0) {
    return 'Revenue over time: no data.';
  }
  const total = points.reduce((sum, p) => sum + p.revenue, 0);
  const peak = points.reduce((best, p) => (p.revenue > best.revenue ? p : best), points[0]);
  const compared = comparisonTitle
    ? ` Dashed line: ${comparisonTitle.toLowerCase()}, total ${formatCurrency(points.reduce((sum, p) => sum + (p.comparisonRevenue ?? 0), 0))}.`
    : '';
  return (
    `Revenue by ${granularity} from ${formatPeriod(points[0].periodStart, granularity)} to ` +
    `${formatPeriod(points[points.length - 1].periodStart, granularity)}: ${points.length} periods, ` +
    `total ${formatCurrency(total)}, highest ${formatCurrency(peak.revenue)} in ${formatPeriod(peak.periodStart, granularity)}.` +
    compared
  );
}

export function describeBreakdown(title: string, groups: readonly BreakdownGroup[]): string {
  if (groups.length === 0) {
    return `${title}: no data.`;
  }
  const parts = groups.map((g) => `${g.key} ${formatCurrency(g.revenue)}`);
  return `${title}: ${parts.join(', ')}.`;
}
/**
 * A chart for one metric of an answer to a question: a line over time or horizontal bars by group, optionally with a
 * dashed comparison line. Values are formatted with the metric's own format (revenue or orders).
 */
export function answerChartConfig(
  kind: 'line' | 'bar',
  labels: readonly string[],
  values: readonly number[],
  label: string,
  format: (value: number) => string,
  colors: ChartColors,
  comparison: { readonly label: string; readonly values: readonly number[] } | null = null,
): ChartConfiguration {
  const axis = { grid: { color: colors.grid }, border: { display: false }, ticks: { color: colors.text, callback: (v: string | number) => format(Number(v)), maxTicksLimit: 6 } };
  const plain = { grid: { display: false }, ticks: { color: colors.text, autoSkip: true, maxTicksLimit: 8, maxRotation: 0 } };
  const datasets: ChartConfiguration['data']['datasets'] = [
    kind === 'line'
      ? { type: 'line', label, data: [...values], borderColor: colors.primary, backgroundColor: colors.primarySoft, borderWidth: 2, fill: true, tension: 0.25, pointRadius: values.length > 60 ? 0 : 3 }
      : { type: 'bar', label, data: [...values], backgroundColor: colors.primary, borderRadius: 4, maxBarThickness: 28 },
  ];
  if (comparison && kind === 'line') {
    datasets.push({ type: 'line', label: comparison.label, data: [...comparison.values], borderColor: colors.text, borderDash: [6, 4], borderWidth: 2, fill: false, tension: 0.25, pointRadius: 0 });
  }
  return {
    type: kind,
    data: { labels: [...labels], datasets },
    options: {
      indexAxis: kind === 'bar' ? 'y' : 'x',
      responsive: true,
      maintainAspectRatio: false,
      animation: reducedMotion() ? false : { duration: 300 },
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { display: false },
        tooltip: { callbacks: { label: (item) => `${item.dataset.label}: ${format(Number(item.raw))}` } },
      },
      scales: kind === 'bar' ? { x: { beginAtZero: true, ...axis }, y: plain } : { x: plain, y: { beginAtZero: true, ...axis } },
    },
  };
}
