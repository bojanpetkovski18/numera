import { Component, inject, signal } from '@angular/core';
import { formatDate } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { ShareLink, ViewsApi } from '../../core/views/views-api';

/** The user's share links that still work, with a way to revoke each one at once. */
@Component({
  selector: 'app-shared-links',
  template: `
    <section class="card" aria-labelledby="links-title">
      <h2 id="links-title">Shared links</h2>
      <p>Read-only links to your saved views. Anyone with a link sees that view's figures, without signing in, until it expires or you revoke it.</p>
      @switch (status()) {
        @case ('loading') {
          <p class="muted" role="status">Loading shared links…</p>
        }
        @case ('error') {
          <p class="error" role="alert">Shared links could not be loaded. <button type="button" class="btn" (click)="load()">Try again</button></p>
        }
        @default {
          @if (links().length === 0) {
            <p class="muted">No active links. Create one from a saved view on the dashboard.</p>
          } @else {
            <ul>
              @for (link of links(); track link.id) {
                <li>
                  <span class="name">{{ link.viewName }}</span>
                  <span class="muted">Created {{ date(link.createdAt) }} · works until {{ date(link.expiresAt) }}</span>
                  <button type="button" class="btn" (click)="revoke(link)">Revoke<span class="visually-hidden"> link to {{ link.viewName }}</span></button>
                </li>
              }
            </ul>
          }
        }
      }
      <p class="muted" role="status">{{ notice() }}</p>
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }
    </section>
  `,
  styles: `
    :host { display: block; margin-bottom: var(--space-5); }
    ul { margin: 0; padding: 0; list-style: none; }
    li { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-3); padding: var(--space-3) 0; border-top: 1px solid var(--color-border); }
    .name { font-weight: var(--font-weight-medium); }
    li .btn { margin-left: auto; }
    .muted { color: var(--color-muted); font-size: var(--font-size-sm); }
    .muted:empty { display: none; }
    .error { color: var(--color-danger); }
  `,
})
export class SharedLinks {
  private readonly api = inject(ViewsApi);
  protected readonly links = signal<readonly ShareLink[]>([]);
  protected readonly status = signal<'loading' | 'loaded' | 'error'>('loading');
  protected readonly notice = signal('');
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.status.set('loading');
    this.api.listShares().subscribe({
      next: (links) => {
        this.links.set(links);
        this.status.set('loaded');
      },
      error: () => this.status.set('error'),
    });
  }

  protected revoke(link: ShareLink): void {
    this.error.set(null);
    this.api.revoke(link.id).subscribe({
      next: () => {
        this.links.set(this.links().filter((l) => l.id !== link.id));
        this.notice.set(`The link to ${link.viewName} no longer works.`);
      },
      error: (error: unknown) => this.error.set(problemMessage(error, 'The link could not be revoked. Try again.')),
    });
  }

  protected date(value: string): string {
    return formatDate(value.slice(0, 10));
  }
}
