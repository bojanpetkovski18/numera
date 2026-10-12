import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { FilterStore } from '../../core/filters/filter.store';
import { formatCurrency, formatInteger, formatKpiCurrency, formatPercent } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { MetricsSummary } from './metrics.models';

interface Kpi {
  readonly label: string;
  readonly value: string;
  readonly detail: string;
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
        <div class="grid">
          @for (kpi of kpis(); track kpi.label) {
            <article class="card kpi">
              <h2 class="label">{{ kpi.label }}</h2>
              <p class="value">{{ kpi.value }}</p>
              <p class="detail">{{ kpi.detail }}</p>
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

  protected readonly summary = httpResource<MetricsSummary>(() => ({
    url: '/api/metrics/summary',
    params: this.filters.apiParams(),
  }));

  protected readonly empty = computed(() => {
    const s = this.summary.hasValue() ? this.summary.value() : undefined;
    return s !== undefined && s.completedLines + s.refundedLines === 0;
  });

  protected readonly kpis = computed<Kpi[]>(() => {
    if (!this.summary.hasValue()) {
      return [];
    }
    const s = this.summary.value();
    const refundBase = s.completedLines + s.refundedLines;
    return [
      { label: 'Revenue', value: formatKpiCurrency(s.revenue), detail: 'Completed orders, after discounts' },
      { label: 'Orders', value: formatInteger(s.orders), detail: `${formatInteger(s.completedLines)} completed lines` },
      { label: 'Average order value', value: formatCurrency(s.averageOrderValue), detail: 'Revenue per order' },
      {
        label: 'Refund rate',
        value: formatPercent(s.refundRate),
        detail: `${formatInteger(s.refundedLines)} of ${formatInteger(refundBase)} lines refunded`,
      },
    ];
  });

  protected message(error: unknown): string {
    return problemMessage(error, 'Try again.');
  }
}