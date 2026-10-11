import { InjectionToken, computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { DEFAULT_FILTER, FilterState, resolveRange, sameFilter, toApiParams } from './filter-state';

/** Today's date; a token so tests can pin it. */
export const TODAY = new InjectionToken<() => Date>('TODAY', { providedIn: 'root', factory: () => () => new Date() });

/** The shared filter used by the dashboard (and, from M08, the orders grid). Mirrored in the URL by the page. */
export const FilterStore = signalStore(
  { providedIn: 'root' },
  withState<FilterState>(DEFAULT_FILTER),
  withComputed((store, today = inject(TODAY)) => {
    // Value equality: an update that leaves the filter unchanged must not look like a change to the widgets
    // (each new object would otherwise trigger another request).
    const filter = computed<FilterState>(() => ({
      preset: store.preset(),
      from: store.from(),
      to: store.to(),
      regions: store.regions(),
      channels: store.channels(),
      categories: store.categories(),
      granularity: store.granularity(),
    }), { equal: sameFilter });
    return {
      filter,
      range: computed(() => resolveRange(filter(), today())),
      apiParams: computed(() => toApiParams(filter(), today()), { equal: (a, b) => JSON.stringify(a) === JSON.stringify(b) }),
      isDefault: computed(() => sameFilter({ ...filter(), granularity: 'month' }, DEFAULT_FILTER)),
    };
  }),
  withMethods((store) => ({
    /** Replaces the whole filter (from the URL); no-op when nothing changed, so URL sync cannot loop. */
    replace(next: FilterState): void {
      if (!sameFilter(next, store.filter())) {
        patchState(store, next);
      }
    },
    update(changes: Partial<FilterState>): void {
      patchState(store, changes);
    },
    /** Clears filters but keeps the chosen chart granularity. */
    reset(): void {
      patchState(store, { ...DEFAULT_FILTER, granularity: store.granularity() });
    },
  })),
);