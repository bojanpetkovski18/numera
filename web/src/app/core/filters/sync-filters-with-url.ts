import { DestroyRef, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { NO_DASHBOARD_SETTINGS, fromQueryParams, toQueryParams } from './filter-state';
import { FilterStore } from './filter.store';

const SHARED_KEYS = ['preset', 'from', 'to', 'region', 'channel', 'category'] as const;
const DASHBOARD_KEYS = ['granularity', 'compare'] as const;

/**
 * Keeps the shared filter store and the current page's URL in step. Call from a routed component's constructor.
 *
 * - First visit: if the URL carries filter parameters (a shared link, a reload, a drill-down) or the visit is a
 *   back/forward navigation, the URL wins; otherwise the filters already chosen on another screen are kept and
 *   written into this page's URL. So moving between the dashboard and the orders grid by the menu keeps the filters.
 * - Afterwards: back/forward and edited URLs update the store; store changes update the URL (replaceUrl).
 *
 * This is the only place that writes the page's query string. Pages with their own URL state (the grid's sort and
 * paging) pass it as `pageParams` so everything goes out in ONE navigation: two separate "merge" navigations in the
 * same tick are both computed from the old URL, and the second silently restores what the first removed.
 *
 * @param ownsDashboardSettings Whether this page owns the dashboard-only settings, the chart granularity and the
 *   comparison mode (the dashboard does, the grid does not). Other pages keep them in the store but not in their URL.
 * @param pageParams Extra, page-specific query params (a signal-reading function), written together with the filters.
 */
export function syncFiltersWithUrl(
  ownsDashboardSettings: boolean,
  pageParams: () => Record<string, string | string[] | null> = () => ({}),
): void {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  const filters = inject(FilterStore);
  const destroyRef = inject(DestroyRef);
  // Back/forward restores the page as it was, even a URL without filters: after a drill-down from the default
  // dashboard, "back" must not show the dashboard with the drilled filters still applied.
  const restoringHistory = router.currentNavigation()?.trigger === 'popstate';
  let first = true;

  route.queryParamMap.pipe(takeUntilDestroyed(destroyRef)).subscribe((params) => {
    const urlHasFilters = hasFilterParams(params, ownsDashboardSettings);
    if (first && !urlHasFilters && !restoringHistory) {
      first = false;
      return; // keep the store; the effect below writes it into the URL
    }
    first = false;
    const fromUrl = fromQueryParams(params);
    filters.replace(ownsDashboardSettings ? fromUrl : { ...fromUrl, granularity: filters.granularity(), compare: filters.compare() });
  });

  effect(() => {
    const all = toQueryParams(filters.filter());
    const queryParams = { ...(ownsDashboardSettings ? all : { ...all, ...NO_DASHBOARD_SETTINGS }), ...pageParams() };
    untracked(() => {
      // scroll: 'manual' keeps the page where it is: this only records the state of the page being looked at.
      void router.navigate([], { relativeTo: route, queryParams, queryParamsHandling: 'merge', replaceUrl: true, scroll: 'manual' });
    });
  });
}

function hasFilterParams(params: ParamMap, ownsDashboardSettings: boolean): boolean {
  return SHARED_KEYS.some((key) => params.has(key)) || (ownsDashboardSettings && DASHBOARD_KEYS.some((key) => params.has(key)));
}