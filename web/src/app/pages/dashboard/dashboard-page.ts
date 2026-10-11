import { Component, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { fromQueryParams, toQueryParams } from '../../core/filters/filter-state';
import { FilterStore } from '../../core/filters/filter.store';
import { BreakdownChart } from './breakdown-chart';
import { FilterBar } from './filter-bar';
import { KpiCards } from './kpi-cards';
import { RevenueChart } from './revenue-chart';

@Component({
  selector: 'app-dashboard-page',
  imports: [BreakdownChart, FilterBar, KpiCards, RevenueChart],
  template: `
    <header class="page-header">
      <h1>Dashboard</h1>
      <p>Sales performance for Harbor &amp; Pine Coffee. Revenue counts completed orders after discounts.</p>
    </header>

    <div class="layout">
      <app-filter-bar />
      <app-kpi-cards />
      <app-revenue-chart />
      <div class="breakdowns">
        <app-breakdown-chart by="category" title="Revenue by category" />
        <app-breakdown-chart by="region" title="Revenue by region" />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      max-width: 80rem;
    }
    .page-header h1 {
      font-size: var(--font-size-xl);
    }
    .page-header p {
      margin: var(--space-2) 0 var(--space-5);
      color: var(--color-muted);
    }
    .layout {
      display: grid;
      gap: var(--space-4);
    }
    .breakdowns {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-4);
    }
    @media (max-width: 900px) {
      .breakdowns {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class DashboardPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly filters = inject(FilterStore);

  constructor() {
    // URL -> store: on load, refresh, back/forward and shared links.
    this.route.queryParamMap
      .pipe(takeUntilDestroyed())
      .subscribe((params) => this.filters.replace(fromQueryParams(params)));

    // Store -> URL: replaceUrl so filter tweaks do not flood the browser history.
    effect(() => {
      const queryParams = toQueryParams(this.filters.filter());
      untracked(() => {
        void this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
      });
    });
  }
}