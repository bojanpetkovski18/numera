import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an HTTP error into a message for the user. Prefers the RFC 7807 `detail` (then `title`) sent by the API,
 * so server-side validation messages are shown as written.
 */
export function problemMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }
  if (error.status === 0) {
    return 'Could not reach the server. Check your connection and try again.';
  }
  const body: unknown = error.error;
  if (typeof body === 'object' && body !== null && !(body instanceof Blob)) {
    const { detail, title } = body as { detail?: unknown; title?: unknown };
    if (typeof detail === 'string' && detail.length > 0) {
      return detail;
    }
    if (typeof title === 'string' && title.length > 0) {
      return title;
    }
  }
  return fallback;
}