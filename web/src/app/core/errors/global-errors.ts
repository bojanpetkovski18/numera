import { ErrorHandler, Injectable, inject, signal } from '@angular/core';

const CHUNK_LOAD_ERROR = /Loading chunk|dynamically imported module|Importing a module script failed/i;

/** The last unexpected error, as a message for the banner in the app root. */
@Injectable({ providedIn: 'root' })
export class GlobalErrors {
  readonly message = signal<string | null>(null);

  report(error: unknown): void {
    this.message.set(messageFor(error));
  }

  dismiss(): void {
    this.message.set(null);
  }
}

/**
 * Catches errors no screen handled (each screen shows its own loading and error states for expected failures).
 * They are still logged for developers, and the user gets a calm message with a way out instead of a page that
 * silently stops working.
 */
@Injectable()
export class AppErrorHandler implements ErrorHandler {
  private readonly errors = inject(GlobalErrors);

  handleError(error: unknown): void {
    console.error(error); // the one deliberate console call: unexpected errors must stay visible to developers
    this.errors.report(error);
  }
}

export function messageFor(error: unknown): string {
  const text = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  return CHUNK_LOAD_ERROR.test(text)
    ? 'Part of the app could not be loaded. The connection may have dropped or a new version was released. Reload the page to continue.'
    : 'Something went wrong on this page. Reload to continue. If it keeps happening, sign out and sign in again.';
}
