import { formatPoints, formatSignedPercent } from '../../core/format/format';
import { ComparisonModeName } from './metrics.models';

/** How the comparison period is named in sentences ("vs previous period"). */
export const COMPARISON_NAMES: Readonly<Record<ComparisonModeName, string>> = {
  previous: 'previous period',
  year: 'same period last year',
};

/** Capitalised, for labels that start a line ("Previous period: $1,234"). */
export const COMPARISON_TITLES: Readonly<Record<ComparisonModeName, string>> = {
  previous: 'Previous period',
  year: 'Same period last year',
};

/**
 * Short names for the cramped KPI cards ("vs last year", "Last year: $1,234"). The full dates are shown once above
 * the cards, so "last year" there cannot be mistaken for the whole of last year.
 */
export const COMPARISON_SHORT_NAMES: Readonly<Record<ComparisonModeName, string>> = {
  previous: 'previous period',
  year: 'last year',
};

export const COMPARISON_SHORT_TITLES: Readonly<Record<ComparisonModeName, string>> = {
  previous: 'Previous period',
  year: 'Last year',
};

/** The `compare` query parameter to add to a metrics request, or nothing without a comparison. */
export function compareParams(compare: ComparisonModeName | null): { compare?: ComparisonModeName } {
  return compare ? { compare } : {};
}

export type ChangeTone = 'good' | 'bad' | 'neutral';

export interface ChangeView {
  /** The arrow, shown but hidden from screen readers. */
  readonly arrow: '▲' | '▼' | '■';
  /** "+12.4%" or "+1.2 pts". */
  readonly text: string;
  /** What a screen reader hears instead of the arrow: "Up 12.4%". */
  readonly spoken: string;
  readonly tone: ChangeTone;
}

/**
 * Turns a change from the API into what a KPI card shows. A change that rounds to zero at one decimal is "no change",
 * so the arrow never disagrees with the number. `higherIsBetter` decides the colour: a rising refund rate is bad.
 * Returns null when there is nothing to compare with.
 */
export function changeView(change: number | null | undefined, unit: 'percent' | 'points', higherIsBetter: boolean): ChangeView | null {
  if (change === null || change === undefined) {
    return null;
  }
  const shown = Math.round(change * 1000) / 1000; // one decimal of a percentage
  const text = unit === 'percent' ? formatSignedPercent(shown) : formatPoints(shown);
  const magnitude = text.replace(/^[+-]/, '');
  if (shown === 0) {
    return { arrow: '■', text, spoken: `No change, ${magnitude}`, tone: 'neutral' };
  }
  const up = shown > 0;
  return {
    arrow: up ? '▲' : '▼',
    text,
    spoken: `${up ? 'Up' : 'Down'} ${magnitude}`,
    tone: up === higherIsBetter ? 'good' : 'bad',
  };
}
