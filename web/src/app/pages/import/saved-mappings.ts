import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ImportStore } from './import.store';

/** The user's saved column mappings: rename or delete them. They are offered automatically for matching files. */
@Component({
  selector: 'app-saved-mappings',
  imports: [DatePipe],
  template: `
    <section aria-labelledby="mappings-title">
      <h2 id="mappings-title">Saved column mappings</h2>
      @switch (store.savedMappingsStatus()) {
        @case ('loading') {
          <p class="muted" role="status">Loading saved mappings…</p>
        }
        @case ('error') {
          <p class="error" role="alert">
            The saved mappings could not be loaded.
            <button type="button" class="btn" (click)="store.loadMappings()">Try again</button>
          </p>
        }
        @default {
          @if (store.savedMappings().length === 0) {
            <p class="muted">None yet. When you import a file with other column names, you can save its mapping for next time.</p>
          } @else {
            <ul>
              @for (m of store.savedMappings(); track m.id) {
                <li>
                  @if (editing() === m.id) {
                    <form class="rename" (submit)="rename($event, m.id)">
                      <label [for]="'rename-' + m.id" class="visually-hidden">New name for {{ m.name }}</label>
                      <input [id]="'rename-' + m.id" type="text" maxlength="80" [value]="m.name" />
                      <button type="submit" class="btn primary">Save</button>
                      <button type="button" class="btn" (click)="editing.set(null)">Cancel</button>
                    </form>
                  } @else {
                    <span class="name">{{ m.name }}</span>
                    <span class="muted">{{ m.mapping.dateFormat }} · discounts as {{ m.mapping.discount === 'percent' ? 'percentages' : 'fractions' }} · updated {{ m.updatedAt | date: 'mediumDate' }}</span>
                    <span class="buttons">
                      <button type="button" class="btn" (click)="editing.set(m.id); renameError.set(null)">
                        Rename<span class="visually-hidden"> {{ m.name }}</span>
                      </button>
                      <button type="button" class="btn" (click)="store.deleteMapping(m.id)">
                        Delete<span class="visually-hidden"> {{ m.name }}</span>
                      </button>
                    </span>
                  }
                </li>
              }
            </ul>
            @if (renameError(); as message) {
              <p class="error" role="alert">{{ message }}</p>
            }
          }
        }
      }
    </section>
  `,
  styles: `
    section { margin-top: var(--space-6); }
    h2 { font-size: var(--font-size-lg); }
    ul { margin: var(--space-3) 0 0; padding: 0; list-style: none; }
    li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2) var(--space-3);
      padding: var(--space-3) 0;
      border-top: 1px solid var(--color-border);
    }
    .name { font-weight: var(--font-weight-medium); }
    .muted { color: var(--color-muted); font-size: var(--font-size-sm); }
    .buttons, .rename { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-left: auto; }
    .rename { margin-left: 0; }
    .rename input { min-height: 2.5rem; padding: 0 var(--space-2); border: 1px solid var(--color-border); border-radius: var(--radius-sm); font: inherit; }
    .error { color: var(--color-danger); }
  `,
})
export class SavedMappings {
  protected readonly store = inject(ImportStore);
  protected readonly editing = signal<string | null>(null);
  protected readonly renameError = signal<string | null>(null);

  protected rename(event: Event, id: string): void {
    event.preventDefault();
    const input = (event.target as HTMLFormElement).querySelector('input') as HTMLInputElement;
    const name = input.value.trim();
    if (name.length === 0) {
      this.renameError.set('Give the mapping a name.');
      return;
    }
    this.store.renameMapping(id, name).subscribe((error) => {
      this.renameError.set(error);
      if (error === null) {
        this.editing.set(null);
      }
    });
  }
}
