/**
 * Display formatting for the dashboard. Amounts are shown in US dollars (the fictional roaster's currency).
 * Dates from the API are calendar dates (yyyy-MM-dd) and are parsed as local dates so they never shift a day.
 */
const LOCALE = 'en-US';

const currency = new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD', maximumFractionDigits: 2, minimumFractionDigits: 2 });
const currencyWhole = new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
const compactCurrency = new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD', notation: 'compact', maximumFractionDigits: 1 });
const integer = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat(LOCALE, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });

/** $1,234.56 */
export function formatCurrency(value: number): string {
  return currency.format(value);
}

/** $1,235 for large KPI figures, cents kept below 1,000. */
export function formatKpiCurrency(value: number): string {
  return Math.abs(value) >= 1000 ? currencyWhole.format(value) : currency.format(value);
}

/** $1.2M, for chart axes. */
export function formatCompactCurrency(value: number): string {
  return compactCurrency.format(value);
}

/** 12,345 */
export function formatInteger(value: number): string {
  return integer.format(value);
}

/** A fraction as a percentage: 0.0468 becomes 4.7% */
export function formatPercent(fraction: number): string {
  return percent.format(fraction);
}

const discount = new Intl.NumberFormat(LOCALE, { style: 'percent', minimumFractionDigits: 0, maximumFractionDigits: 1 });

/** A discount fraction without trailing zeros: 0.05 becomes 5%, 0.333 becomes 33.3%. */
export function formatDiscount(fraction: number): string {
  return discount.format(fraction);
}

/** Parses yyyy-MM-dd as a local calendar date. */
export function parseIsoDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

/** Formats a local Date as yyyy-MM-dd. */
export function toIsoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

export type Granularity = 'day' | 'week' | 'month';

/** Label for a period start: "Mar 2025" for months, "Mar 3, 2025" for days and weeks. */
export function formatPeriod(periodStart: string, granularity: Granularity): string {
  const date = parseIsoDate(periodStart);
  return granularity === 'month'
    ? date.toLocaleDateString(LOCALE, { month: 'short', year: 'numeric' })
    : date.toLocaleDateString(LOCALE, { month: 'short', day: 'numeric', year: 'numeric' });
}

/** "Jan 5, 2025" */
export function formatDate(value: string): string {
  return parseIsoDate(value).toLocaleDateString(LOCALE, { month: 'short', day: 'numeric', year: 'numeric' });
}