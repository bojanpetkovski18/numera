import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Any unknown address. Outside the signed-in shell, so it also works for a stale link before signing in. */
@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  template: `
    <main class="page">
      <p class="brand">Numera</p>
      <h1>Page not found</h1>
      <p>There is no page at this address. The link may be mistyped or out of date.</p>
      <a class="btn" routerLink="/dashboard">Go to the dashboard</a>
    </main>
  `,
  styles: `
    .page {
      display: grid;
      justify-items: start;
      gap: var(--space-3);
      max-width: 32rem;
      margin: 0 auto;
      padding: var(--space-7) var(--space-4);
    }
    .brand {
      margin: 0;
      padding-left: var(--space-3);
      border-left: 4px solid var(--color-accent);
      font-weight: var(--font-weight-bold);
      letter-spacing: 0.04em;
    }
    h1 {
      margin: 0;
      font-size: var(--font-size-xl);
    }
    p {
      margin: 0;
      color: var(--color-muted);
    }
    .btn {
      margin-top: var(--space-2);
      text-decoration: none;
    }
  `,
})
export class NotFoundPage {}
