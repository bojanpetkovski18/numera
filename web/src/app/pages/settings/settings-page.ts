import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { formatInteger } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { ImportStore } from '../import/import.store';

interface DemoResetResponse {
  readonly orderLines: number;
  readonly completedAt: string;
}

type ResetState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'busy' }
  | { readonly kind: 'done'; readonly text: string }
  | { readonly kind: 'error'; readonly text: string };

@Component({
  selector: 'app-settings-page',
  template: `
    <header class="page-header">
      <h1>Settings</h1>
      <p>Options for this demo workspace.</p>
    </header>

    <section class="card" aria-labelledby="reset-title">
      <h2 id="reset-title">Demo data</h2>
      <p>
        Put the demo back the way it started. Imported orders and the import history are removed, and the original
        200,000 fictional order lines are loaded again. It takes a few seconds, and everyone using the demo sees the
        result.
      </p>
      <!-- aria-disabled rather than disabled while busy, so keyboard focus stays on the button. -->
      <button #opener type="button" class="btn danger" (click)="openConfirm()" [attr.aria-disabled]="busy()" [attr.aria-busy]="busy()">
        {{ busy() ? 'Resetting…' : 'Reset demo data' }}
      </button>

      <p class="message" role="status">{{ statusText() }}</p>
      @if (errorText(); as text) {
        <p class="message error" role="alert">{{ text }}</p>
      }
    </section>

    <dialog #dialog aria-labelledby="confirm-title" aria-describedby="confirm-text" (close)="dialogClosed()">
      <h2 id="confirm-title">Reset the demo data?</h2>
      <p id="confirm-text">
        Imported orders and the import history will be deleted, and the original data loaded again. This cannot be
        undone.
      </p>
      <div class="actions">
        <!-- Focus starts on Cancel: the safe choice for a destructive action. -->
        <button type="button" class="btn" autofocus (click)="dialog.close('cancel')">Cancel</button>
        <button type="button" class="btn danger" (click)="dialog.close('confirm')">Reset data</button>
      </div>
    </dialog>
  `,
  styles: `
    :host {
      display: block;
      max-width: 48rem;
    }
    .page-header h1 {
      font-size: var(--font-size-xl);
    }
    .page-header p {
      margin: var(--space-2) 0 var(--space-5);
      color: var(--color-muted);
    }
    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--font-size-lg);
    }
    .card p {
      margin: 0 0 var(--space-4);
      color: var(--color-muted);
    }
    .card .message {
      margin: var(--space-3) 0 0;
      color: var(--color-ink);
    }
    .message:empty {
      display: none;
    }
    .card .error {
      color: var(--color-danger);
    }
    dialog {
      width: min(28rem, calc(100vw - 2rem));
      padding: var(--space-5);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
      color: var(--color-ink);
    }
    dialog::backdrop {
      background: rgb(0 0 0 / 0.45);
    }
    dialog p {
      margin: 0 0 var(--space-5);
      color: var(--color-muted);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--space-2);
    }
  `,
})
export class SettingsPage {
  private readonly http = inject(HttpClient);
  private readonly imports = inject(ImportStore);
  private readonly destroyRef = inject(DestroyRef);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly opener = viewChild.required<ElementRef<HTMLButtonElement>>('opener');

  private readonly state = signal<ResetState>({ kind: 'idle' });
  protected readonly busy = computed(() => this.state().kind === 'busy');
  protected readonly statusText = computed(() => {
    const state = this.state();
    return state.kind === 'busy' ? 'Resetting the demo data. This takes a few seconds…' : state.kind === 'done' ? state.text : '';
  });
  protected readonly errorText = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.text : null;
  });

  protected openConfirm(): void {
    if (!this.busy()) {
      this.dialog().nativeElement.showModal();
    }
  }

  /** Runs on every close: a button, Escape, or the form method. Only an explicit "confirm" resets. */
  protected dialogClosed(): void {
    const dialog = this.dialog().nativeElement;
    const confirmed = dialog.returnValue === 'confirm';
    dialog.returnValue = '';
    this.opener().nativeElement.focus();
    if (confirmed) {
      this.reset();
    }
  }

  private reset(): void {
    this.state.set({ kind: 'busy' });
    this.http
      .post<DemoResetResponse>('/api/demo/reset', null)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.imports.reset(); // a finished upload shown on the Import screen no longer describes the data
          const time = new Date(result.completedAt).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
          this.state.set({ kind: 'done', text: `Demo data reset at ${time}: ${formatInteger(result.orderLines)} order lines loaded.` });
        },
        error: (error: unknown) => {
          this.state.set({ kind: 'error', text: `The demo data was not reset. ${problemMessage(error, 'Try again.')}` });
        },
      });
  }
}
