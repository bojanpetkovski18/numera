import { Component, inject } from '@angular/core';
import { FilterStore } from '../../core/filters/filter.store';
import { syncFiltersWithUrl } from '../../core/filters/sync-filters-with-url';
import { FilterBar } from '../../shared/filters/filter-bar';
import { BreakdownChart } from './breakdown-chart';
import { KpiCards } from './kpi-cards';
import { RevenueChart } from './revenue-chart';
import { ViewsBar } from './views-bar';
import { WhatChanged } from './what-changed';

@Component({
  selector: 'app-dashboard-page',
  imports: [BreakdownChart, FilterBar, KpiCards, RevenueChart, ViewsBar, WhatChanged],
  template: `
    <header class="page-header">
      <h1>Dashboard</h1>
      <p>Sales performance for Harbor &amp; Pine Coffee. Revenue counts completed orders after discounts.</p>
    </header>
    <app-views-bar class="views" />

    <div class="layout">
      <app-filter-bar />
      <app-kpi-cards />
      @if (filters.compareParam()) {
        <app-what-changed />
      }
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
      margin: var(--space-2) 0 var(--space-4);
      color: var(--color-muted);
    }
    .views {
      margin-bottom: var(--space-4);
    }
    .layout {
      display: grid;
      gap: var(--space-4);
    }
    /* Grid items default to min-width: auto, so a wide data table would widen the page instead of scrolling. */
    .layout > *,
    .breakdowns > * {
      min-width: 0;
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
  protected readonly filters = inject(FilterStore);

  constructor() {
    syncFiltersWithUrl(true);
  }
}