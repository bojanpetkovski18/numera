import {
  formatCompactCurrency,
  formatCurrency,
  formatDate,
  formatInteger,
  formatKpiCurrency,
  formatPercent,
  formatPeriod,
  parseIsoDate,
  toIsoDate,
} from './format';

describe('format helpers', () => {
  it('formats currency with cents', () => {
    expect(formatCurrency(1234.5)).toBe('$1,234.50');
    expect(formatCurrency(0)).toBe('$0.00');
  });

  it('drops cents on large KPI amounts only', () => {
    expect(formatKpiCurrency(10376613.65)).toBe('$10,376,614');
    expect(formatKpiCurrency(93.84)).toBe('$93.84');
  });

  it('formats compact axis amounts', () => {
    expect(formatCompactCurrency(1_250_000)).toBe('$1.3M');
    expect(formatCompactCurrency(482_884)).toBe('$482.9K');
    expect(formatCompactCurrency(0)).toBe('$0');
  });

  it('formats integers with grouping', () => {
    expect(formatInteger(110579)).toBe('110,579');
  });

  it('formats a fraction as a percentage with one decimal', () => {
    expect(formatPercent(0.0468)).toBe('4.7%');
    expect(formatPercent(0)).toBe('0.0%');
    expect(formatPercent(1)).toBe('100.0%');
  });

  it('parses and prints calendar dates without time-zone shifts', () => {
    const date = parseIsoDate('2025-03-01');
    expect(date.getFullYear()).toBe(2025);
    expect(date.getMonth()).toBe(2);
    expect(date.getDate()).toBe(1);
    expect(toIsoDate(date)).toBe('2025-03-01');
    expect(toIsoDate(new Date(2024, 11, 31))).toBe('2024-12-31');
  });

  it('labels periods by granularity', () => {
    expect(formatPeriod('2025-03-01', 'month')).toBe('Mar 2025');
    expect(formatPeriod('2025-03-03', 'week')).toBe('Mar 3, 2025');
    expect(formatPeriod('2025-03-03', 'day')).toBe('Mar 3, 2025');
  });

  it('formats a single date', () => {
    expect(formatDate('2026-09-30')).toBe('Sep 30, 2026');
  });
});