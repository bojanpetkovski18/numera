import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, linkedSignal, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, skip } from 'rxjs';
import { dashboardQueryParams } from '../../core/filters/drill-down';
import { DATE_PRESETS } from '../../core/filters/filter-state';
import { FilterStore } from '../../core/filters/filter.store';
import { syncFiltersWithUrl } from '../../core/filters/sync-filters-with-url';
import { formatCurrency, formatDate, formatDiscount, formatInteger } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { FilterBar } from '../../shared/filters/filter-bar';
import { FilterChips } from '../../shared/filters/filter-chips';
import {
  DEFAULT_GRID,
  GridState,
  OrderStatusValue,
  PAGE_SIZES,
  PageSize,
  SortField,
  effectiveSort,
  gridApiParams,
  gridFromQueryParams,
  gridToQueryParams,
  nextSort,
  sameGrid,
} from './grid-state';
import { OrdersPage as OrdersPageData, OrderRow } from './orders.models';

interface Column {
  readonly field: SortField;
  readonly label: string;
  readonly numeric?: boolean;
}

/** One active filter, shown as a removable chip. */
export type FilterChip =
  | { readonly kind: 'date'; readonly label: string }
  | { readonly kind: 'regions' | 'channels' | 'categories'; readonly value: string; readonly label: string }
  | { readonly kind: 'status'; readonly value: OrderStatusValue; readonly label: string }
  | { readonly kind: 'search'; readonly label: string };

export const SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-orders-page',
  imports: [FilterBar, FilterChips, RouterLink],
  templateUrl: './orders-page.html',
  styleUrl: './orders-page.css',
})
export class OrdersPage {
  private readonly route = inject(ActivatedRoute);
  protected readonly filters = inject(FilterStore);

  protected readonly columns: readonly Column[] = [
    { field: 'orderDate', label: 'Date' },
    { field: 'orderId', label: 'Order' },
    { field: 'customerId', label: 'Customer' },
    { field: 'region', label: 'Region' },
    { field: 'channel', label: 'Channel' },
    { field: 'productCategory', label: 'Category' },
    { field: 'productSku', label: 'SKU' },
    { field: 'quantity', label: 'Qty', numeric: true },
    { field: 'unitPrice', label: 'Unit price', numeric: true },
    { field: 'discount', label: 'Discount', numeric: true },
    { field: 'lineTotal', label: 'Line total', numeric: true },
    { field: 'status', label: 'Status' },
  ];
  protected readonly pageSizes = PAGE_SIZES;
  protected readonly statusOptions: readonly { value: OrderStatusValue; label: string }[] = [
    { value: 'completed', label: 'Completed' },
    { value: 'refunded', label: 'Refunded' },
    { value: 'cancelled', label: 'Cancelled' },
  ];

  /** Grid state as last set by the user or the URL. Read it through `view()`. */
  private readonly state = signal<GridState>(DEFAULT_GRID);
  /** The shared filter the current page number was chosen under. */
  private readonly pageFilterKey = signal('');
  private readonly filterKey = computed(() => JSON.stringify(this.filters.apiParams()));

  /**
   * The grid as shown and requested. When the shared filters change, the page falls back to 1 in the same
   * computation, so the request for the new filter is the only one sent (no request for a stale page number).
   */
  protected readonly grid = computed<GridState>(
    () => (this.pageFilterKey() === this.filterKey() ? this.state() : { ...this.state(), page: 1 }),
    { equal: sameGrid },
  );
  /** What is typed in the search box; applied to the grid after a pause. */
  protected readonly searchText = signal('');

  protected readonly orders = httpResource<OrdersPageData>(() => ({
    url: '/api/orders',
    params: { ...this.filters.apiParams(), ...gridApiParams(this.grid()) },
  }));

  /** The last page that loaded, so the table stays in place (dimmed) while the next one loads. */
  protected readonly shown = linkedSignal<OrdersPageData | undefined, OrdersPageData | undefined>({
    source: () => (this.orders.hasValue() ? this.orders.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly sort = computed(() => effectiveSort(this.grid()));
  protected readonly firstLoad = computed(() => this.orders.isLoading() && this.shown() === undefined);

  protected readonly rangeText = computed(() => {
    const page = this.shown();
    if (!page || page.totalCount === 0) {
      return '';
    }
    const first = (page.page - 1) * page.pageSize + 1;
    const last = Math.min(page.page * page.pageSize, page.totalCount);
    return first > page.totalCount
      ? `Page ${formatInteger(page.page)} is past the end (${formatInteger(page.totalCount)} order lines)`
      : `${formatInteger(first)}–${formatInteger(last)} of ${formatInteger(page.totalCount)} order lines`;
  });

  /** Every active filter, so it is clear why the grid shows these rows (for example after a drill-down). */
  protected readonly chips = computed<FilterChip[]>(() => {
    const filter = this.filters.filter();
    const grid = this.grid();
    const chips: FilterChip[] = [];
    const date = this.dateLabel();
    if (date) {
      chips.push({ kind: 'date', label: `Date: ${date}` });
    }
    const lists = [['regions', 'Region'], ['channels', 'Channel'], ['categories', 'Category']] as const;
    for (const [kind, name] of lists) {
      for (const value of filter[kind]) {
        chips.push({ kind, value, label: `${name}: ${value}` });
      }
    }
    for (const value of grid.statuses) {
      chips.push({ kind: 'status', value, label: `Status: ${this.statusOptions.find((s) => s.value === value)?.label ?? value}` });
    }
    if (grid.q) {
      chips.push({ kind: 'search', label: `Order ID contains: ${grid.q}` });
    }
    return chips;
  });

  /** The dashboard with the same filters (and the chart granularity last used there). */
  protected readonly dashboardParams = computed(() => dashboardQueryParams(this.filters.filter()));

  protected readonly chipLabels = computed(() => this.chips().map((c) => c.label));

  protected readonly caption = computed(() => {
    const { field, dir } = this.sort();
    const label = this.columns.find((c) => c.field === field)?.label ?? field;
    return `Order lines sorted by ${label}, ${dir === 'asc' ? 'ascending' : 'descending'}`;
  });

  constructor() {
    syncFiltersWithUrl(false, () => gridToQueryParams(this.grid()));

    // URL -> grid. Subscribed after the shared filter sync, so the store already holds the URL's filters and the
    // URL's page number is kept (it belongs to those filters).
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const fromUrl = gridFromQueryParams(params);
      // Compare with the stored state, not the displayed grid: after a filter change the display already shows
      // page 1 while the stored page is still the old one.
      if (!sameGrid(fromUrl, this.state())) {
        this.state.set(fromUrl);
      }
      this.pageFilterKey.set(untracked(this.filterKey));
      if (fromUrl.q !== this.searchText()) {
        this.searchText.set(fromUrl.q);
      }
    });


    // Search is applied after the user pauses typing.
    toObservable(this.searchText)
      .pipe(skip(1), debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged(), takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((text) => {
        const q = text.trim().slice(0, 40);
        if (q !== this.grid().q) {
          this.setGrid((g) => ({ ...g, q, page: 1 }));
        }
      });
  }

  /** Applies a change on top of what is shown, and ties the resulting page number to the current filter. */
  private setGrid(change: (current: GridState) => GridState): void {
    this.state.set(change(this.grid()));
    this.pageFilterKey.set(this.filterKey());
  }

  protected ariaSort(field: SortField): 'ascending' | 'descending' | 'none' {
    const { field: current, dir } = this.sort();
    return current !== field ? 'none' : dir === 'asc' ? 'ascending' : 'descending';
  }

  protected sortBy(field: SortField): void {
    this.setGrid((g) => nextSort(g, field));
  }

  protected setSortFromSelect(event: Event): void {
    const [field, dir] = (event.target as HTMLSelectElement).value.split(':') as [SortField, 'asc' | 'desc'];
    this.setGrid((g) => ({ ...g, sort: field, dir, page: 1 }));
  }

  protected setStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as OrderStatusValue | '';
    this.setGrid((g) => ({ ...g, statuses: value ? [value] : [], page: 1 }));
  }

  protected onSearchInput(event: Event): void {
    this.searchText.set((event.target as HTMLInputElement).value);
  }

  protected setPageSize(event: Event): void {
    const pageSize = Number((event.target as HTMLSelectElement).value) as PageSize;
    this.setGrid((g) => ({ ...g, pageSize, page: 1 }));
  }

  protected goTo(page: number): void {
    const total = this.shown()?.totalPages ?? 1;
    const target = Math.min(Math.max(1, page), Math.max(1, total));
    if (target !== this.grid().page) {
      this.setGrid((g) => ({ ...g, page: target }));
    }
  }

  protected removeChip(chip: FilterChip): void {
    switch (chip.kind) {
      case 'date':
        this.filters.update({ preset: 'all', from: null, to: null });
        break;
      case 'regions':
      case 'channels':
      case 'categories':
        this.filters.update({ [chip.kind]: this.filters[chip.kind]().filter((v) => v !== chip.value) });
        break;
      case 'status':
        this.setGrid((g) => ({ ...g, statuses: g.statuses.filter((s) => s !== chip.value), page: 1 }));
        break;
      case 'search':
        this.searchText.set('');
        this.setGrid((g) => ({ ...g, q: '', page: 1 }));
        break;
    }
  }

  /** "completed" or "completed or refunded", for the empty-state hint. */
  protected statusText(): string {
    return this.grid().statuses.join(' or ');
  }

  protected showAllStatuses(): void {
    this.setGrid((g) => ({ ...g, statuses: [], page: 1 }));
  }

  protected clearAll(): void {
    this.filters.reset(); // first, so the grid change below is tied to the cleared filter
    this.searchText.set('');
    this.setGrid((g) => ({ ...DEFAULT_GRID, pageSize: g.pageSize }));
  }

  protected cell(row: OrderRow, field: SortField): string {
    switch (field) {
      case 'orderDate': return formatDate(row.orderDate);
      case 'quantity': return formatInteger(row.quantity);
      case 'unitPrice': return formatCurrency(row.unitPrice);
      case 'discount': return formatDiscount(row.discount);
      case 'lineTotal': return formatCurrency(row.lineTotal);
      default: return String(row[field]);
    }
  }

  protected count(value: number): string {
    return formatInteger(value);
  }

  private dateLabel(): string | null {
    const preset = this.filters.preset();
    if (preset === 'all') {
      return null;
    }
    if (preset !== 'custom') {
      return DATE_PRESETS.find((p) => p.value === preset)?.label ?? preset;
    }
    const from = this.filters.from();
    const to = this.filters.to();
    if (from && to) {
      return from === to ? formatDate(from) : `${formatDate(from)} – ${formatDate(to)}`;
    }
    if (from || to) {
      return from ? `from ${formatDate(from)}` : `up to ${formatDate(to as string)}`;
    }
    return null;
  }

  protected message(error: unknown): string {
    return problemMessage(error, 'Try again.');
  }
}