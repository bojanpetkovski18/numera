import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { fileNameFromDisposition } from '../../core/http/content-disposition';
import { FileSaver } from '../../core/http/file-saver';
import { problemMessageFromBlob } from '../../core/http/problem';
import { formatInteger } from '../../core/format/format';

/** The server's export limit (ExportOrdersHandler.MaxRows). The server decides; this only explains it up front. */
export const EXPORT_MAX_ROWS = 100_000;

type ExportState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'busy' }
  | { readonly kind: 'done'; readonly key: string; readonly text: string }
  | { readonly kind: 'error'; readonly key: string; readonly text: string };

/**
 * Downloads the grid's current view as CSV. Fetched through HttpClient (not a plain link) so the request carries
 * the bearer token; the browser then saves the received file.
 */
@Component({
  selector: 'app-export-button',
  template: `
    <button
      type="button"
      class="btn"
      (click)="start()"
      [disabled]="busy() || tooMany()"
      [attr.aria-busy]="busy()"
      [attr.aria-describedby]="tooMany() ? limitId : null"
    >
      @if (busy()) {
        <span class="spinner" aria-hidden="true"></span> Exporting…
      } @else {
        Export CSV
      }
    </button>
    @if (tooMany()) {
      <p class="note" [id]="limitId">Narrow the filters to export: a file can hold at most {{ max }} order lines.</p>
    }
    <p class="note" role="status">{{ busy() ? 'Preparing the CSV file…' : success() }}</p>
    @if (failure(); as text) {
      <p class="note error" role="alert">{{ text }}</p>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: var(--space-1) var(--space-3);
    }
    .note {
      margin: 0;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
    }
    .note:empty {
      display: none;
    }
    .error {
      color: var(--color-danger);
    }
    .spinner {
      width: 0.9rem;
      height: 0.9rem;
      margin-right: var(--space-2);
      border: 2px solid currentColor;
      border-right-color: transparent;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
    @media (prefers-reduced-motion: reduce) {
      .spinner {
        animation: none;
      }
    }
  `,
})
export class ExportButton {
  /** Filter, search, status and sort, exactly as the grid requests them (without paging). */
  readonly params = input.required<Record<string, string | string[]>>();
  /** Rows the grid currently reports, to explain the limit before the user tries. */
  readonly rowCount = input<number | null>(null);

  private readonly http = inject(HttpClient);
  private readonly saver = inject(FileSaver);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly max = formatInteger(EXPORT_MAX_ROWS);
  protected readonly limitId = 'export-limit';
  private readonly state = signal<ExportState>({ kind: 'idle' });
  private readonly key = computed(() => JSON.stringify(this.params()));

  protected readonly busy = computed(() => this.state().kind === 'busy');
  protected readonly tooMany = computed(() => (this.rowCount() ?? 0) > EXPORT_MAX_ROWS);
  /** Messages belong to the view they were made for; after a filter change they would mislead, so they hide. */
  protected readonly success = computed(() => {
    const state = this.state();
    return state.kind === 'done' && state.key === this.key() ? state.text : '';
  });
  protected readonly failure = computed(() => {
    const state = this.state();
    return state.kind === 'error' && state.key === this.key() ? state.text : null;
  });

  protected start(): void {
    if (this.busy()) {
      return;
    }
    const key = this.key();
    this.state.set({ kind: 'busy' });
    this.http
      .get('/api/orders/export', { params: this.params(), responseType: 'blob', observe: 'response' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          const fileName = fileNameFromDisposition(response.headers.get('Content-Disposition')) ?? 'numera-orders.csv';
          this.saver.save(response.body ?? new Blob([]), fileName);
          const rows = Number(response.headers.get('X-Row-Count'));
          const what = Number.isFinite(rows) && response.headers.has('X-Row-Count') ? `${formatInteger(rows)} order lines` : 'The orders';
          this.state.set({ kind: 'done', key, text: `${what} exported to ${fileName}.` });
        },
        error: async (error: unknown) => {
          const text = await problemMessageFromBlob(error, 'The export could not be downloaded. Try again.');
          this.state.set({ kind: 'error', key, text: `Export failed. ${text}` });
        },
      });
  }
}
