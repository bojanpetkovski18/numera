/** Grid-only state (the shared filters live in FilterStore). Mirrored in the URL. */
export type SortField =
  | 'orderDate'
  | 'orderId'
  | 'customerId'
  | 'region'
  | 'channel'
  | 'productCategory'
  | 'productSku'
  | 'quantity'
  | 'unitPrice'
  | 'discount'
  | 'lineTotal'
  | 'status';

export type SortDir = 'asc' | 'desc';
export type OrderStatusValue = 'completed' | 'refunded' | 'cancelled';

export const PAGE_SIZES = [25, 50, 100] as const;
export type PageSize = (typeof PAGE_SIZES)[number];

export interface GridState {
  readonly statuses: readonly OrderStatusValue[];
  readonly q: string;
  /** null means the server default: newest first. */
  readonly sort: SortField | null;
  readonly dir: SortDir;
  readonly page: number;
  readonly pageSize: PageSize;
}

export const DEFAULT_GRID: GridState = { statuses: [], q: '', sort: null, dir: 'desc', page: 1, pageSize: 25 };

export const SORT_FIELDS: readonly SortField[] = [
  'orderDate', 'orderId', 'customerId', 'region', 'channel', 'productCategory',
  'productSku', 'quantity', 'unitPrice', 'discount', 'lineTotal', 'status',
];

const NUMERIC_OR_DATE = new Set<SortField>(['orderDate', 'quantity', 'unitPrice', 'discount', 'lineTotal']);
const STATUSES = new Set<OrderStatusValue>(['completed', 'refunded', 'cancelled']);

/** Effective sort, resolving the default. */
export function effectiveSort(state: GridState): { field: SortField; dir: SortDir } {
  return state.sort === null ? { field: 'orderDate', dir: 'desc' } : { field: state.sort, dir: state.dir };
}

/** Clicking a header: the same column flips direction; a new column starts biggest/newest first for numbers and dates, A to Z for text. */
export function nextSort(state: GridState, field: SortField): GridState {
  const current = effectiveSort(state);
  const dir: SortDir = current.field === field ? (current.dir === 'asc' ? 'desc' : 'asc') : NUMERIC_OR_DATE.has(field) ? 'desc' : 'asc';
  return { ...state, sort: field, dir, page: 1 };
}

export interface QueryParamSource {
  get(name: string): string | null;
  getAll(name: string): string[];
}

/** Reads grid state from the URL; anything invalid falls back to the default. */
export function gridFromQueryParams(params: QueryParamSource): GridState {
  const sort = params.get('sort') as SortField | null;
  const validSort = sort !== null && SORT_FIELDS.includes(sort) ? sort : null;
  const dir = params.get('dir');
  const page = Number(params.get('page'));
  const size = Number(params.get('pageSize')) as PageSize;
  return {
    statuses: [...new Set(params.getAll('status').filter((s): s is OrderStatusValue => STATUSES.has(s as OrderStatusValue)))],
    q: (params.get('q') ?? '').trim().slice(0, 40),
    sort: validSort,
    dir: validSort === null ? 'desc' : dir === 'asc' || dir === 'desc' ? dir : 'asc',
    page: Number.isInteger(page) && page >= 1 ? page : 1,
    pageSize: PAGE_SIZES.includes(size) ? size : 25,
  };
}

/** Grid state as URL params; defaults are omitted (null removes the key when merged by the router). */
export function gridToQueryParams(state: GridState): Record<string, string | string[] | null> {
  return {
    status: state.statuses.length ? [...state.statuses] : null,
    q: state.q || null,
    sort: state.sort,
    dir: state.sort === null ? null : state.dir,
    page: state.page > 1 ? String(state.page) : null,
    pageSize: state.pageSize === 25 ? null : String(state.pageSize),
  };
}

/** Grid state as /api/orders parameters (added to the shared filter parameters). */
export function gridApiParams(state: GridState): Record<string, string | string[]> {
  const params: Record<string, string | string[]> = { page: String(state.page), pageSize: String(state.pageSize) };
  if (state.statuses.length) params['status'] = [...state.statuses];
  if (state.q) params['q'] = state.q;
  if (state.sort !== null) {
    params['sort'] = state.sort;
    params['dir'] = state.dir;
  }
  return params;
}

/** /api/orders/export parameters: the grid's search, status and sort without paging, so the file holds every page. */
export function gridExportParams(state: GridState): Record<string, string | string[]> {
  const { page: _page, pageSize: _pageSize, ...rest } = gridApiParams(state);
  return rest;
}

export function sameGrid(a: GridState, b: GridState): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}