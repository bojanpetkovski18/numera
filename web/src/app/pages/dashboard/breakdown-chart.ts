import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { drillQueryParams, drillToGroup } from '../../core/filters/drill-down';
import { FilterStore } from '../../core/filters/filter.store';
import { formatCurrency, formatInteger } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { ChartCanvas } from '../../shared/chart/chart-canvas';
import { breakdownChartConfig, describeBreakdown, readChartColors } from './chart-configs';
import { RevenueBreakdown } from './metrics.models';

@Component({
  selector: 'app-breakdown-chart',
  imports: [ChartCanvas, RouterLink],
  template: `
    <section class="card" [attr.aria-labelledby]="titleId()">
      <h2 class="widget-title" [id]="titleId()">{{ title() }}</h2>
      <p class="widget-subtitle">Completed revenue, highest first</p>

      @if (breakdown.error(); as error) {
        <div class="state error" role="alert">
          <p>{{ title() }} could not be loaded. {{ message(error) }}</p>
          <button type="button" class="btn" (click)="breakdown.reload()">Try again</button>
        </div>
      } @else if (breakdown.isLoading()) {
        <div class="skeleton chart-skeleton" aria-hidden="true"></div>
        <span class="visually-hidden" role="status">Loading {{ title() }}…</span>
      } @else if (empty()) {
        <div class="state">No data for these filters.</div>
      } @else if (breakdown.hasValue()) {
        <app-chart-canvas
          [config]="config()"
          [label]="description()"
          [style.--chart-height]="height()"
          [items]="items()"
          [hint]="'Use the arrow keys to choose a ' + dimensionLabel().toLowerCase() + ', then press Enter to see its orders.'"
          (selectItem)="open($event)"
        />
        <p class="chart-hint">Select a bar to see its completed order lines.</p>
        <details class="table-toggle">
          <summary>Show data table</summary>
          <table class="data-table">
            <caption class="visually-hidden">{{ title() }}</caption>
            <thead>
              <tr><th scope="col">{{ dimensionLabel() }}</th><th scope="col">Revenue</th><th scope="col">Orders</th></tr>
            </thead>
            <tbody>
              @for (g of breakdown.value().groups; track g.key) {
                <tr>
                  <th scope="row"><a routerLink="/orders" [queryParams]="drills()[$index]">{{ g.key }}</a></th>
                  <td>{{ money(g.revenue) }}</td>
                  <td>{{ count(g.orders) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </details>
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    section {
      height: 100%;
    }
    .widget-subtitle {
      margin-bottom: var(--space-4);
    }
    .chart-skeleton {
      height: 14rem;
    }
  `,
})
export class BreakdownChart {
  readonly by = input.required<'category' | 'region' | 'channel'>();
  readonly title = input.required<string>();

  private readonly filters = inject(FilterStore);
  private readonly router = inject(Router);
  private readonly colors = readChartColors();

  protected readonly breakdown = httpResource<RevenueBreakdown>(() => ({
    url: '/api/metrics/breakdown',
    params: { ...this.filters.apiParams(), by: this.by() },
  }));

  protected readonly titleId = computed(() => `breakdown-${this.by()}-title`);
  protected readonly dimensionLabel = computed(() => ({ category: 'Category', region: 'Region', channel: 'Channel' })[this.by()]);

  protected readonly empty = computed(
    () => this.breakdown.hasValue() && this.breakdown.value().groups.every((g) => g.revenue === 0 && g.orders === 0),
  );

  protected readonly config = computed(() => breakdownChartConfig(this.breakdown.value()?.groups ?? [], this.colors));
  protected readonly description = computed(() => describeBreakdown(this.title(), this.breakdown.value()?.groups ?? []));

  /** Bars stay a readable thickness whatever the number of groups. */
  protected readonly height = computed(() => `${Math.max(8, (this.breakdown.value()?.groups.length ?? 0) * 2.5 + 3)}rem`);

  private readonly groups = computed(() => (this.breakdown.hasValue() ? this.breakdown.value().groups : []));

  /** Keyboard announcement for each bar. */
  protected readonly items = computed(() =>
    this.groups().map((g) => `${g.key}: ${formatCurrency(g.revenue)}, ${formatInteger(g.orders)} orders`),
  );

  /** Orders-grid query params for each bar: the current filter narrowed to that group. */
  protected readonly drills = computed(() => {
    const filter = this.filters.filter();
    return this.groups().map((g) => drillQueryParams(drillToGroup(filter, this.by(), g.key)));
  });

  protected open(index: number): void {
    const queryParams = this.drills()[index];
    if (queryParams) {
      void this.router.navigate(['/orders'], { queryParams });
    }
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