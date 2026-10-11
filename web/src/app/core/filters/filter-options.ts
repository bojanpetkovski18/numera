/** GET /api/filter-options: values present in the data, plus its date range. */
export interface FilterOptions {
  readonly regions: readonly string[];
  readonly channels: readonly string[];
  readonly categories: readonly string[];
  readonly firstOrderDate: string | null;
  readonly lastOrderDate: string | null;
}