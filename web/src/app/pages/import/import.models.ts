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