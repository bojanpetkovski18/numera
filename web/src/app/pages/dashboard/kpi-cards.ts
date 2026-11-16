import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { FilterStore } from '../../core/filters/filter.store';
import { formatCurrency, formatDate, formatInteger, formatKpiCurrency, formatPercent } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { COMPARISON_NAMES, COMPARISON_SHORT_NAMES, COMPARISON_SHORT_TITLES, ChangeView, changeView, compareParams } from './comparison';
import { DASHBOARD_SOURCE } from './dashboard-source';
import { MetricsSummary } from './metrics.models';

interface Kpi {
  readonly label: string;
  readonly value: string;
  readonly detail: string;
  /** Only when the figures were compared with another period. */
  readonly comparison?: {
    readonly change: ChangeView | null;
    /** "Previous period: $9,234,567" or "Last year: $9,234,567" */
    readonly before: string;
  };
}

@Component({
  selector: 'app-kpi-cards',
  template: `
    <section aria-label="Key figures" aria-live="polite" [attr.aria-busy]="summary.isLoading()">
      @if (summary.error(); as error) {
        <div class="card state error" role="alert">
          <p>Key figures could not be loaded. {{ message(error) }}</p>
          <button type="button" class="btn" (click)="summary.reload()">Try again</button>
        </div>
      } @else if (summary.isLoading()) {
        <div class="grid">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="card kpi" aria-hidden="true">
              <div class="skeleton line short"></div>
              <div class="skeleton line value"></div>
            </div>
          }
          <span class="visually-hidden">Loading key figures…</span>
        </div>
      } @else if (empty()) {
        <div class="card state">No data for these filters.</div>
      } @else {
        @if (comparedWith(); as text) {
          <p class="compared">{{ text }}</p>
        }
        <div class="grid">
          @for (kpi of kpis(); track kpi.label) {
            <article class="card kpi">
              <h2 class="label">{{ kpi.label }}</h2>
              <p class="value">{{ kpi.value }}</p>
              @if (kpi.comparison; as c) {
                @if (c.change; as change) {
                  <p class="change" [class.good]="change.tone === 'good'" [class.bad]="change.tone === 'bad'">
                    <span aria-hidden="true">{{ change.arrow }} {{ change.text }}</span>
                    <span class="visually-hidden">{{ change.spoken }}</span>
                    <!-- The leading space is kept: whitespace between elements is not. -->
                    <span class="vs"> vs {{ comparisonName() }}</span>
                  </p>
                } @else {
                  <p class="change none">Nothing to compare with</p>
                }
                <p class="detail">{{ c.before }}</p>
              } @else {
                <p class="detail">{{ kpi.detail }}</p>
              }
            </article>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: var(--space-4);
    }
    .kpi {
      min-height: 7.5rem;
    }
    .label {
      margin: 0;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
    }
    .value {
      margin: var(--space-2) 0 var(--space-1);
      font-size: var(--font-size-xl);
      font-weight: var(--font-weight-bold);
      font-variant-numeric: tabular-nums;
      overflow-wrap: anywhere;
    }
    .detail {
      margin: 0;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
    }
    .compared {
      margin: 0 0 var(--space-3);
      color: var(--color-muted);
      font-size: var(--font-size-sm);
    }
    .change {
      margin: 0 0 var(--space-1);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
      font-variant-numeric: tabular-nums;
    }
    .change.good {
      color: var(--color-success);
    }
    .change.bad {
      color: var(--color-danger);
    }
    .change.none,
    .vs {
      color: var(--color-muted);
      font-weight: normal;
    }
    .line {
      height: 0.9rem;
      margin-bottom: var(--space-3);
    }
    .line.short {
      width: 40%;
    }
    .line.value {
      height: 1.75rem;
      width: 70%;
    }
    @media (max-width: 1024px) {
      .grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
    @media (max-width: 420px) {
      .grid {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class KpiCards {
  private readonly filters = inject(FilterStore);
  private readonly source = inject(DASHBOARD_SOURCE);

  protected readonly summary = httpResource<MetricsSummary>(() => ({
    url: `${this.source.base}/summary`,
    params: { ...this.filters.apiParams(), ...compareParams(this.filters.compareParam()) },
  }));

  private readonly loaded = computed(() => (this.summary.hasValue() ? this.summary.value() : undefined));

  protected readonly empty = computed(() => {
    const s = this.loaded();
    return s !== undefined && s.completedLines + s.refundedLines === 0;
  });

  protected readonly comparisonName = computed(() => {
    const mode = this.loaded()?.comparison?.mode;
    return mode ? COMPARISON_SHORT_NAMES[mode] : '';
  });

  /** "Compared with Mar 31, 2026 to Jun 30, 2026 (previous period)." The dates come from the API, which defines them. */
  protected readonly comparedWith = computed(() => {
    const c = this.loaded()?.comparison;
    return c ? `Compared with ${formatDate(c.from)} to ${formatDate(c.to)} (${COMPARISON_NAMES[c.mode]}).` : null;
  });

  protected readonly kpis = computed<Kpi[]>(() => {
    const s = this.loaded();
    if (!s) {
      return [];
    }
    const refundBase = s.completedLines + s.refundedLines;
    const c = s.comparison ?? null;
    const changes = s.changes ?? null;
    const title = c ? COMPARISON_SHORT_TITLES[c.mode] : '';
    const compared = (change: ChangeView | null, before: string) => (c ? { comparison: { change, before: `${title}: ${before}` } } : {});
    return [
      {
        label: 'Revenue',
        value: formatKpiCurrency(s.revenue),
        detail: 'Completed orders, after discounts',
        ...compared(changeView(changes?.revenue, 'percent', true), formatKpiCurrency(c?.revenue ?? 0)),
      },
      {
        label: 'Orders',
        value: formatInteger(s.orders),
        detail: `${formatInteger(s.completedLines)} completed lines`,
        ...compared(changeView(changes?.orders, 'percent', true), formatInteger(c?.orders ?? 0)),
      },
      {
        label: 'Average order value',
        value: formatCurrency(s.averageOrderValue),
        detail: 'Revenue per order',
        ...compared(changeView(changes?.averageOrderValue, 'percent', true), formatCurrency(c?.averageOrderValue ?? 0)),
      },
      {
        label: 'Refund rate',
        value: formatPercent(s.refundRate),
        detail: `${formatInteger(s.refundedLines)} of ${formatInteger(refundBase)} lines refunded`,
        ...compared(changeView(changes?.refundRateDifference, 'points', false), formatPercent(c?.refundRate ?? 0)),
      },
    ];
  });

  protected message(error: unknown): string {
    return problemMessage(error, 'Try again.');
  }
}
