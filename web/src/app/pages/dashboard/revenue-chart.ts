import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { drillQueryParams, drillToPeriod } from '../../core/filters/drill-down';
import { FilterStore, TODAY } from '../../core/filters/filter.store';
import { Granularity, formatCurrency, formatInteger, formatPeriod } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { ChartCanvas } from '../../shared/chart/chart-canvas';
import { describeSeries, readChartColors, revenueChartConfig } from './chart-configs';
import { COMPARISON_TITLES, compareParams } from './comparison';
import { RevenueTimeseries } from './metrics.models';

@Component({
  selector: 'app-revenue-chart',
  imports: [ChartCanvas, RouterLink],
  template: `
    <section class="card" aria-labelledby="revenue-title">
      <header class="head">
        <div>
          <h2 id="revenue-title" class="widget-title">Revenue over time</h2>
          @if (subtitle(); as s) {
            <p class="widget-subtitle">{{ s }}</p>
          }
        </div>
        <div class="toggle" role="group" aria-label="Group revenue by">
          @for (option of granularities; track option.value) {
            <button
              type="button"
              class="btn"
              [class.active]="filters.granularity() === option.value"
              [attr.aria-pressed]="filters.granularity() === option.value"
              (click)="filters.update({ granularity: option.value })"
            >
              {{ option.label }}
            </button>
          }
        </div>
      </header>

      @if (series.error(); as error) {
        <div class="state error" role="alert">
          <p>Revenue over time could not be loaded. {{ message(error) }}</p>
          <button type="button" class="btn" (click)="series.reload()">Try again</button>
        </div>
      } @else if (series.isLoading()) {
        <div class="skeleton chart-skeleton" aria-hidden="true"></div>
        <span class="visually-hidden" role="status">Loading revenue over time…</span>
      } @else if (empty()) {
        <div class="state">No data for these filters.</div>
      } @else if (series.hasValue()) {
        @if (comparisonTitle(); as title) {
          <ul class="legend" aria-label="Lines">
            <li><span class="swatch current" aria-hidden="true"></span>Revenue</li>
            <li><span class="swatch compared" aria-hidden="true"></span>{{ title }} (dashed)</li>
          </ul>
        }
        <app-chart-canvas
          [config]="config()"
          [label]="description()"
          [items]="items()"
          [hint]="'Use the arrow keys to choose a ' + series.value().granularity + ', then press Enter to see its orders.'"
          (selectItem)="open($event)"
        />
        <p class="chart-hint">Select a {{ series.value().granularity }} to see its completed order lines.</p>
        <details class="table-toggle">
          <summary>Show data table</summary>
          <div class="table-scroll">
            <table class="data-table">
              <caption class="visually-hidden">Revenue and orders by {{ series.value().granularity }}</caption>
              <thead>
                <tr>
                  <th scope="col">Period</th>
                  <th scope="col">Revenue</th>
                  <th scope="col">Orders</th>
                  @if (comparisonTitle(); as title) {
                    <th scope="col">{{ title }}: revenue</th>
                    <th scope="col">{{ title }}: orders</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (p of series.value().points; track p.periodStart) {
                  <tr>
                    <th scope="row"><a routerLink="/orders" [queryParams]="drills()[$index]">{{ period(p.periodStart) }}</a></th>
                    <td>{{ money(p.revenue) }}</td>
                    <td>{{ count(p.orders) }}</td>
                    @if (comparisonTitle()) {
                      <td>{{ money(p.comparisonRevenue ?? 0) }}</td>
                      <td>{{ count(p.comparisonOrders ?? 0) }}</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </details>
      }
    </section>
  `,
  styles: `
    .head {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      align-items: flex-start;
      gap: var(--space-3);
      margin-bottom: var(--space-4);
    }
    .toggle {
      display: inline-flex;
    }
    .toggle .btn {
      min-height: 2.25rem;
      border-radius: 0;
      margin-left: -1px;
    }
    .toggle .btn:first-child {
      border-radius: var(--radius-sm) 0 0 var(--radius-sm);
    }
    .toggle .btn:last-child {
      border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
    }
    .toggle .btn.active {
      position: relative;
      background: var(--color-primary);
      border-color: var(--color-primary);
      color: var(--color-primary-ink);
    }
    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1) var(--space-4);
      margin: 0 0 var(--space-2);
      padding: 0;
      list-style: none;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
    }
    .legend li {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
    }
    .swatch {
      width: 1.5rem;
      border-top: 3px solid var(--color-primary);
    }
    .swatch.compared {
      border-top: 2px dashed var(--color-muted);
    }
    .chart-skeleton {
      height: var(--chart-height, 18rem);
    }
  `,
})
export class RevenueChart {
  protected readonly filters = inject(FilterStore);
  private readonly today = inject(TODAY);
  private readonly router = inject(Router);
  private readonly colors = readChartColors();

  protected readonly granularities: readonly { value: Granularity; label: string }[] = [
    { value: 'day', label: 'Day' },
    { value: 'week', label: 'Week' },
    { value: 'month', label: 'Month' },
  ];

  protected readonly series = httpResource<RevenueTimeseries>(() => ({
    url: '/api/metrics/timeseries',
    params: { ...this.filters.apiParams(), granularity: this.filters.granularity(), ...compareParams(this.filters.compareParam()) },
  }));

  /** "Previous period" or "Same period last year" when the loaded series carries a comparison. */
  protected readonly comparisonTitle = computed(() => {
    const mode = this.series.hasValue() ? this.series.value().comparison?.mode : undefined;
    return mode ? COMPARISON_TITLES[mode] : null;
  });

  protected readonly empty = computed(
    () =>
      this.series.hasValue() &&
      this.series.value().points.every((p) => p.revenue === 0 && p.orders === 0 && !p.comparisonRevenue && !p.comparisonOrders),
  );

  protected readonly config = computed(() =>
    revenueChartConfig(this.series.value()?.points ?? [], this.series.value()?.granularity ?? 'month', this.colors, this.comparisonTitle()),
  );

  protected readonly description = computed(() =>
    describeSeries(this.series.value()?.points ?? [], this.series.value()?.granularity ?? 'month', this.comparisonTitle()),
  );

  /**
   * Period count and the best period. Deliberately not a total: summing per-period values that were each rounded
   * to the cent drifts from the Revenue KPI by a few cents, which reads as an error.
   */
  protected readonly subtitle = computed(() => {
    if (!this.series.hasValue() || this.empty()) {
      return null;
    }
    const { points, granularity } = this.series.value();
    const peak = points.reduce((best, p) => (p.revenue > best.revenue ? p : best), points[0]);
    return `${formatInteger(points.length)} ${granularity}s · highest ${formatPeriod(peak.periodStart, granularity)}, ${formatCurrency(peak.revenue)}`;
  });

  /** Keyboard announcement for each point. */
  protected readonly items = computed(() => {
    const series = this.series.hasValue() ? this.series.value() : undefined;
    if (!series) {
      return [];
    }
    const title = this.comparisonTitle();
    return series.points.map((p) => {
      const current = `${formatPeriod(p.periodStart, series.granularity)}: ${formatCurrency(p.revenue)}, ${formatInteger(p.orders)} orders`;
      return title ? `${current}; ${title.toLowerCase()}: ${formatCurrency(p.comparisonRevenue ?? 0)}` : current;
    });
  });

  /**
   * Orders-grid query params for each point: the current filter narrowed to that period. Uses the granularity the
   * points were loaded with, not the toggle, which may already have moved on while the new series loads.
   */
  protected readonly drills = computed(() => {
    const series = this.series.hasValue() ? this.series.value() : undefined;
    if (!series) {
      return [];
    }
    const filter = this.filters.filter();
    const today = this.today();
    return series.points.map((p) => drillQueryParams(drillToPeriod(filter, today, p.periodStart, series.granularity)));
  });

  protected open(index: number): void {
    const queryParams = this.drills()[index];
    if (queryParams) {
      void this.router.navigate(['/orders'], { queryParams });
    }
  }

  protected period(value: string): string {
    return formatPeriod(value, this.series.value()?.granularity ?? 'month');
  }

  protected money(value: number): string {
    return formatCurrency(value);
  }

  protected count(value: number): string {
    return formatInteger(value);
  }

  protected message(error: unknown): string {
    return problemMessage(error, 'Try again.');
  }
}