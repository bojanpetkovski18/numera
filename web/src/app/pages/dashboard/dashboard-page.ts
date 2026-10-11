import { Component } from '@angular/core';
import { syncFiltersWithUrl } from '../../core/filters/sync-filters-with-url';
import { FilterBar } from '../../shared/filters/filter-bar';
import { BreakdownChart } from './breakdown-chart';
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
  constructor() {
    syncFiltersWithUrl(true);
  }
}