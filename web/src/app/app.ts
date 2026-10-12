import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { GlobalErrors } from './core/errors/global-errors';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    @if (errors.message(); as message) {
      <div class="global-error" role="alert">
        <p>{{ message }}</p>
        <div class="actions">
          <button type="button" class="btn" (click)="reload()">Reload page</button>
          <button type="button" class="btn" (click)="errors.dismiss()">Dismiss</button>
        </div>
      </div>
    }
    <router-outlet />
  `,
  styles: `
    .global-error {
      position: sticky;
      top: 0;
      z-index: 20;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2) var(--space-4);
      padding: var(--space-3) var(--space-4);
      background: #fdecea;
      border-bottom: 2px solid var(--color-danger);
      color: var(--color-ink);
    }
    p {
      margin: 0;
      flex: 1 1 20rem;
    }
    .actions {
      display: flex;
      gap: var(--space-2);
    }
  `,
})
export class App {
  protected readonly errors = inject(GlobalErrors);

  protected reload(): void {
    location.reload();
  }
}
