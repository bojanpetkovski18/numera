import { formatKpiCurrency, formatSignedPercent } from '../../core/format/format';
import { Driver, DriverDimension, RevenueDrivers } from './metrics.models';

/** Drivers listed one by one; the rest are summed into one "others" row. */
export const SHOWN_DRIVERS = 5;

export const DRIVER_DIMENSIONS: readonly { readonly value: DriverDimension; readonly label: string }[] = [
  { value: 'region', label: 'Region' },
  { value: 'channel', label: 'Channel' },
  { value: 'category', label: 'Category' },
];

const VS: Readonly<Record<RevenueDrivers['comparison']['mode'], string>> = {
  previous: 'the previous period',
  year: 'the same period last year',
};

/** An amount with its sign: +$28,400, -$950.25, $0.00. Whole dollars from $1,000, as on the KPI cards. */
export function formatSignedMoney(value: number): string {
  const amount = formatKpiCurrency(Math.abs(value));
  return value > 0 ? `+${amount}` : value < 0 ? `-${amount}` : amount;
}

/**
 * The panel's headline, built only from the data: the total change, then the group that moved most in the same
 * direction. "Revenue rose $41,200 (+8.1%) vs the previous period. The biggest increase was in Europe (+$28,400)."
 */
export function summarySentence(d: RevenueDrivers): string {
  const vs = VS[d.comparison.mode];
  if (d.change === 0) {
    return `Revenue was unchanged vs ${vs}.`;
  }
  const rose = d.change > 0;
  const percent = d.relativeChange === null ? '' : ` (${formatSignedPercent(d.relativeChange)})`;
  const first = `Revenue ${rose ? 'rose' : 'fell'} ${formatKpiCurrency(Math.abs(d.change))}${percent} vs ${vs}.`;
  const lead = d.drivers.find((x) => (rose ? x.contribution > 0 : x.contribution < 0));
  return lead ? `${first} The biggest ${rose ? 'increase' : 'decrease'} was in ${lead.key} (${formatSignedMoney(lead.contribution)}).` : first;
}

export interface OtherDrivers {
  readonly count: number;
  readonly currentRevenue: number;
  readonly comparisonRevenue: number;
  readonly contribution: number;
}

/** The largest drivers, and the rest summed (null when there is no rest). The API sends them largest first. */
export function splitDrivers(drivers: readonly Driver[], shown = SHOWN_DRIVERS): { shown: readonly Driver[]; others: OtherDrivers | null } {
  const rest = drivers.slice(shown);
  const sum = (pick: (d: Driver) => number) => Math.round(rest.reduce((total, d) => total + pick(d), 0) * 100) / 100;
  return {
    shown: drivers.slice(0, shown),
    others: rest.length
      ? { count: rest.length, currentRevenue: sum((d) => d.currentRevenue), comparisonRevenue: sum((d) => d.comparisonRevenue), contribution: sum((d) => d.contribution) }
      : null,
  };
}

/** Bar length as a percentage of half the track: the largest contribution in the list reaches the edge. */
export function barWidth(contribution: number, largest: number): number {
  return largest === 0 ? 0 : Math.round((Math.abs(contribution) / largest) * 1000) / 10;
}
