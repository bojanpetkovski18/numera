/** Mirrors the API's import job summary. */
export interface ImportJob {
  readonly id: string;
  readonly fileName: string;
  readonly status: 'pending' | 'processing' | 'completed' | 'failed';
  readonly totalRows: number;
  readonly importedRows: number;
  readonly rejectedRows: number;
  readonly warnings: readonly string[];
  readonly failureReason: string | null;
  readonly createdAt: string;
  readonly completedAt: string | null;
}

export type UploadEvent =
  | { readonly kind: 'progress'; readonly percent: number }
  | { readonly kind: 'done'; readonly job: ImportJob };

export const MAX_FILE_BYTES = 10 * 1024 * 1024;

/** Client-side pre-check; the server remains authoritative. Returns an error message, or null when acceptable. */
export function checkFile(file: { readonly name: string; readonly size: number }): string | null {
  if (!file.name.toLowerCase().endsWith('.csv')) {
    return 'Choose a .csv file.';
  }
  if (file.size === 0) {
    return 'The file is empty.';
  }
  if (file.size > MAX_FILE_BYTES) {
    return 'The file is larger than 10 MB.';
  }
  return null;
}
/** Numera's fields, in the order the mapping step lists them, with a readable label. */
export const IMPORT_FIELDS: readonly { readonly name: string; readonly label: string }[] = [
  { name: 'order_id', label: 'Order ID' },
  { name: 'order_date', label: 'Order date' },
  { name: 'customer_id', label: 'Customer ID' },
  { name: 'region', label: 'Region' },
  { name: 'channel', label: 'Channel' },
  { name: 'product_sku', label: 'Product SKU' },
  { name: 'product_category', label: 'Product category' },
  { name: 'quantity', label: 'Quantity' },
  { name: 'unit_price', label: 'Unit price' },
  { name: 'discount', label: 'Discount' },
  { name: 'status', label: 'Status' },
];

export const DATE_FORMATS = ['yyyy-MM-dd', 'dd/MM/yyyy', 'MM/dd/yyyy', 'dd.MM.yyyy'] as const;
export type DateFormat = (typeof DATE_FORMATS)[number];
export type DiscountScale = 'fraction' | 'percent';

/** How a file's columns map to Numera's fields; the shape the API accepts and returns. */
export interface ImportMapping {
  /** Numera field to source column; null while unmapped (never sent like that). */
  readonly columns: Readonly<Record<string, string | null>>;
  readonly dateFormat: DateFormat;
  readonly discount: DiscountScale;
}

export interface SavedMapping {
  readonly id: string;
  readonly name: string;
  readonly mapping: ImportMapping;
  readonly sourceHeaders: readonly string[];
  readonly updatedAt: string;
}

/** The header and first rows of a file, with a suggested (or saved) mapping. */
export interface ImportPreview {
  readonly headers: readonly string[];
  readonly rows: readonly (readonly string[])[];
  /** The file already uses Numera's column names: it can be imported as it is. */
  readonly matchesNumeraFormat: boolean;
  readonly suggestion: ImportMapping;
  readonly dateFormatAmbiguous: boolean;
  readonly unmapped: readonly string[];
  readonly savedMapping: SavedMapping | null;
}

/** What the mapping step hands back: the mapping, and a name to save it under (or null). */
export interface MappingChoice {
  readonly mapping: ImportMapping;
  readonly saveAs: string | null;
}
