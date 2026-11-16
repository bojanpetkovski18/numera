import { barWidth, formatSignedMoney, splitDrivers, summarySentence } from './drivers';
import { Driver, RevenueDrivers } from './metrics.models';

const driver = (key: string, current: number, comparison: number): Driver => ({
  key,
  currentRevenue: current,
  comparisonRevenue: comparison,
  contribution: Math.round((current - comparison) * 100) / 100,
  shareOfChange: null,
});

function result(change: number, relativeChange: number | null, drivers: Driver[], mode: 'previous' | 'year' = 'previous'): RevenueDrivers {
  return {
    by: 'region',
    comparison: { mode, from: '2026-03-31', to: '2026-06-30' },
    currentRevenue: 0,
    comparisonRevenue: 0,
    change,
    relativeChange,
    drivers,
  };
}

describe('summarySentence', () => {
  it('describes a rise and its biggest increase', () => {
    const d = result(41200, 0.081, [driver('Europe', 128400, 100000), driver('Asia-Pacific', 10000, 12000)]);
    expect(summarySentence(d)).toBe('Revenue rose $41,200 (+8.1%) vs the previous period. The biggest increase was in Europe (+$28,400).');
  });

  it('describes a fall by its biggest decrease, even when a larger driver moved the other way', () => {
    const d = result(-950.25, -0.0556, [driver('Europe', 5000, 3000), driver('North America', 1000, 3950.25)], 'year');
    expect(summarySentence(d)).toBe(
      'Revenue fell $950.25 (-5.6%) vs the same period last year. The biggest decrease was in North America (-$2,950).',
    );
  });

  it('leaves out the percentage when there was no revenue to compare with', () => {
    expect(summarySentence(result(25, null, [driver('Europe', 25, 0)]))).toBe(
      'Revenue rose $25.00 vs the previous period. The biggest increase was in Europe (+$25.00).',
    );
  });

  it('says when nothing changed', () => {
    expect(summarySentence(result(0, 0, [driver('Europe', 60, 40), driver('Asia-Pacific', 40, 60)]))).toBe(
      'Revenue was unchanged vs the previous period.',
    );
  });
});

describe('splitDrivers', () => {
  it('lists the largest drivers and sums the rest into one row', () => {
    const drivers = [6, 5, 4, 3, 2, 1, -0.5].map((n, i) => driver(`G${i}`, 10 + n, 10));

    const { shown, others } = splitDrivers(drivers);

    expect(shown.map((d) => d.key)).toEqual(['G0', 'G1', 'G2', 'G3', 'G4']);
    expect(others).toEqual({ count: 2, currentRevenue: 20.5, comparisonRevenue: 20, contribution: 0.5 });
  });

  it('has no "others" row when everything fits', () => {
    expect(splitDrivers([driver('A', 2, 1)]).others).toBeNull();
  });
});

describe('formatSignedMoney and barWidth', () => {
  it('signs amounts and keeps cents below $1,000', () => {
    expect(formatSignedMoney(28400.4)).toBe('+$28,400');
    expect(formatSignedMoney(-12.5)).toBe('-$12.50');
    expect(formatSignedMoney(0)).toBe('$0.00');
  });

  it('sizes bars against the largest contribution, whatever the sign', () => {
    expect(barWidth(-50, 200)).toBe(25);
    expect(barWidth(200, 200)).toBe(100);
    expect(barWidth(0, 0)).toBe(0);
  });
});
