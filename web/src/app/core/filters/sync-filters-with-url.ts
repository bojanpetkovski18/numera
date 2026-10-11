import { DestroyRef, effect, inject, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { fromQueryParams, toQueryParams } from './filter-state';
import { FilterStore } from './filter.store';

const SHARED_KEYS = ['preset', 'from', 'to', 'region', 'channel', 'category'] as const;

/**
 * Keeps the shared filter store and the current page's URL in step. Call from a routed component's constructor.
 *
 * - First visit: if the URL carries filter parameters (a shared link, a reload) they win; otherwise the filters
 *   already chosen on another screen are kept and written into this page's URL. So moving between the dashboard
 *   and the orders grid by the menu keeps the filters.
 * - Afterwards: back/forward and edited URLs update the store; store changes update the URL (replaceUrl).
 *
 * This is the only place that writes the page's query string. Pages with their own URL state (the grid's sort and
 * paging) pass it as `pageParams` so everything goes out in ONE navigation: two separate "merge" navigations in the
 * same tick are both computed from the old URL, and the second silently restores what the first removed.
 *
 * @param includeGranularity Whether this page owns the chart granularity (the dashboard does, the grid does not).
 * @param pageParams Extra, page-specific query params (a signal-reading function), written together with the filters.
 */
export function syncFiltersWithUrl(
  includeGranularity: boolean,
  pageParams: () => Record<string, string | string[] | null> = () => ({}),
): void {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  const filters = inject(FilterStore);
  const destroyRef = inject(DestroyRef);
  let first = true;

  route.queryParamMap.pipe(takeUntilDestroyed(destroyRef)).subscribe((params) => {
    const urlHasFilters = hasFilterParams(params, includeGranularity);
    if (first && !urlHasFilters) {
      first = false;
      return; // keep the store; the effect below writes it into the URL
    }
    first = false;
    const fromUrl = fromQueryParams(params);
    filters.replace(includeGranularity ? fromUrl : { ...fromUrl, granularity: filters.granularity() });
  });

  effect(() => {
    const all = toQueryParams(filters.filter());
    const queryParams = { ...(includeGranularity ? all : { ...all, granularity: null }), ...pageParams() };
    untracked(() => {
      void router.navigate([], { relativeTo: route, queryParams, queryParamsHandling: 'merge', replaceUrl: true });
    });
  });
}

function hasFilterParams(params: ParamMap, includeGranularity: boolean): boolean {
  return SHARED_KEYS.some((key) => params.has(key)) || (includeGranularity && params.has('granularity'));
}