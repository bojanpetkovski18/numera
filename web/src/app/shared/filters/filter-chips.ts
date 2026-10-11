import { Component, ElementRef, Injector, afterNextRender, inject, input, output, viewChild } from '@angular/core';

/**
 * Active filters as removable chips, so it is clear why a list shows the rows it does. Removing a chip moves the
 * focus to the label in front of the chips, because the button that had it is gone.
 */
@Component({
  selector: 'app-filter-chips',
  template: `
    <p #label class="label" tabindex="-1">{{ chips().length ? 'Filtered by' : emptyText() }}</p>
    @if (chips().length) {
      <ul class="chips">
        @for (chip of chips(); track chip; let i = $index) {
          <li class="chip">
            <span>{{ chip }}</span>
            <button type="button" class="remove" [attr.aria-label]="'Remove filter ' + chip" (click)="onRemove(i)">
              <span aria-hidden="true">×</span>
            </button>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }
    .label {
      margin: 0;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .chip {
      display: inline-flex;
      align-items: center;
      padding-left: var(--space-3);
      border: 1px solid var(--color-primary);
      border-radius: 999px;
      color: var(--color-primary);
      font-size: var(--font-size-sm);
    }
    .remove {
      min-width: 2rem;
      min-height: 2rem;
      border: 0;
      border-radius: 999px;
      background: transparent;
      color: inherit;
      font: inherit;
      font-size: 1.1rem;
      cursor: pointer;
    }
    .remove:hover {
      background: var(--color-bg);
    }
  `,
})
export class FilterChips {
  /** Chip labels, for example "Region: Europe". */
  readonly chips = input.required<readonly string[]>();
  readonly emptyText = input('No filters');
  /** Index of the chip whose remove button was pressed. */
  readonly remove = output<number>();

  private readonly label = viewChild.required<ElementRef<HTMLElement>>('label');
  private readonly injector = inject(Injector);

  protected onRemove(index: number): void {
    this.remove.emit(index);
    afterNextRender(() => this.label().nativeElement.focus(), { injector: this.injector });
  }
}
