import { httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, linkedSignal, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { debounceTime, distinctUntilChanged, skip } from 'rxjs';
import { FilterStore } from '../../core/filters/filter.store';
import { syncFiltersWithUrl } from '../../core/filters/sync-filters-with-url';
import { formatCurrency, formatDate, formatDiscount, formatInteger } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { FilterBar } from '../../shared/filters/filter-bar';
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

export const SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-orders-page',
  imports: [FilterBar],
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

  protected message(error: unknown): string {
    return problemMessage(error, 'Try again.');
  }
}