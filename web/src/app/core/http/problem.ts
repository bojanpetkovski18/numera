import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an HTTP error into a message for the user. Prefers the RFC 7807 `detail` (then `title`) sent by the API,
 * so server-side validation messages are shown as written.
 */
export function problemMessage(error: unknown, fallback: string): string {
  return messageFrom(error, error instanceof HttpErrorResponse ? error.error : undefined, fallback);
}

/**
 * Same as {@link problemMessage}, for requests made with `responseType: 'blob'`, where the problem-details JSON
 * arrives as a Blob that has to be read first.
 */
export async function problemMessageFromBlob(error: unknown, fallback: string): Promise<string> {
  if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
    try {
      const parsed: unknown = JSON.parse(await error.error.text());
      return messageFrom(error, parsed, fallback);
    } catch {
      return messageFrom(error, undefined, fallback);
    }
  }
  return problemMessage(error, fallback);
}

function messageFrom(error: unknown, body: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }
  if (error.status === 0) {
    return 'Could not reach the server. Check your connection and try again.';
  }
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