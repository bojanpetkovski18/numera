import { InjectionToken } from '@angular/core';

/** Where the dashboard widgets get their figures, and whether a value opens the rows behind it. */
export interface DashboardSource {
  /** Base URL of the summary, timeseries, breakdown and drivers endpoints. */
  readonly base: string;
  /** False on a shared page: no links to the orders grid, which a shared link cannot open. */
  readonly drillDown: boolean;
}

/** The signed-in dashboard by default; the shared page provides its own. */
export const DASHBOARD_SOURCE = new InjectionToken<DashboardSource>('DASHBOARD_SOURCE', {
  providedIn: 'root',
  factory: () => ({ base: '/api/metrics', drillDown: true }),
});
