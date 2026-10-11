import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { FilterStore } from '../../core/filters/filter.store';
import { Granularity, formatCurrency, formatInteger, formatPeriod } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { ChartCanvas } from '../../shared/chart/chart-canvas';
import { describeSeries, readChartColors, revenueChartConfig } from './chart-configs';
import { RevenueTimeseries } from './metrics.models';

@Component({
  selector: 'app-revenue-chart',
  imports: [ChartCanvas],
  template: `
    <section class="card" aria-labelledby="revenue-title">
      <header class="head">
        <div>
          <h2 id="revenue-title" class="widget-title">Revenue over time</h2>
          @if (total(); as t) {
            <p class="widget-subtitle">{{ t }}</p>
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
          <button type="button" class="btn" (click)="series.reload()">Retry</button>
        </div>
      } @else if (series.isLoading()) {
        <div class="skeleton chart-skeleton" aria-hidden="true"></div>
        <span class="visually-hidden" role="status">Loading revenue over time…</span>
      } @else if (empty()) {
        <div class="state">No data for these filters.</div>
      } @else if (series.hasValue()) {
        <app-chart-canvas [config]="config()" [label]="description()" />
        <details class="table-toggle">
          <summary>Show data table</summary>
          <div class="table-scroll">
            <table class="data-table">
              <caption class="visually-hidden">Revenue and orders by {{ series.value().granularity }}</caption>
              <thead>
                <tr><th scope="col">Period</th><th scope="col">Revenue</th><th scope="col">Orders</th></tr>
              </thead>
              <tbody>
                @for (p of series.value().points; track p.periodStart) {
                  <tr>
                    <th scope="row">{{ period(p.periodStart) }}</th>
                    <td>{{ money(p.revenue) }}</td>
                    <td>{{ count(p.orders) }}</td>
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
    .chart-skeleton {
      height: var(--chart-height, 18rem);
    }
  `,
})
export class RevenueChart {
  protected readonly filters = inject(FilterStore);
  private readonly colors = readChartColors();

  protected readonly granularities: readonly { value: Granularity; label: string }[] = [
    { value: 'day', label: 'Day' },
    { value: 'week', label: 'Week' },
    { value: 'month', label: 'Month' },
  ];

  protected readonly series = httpResource<RevenueTimeseries>(() => ({
    url: '/api/metrics/timeseries',
    params: { ...this.filters.apiParams(), granularity: this.filters.granularity() },
  }));

  protected readonly empty = computed(
    () => this.series.hasValue() && this.series.value().points.every((p) => p.revenue === 0 && p.orders === 0),
  );

  protected readonly config = computed(() =>
    revenueChartConfig(this.series.value()?.points ?? [], this.series.value()?.granularity ?? 'month', this.colors),
  );

  protected readonly description = computed(() =>
    describeSeries(this.series.value()?.points ?? [], this.series.value()?.granularity ?? 'month'),
  );

  protected readonly total = computed(() => {
    if (!this.series.hasValue() || this.empty()) {
      return null;
    }
    const points = this.series.value().points;
    return `${formatCurrency(points.reduce((sum, p) => sum + p.revenue, 0))} across ${formatInteger(points.length)} periods`;
  });

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