import { FALLBACK_COLORS, breakdownChartConfig, describeBreakdown, describeSeries, revenueChartConfig } from './chart-configs';

const points = [
  { periodStart: '2025-01-01', revenue: 95, orders: 2 },
  { periodStart: '2025-02-01', revenue: 30, orders: 1 },
  { periodStart: '2025-03-01', revenue: 12.34, orders: 1 },
];

describe('chart configs', () => {
  it('maps the series to labelled line data', () => {
    const config = revenueChartConfig(points, 'month', FALLBACK_COLORS);

    expect(config.data.labels).toEqual(['Jan 2025', 'Feb 2025', 'Mar 2025']);
    expect(config.data.datasets[0].data).toEqual([95, 30, 12.34]);
  });

  it('hides point markers on dense series', () => {
    const dense = Array.from({ length: 90 }, (_, i) => ({ periodStart: '2025-01-01', revenue: i, orders: 1 }));

    expect(revenueChartConfig(dense, 'day', FALLBACK_COLORS).data.datasets[0].pointRadius).toBe(0);
    expect(revenueChartConfig(points, 'month', FALLBACK_COLORS).data.datasets[0].pointRadius).toBe(3);
  });

  it('draws breakdowns as horizontal bars in the given order', () => {
    const config = breakdownChartConfig(
      [
        { key: 'Blends', revenue: 80, orders: 3 },
        { key: 'Equipment', revenue: 45, orders: 1 },
      ],
      FALLBACK_COLORS,
    );

    expect(config.options?.indexAxis).toBe('y');
    expect(config.data.labels).toEqual(['Blends', 'Equipment']);
  });

  it('writes text alternatives with totals and the peak', () => {
    expect(describeSeries(points, 'month')).toBe(
      'Revenue by month from Jan 2025 to Mar 2025: 3 periods, total $137.34, highest $95.00 in Jan 2025.',
    );
    expect(describeSeries([], 'month')).toBe('Revenue over time: no data.');
    expect(describeBreakdown('Revenue by region', [{ key: 'Europe', revenue: 77.34, orders: 2 }])).toBe(
      'Revenue by region: Europe $77.34.',
    );
  });
});