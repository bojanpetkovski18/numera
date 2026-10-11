import { HttpErrorResponse } from '@angular/common/http';
import { problemMessage, problemMessageFromBlob } from './problem';

describe('problemMessage', () => {
  it('prefers the problem detail, then the title', () => {
    const withDetail = new HttpErrorResponse({ status: 422, error: { title: 'Import rejected', detail: 'Missing columns.' } });
    const withTitle = new HttpErrorResponse({ status: 404, error: { title: 'Import not found' } });

    expect(problemMessage(withDetail, 'fallback')).toBe('Missing columns.');
    expect(problemMessage(withTitle, 'fallback')).toBe('Import not found');
  });

  it('explains a network failure', () => {
    expect(problemMessage(new HttpErrorResponse({ status: 0 }), 'fallback')).toContain('Could not reach the server');
  });

  it('falls back for anything else', () => {
    expect(problemMessage(new Error('boom'), 'fallback')).toBe('fallback');
    expect(problemMessage(new HttpErrorResponse({ status: 500, error: 'oops' }), 'fallback')).toBe('fallback');
  });

  it('reads problem details delivered as a Blob', async () => {
    const blob = new Blob([JSON.stringify({ title: 'Import not found', detail: 'No import exists with that id.' })], {
      type: 'application/problem+json',
    });

    const message = await problemMessageFromBlob(new HttpErrorResponse({ status: 404, error: blob }), 'fallback');

    expect(message).toBe('No import exists with that id.');
  });

  it('falls back when a Blob body is not JSON', async () => {
    const message = await problemMessageFromBlob(
      new HttpErrorResponse({ status: 500, error: new Blob(['<html>']) }),
      'fallback',
    );

    expect(message).toBe('fallback');
  });
});