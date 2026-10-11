import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { DATE_PRESETS, DatePreset } from '../../core/filters/filter-state';
import { FilterStore } from '../../core/filters/filter.store';
import { formatDate } from '../../core/format/format';
import { FilterOptions } from '../../core/filters/filter-options';

type ListField = 'regions' | 'channels' | 'categories';

@Component({
  selector: 'app-filter-bar',
  template: `
    <form class="card bar" aria-label="Dashboard filters" (submit)="$event.preventDefault()">
      <div class="field">
        <label for="f-preset">Date range</label>
        <select id="f-preset" (change)="setPreset($event)">
          @for (p of presets; track p.value) {
            <option [value]="p.value" [selected]="p.value === filters.preset()">{{ p.label }}</option>
          }
        </select>
      </div>

      @if (filters.preset() === 'custom') {
        <div class="field">
          <label for="f-from">From</label>
          <input id="f-from" type="date" [value]="filters.from() ?? ''" [max]="filters.to() ?? ''" (change)="setDate('from', $event)" />
        </div>
        <div class="field">
          <label for="f-to">To</label>
          <input id="f-to" type="date" [value]="filters.to() ?? ''" [min]="filters.from() ?? ''" (change)="setDate('to', $event)" />
        </div>
      }

      @for (f of listFields; track f.field) {
        <div class="field">
          <label [for]="'f-' + f.field">{{ f.label }}</label>
          <!-- [selected] per option, not [value] on the select: the options arrive after the filter (from the URL),
               and a select cannot take a value before the matching option exists. -->
          <select [id]="'f-' + f.field" (change)="setList(f.field, $event)" [disabled]="options.isLoading()">
            <option value="" [selected]="selected(f.field) === ''">{{ f.all }}</option>
            @for (value of values(f.field); track value) {
              <option [value]="value" [selected]="value === selected(f.field)">{{ value }}</option>
            }
          </select>
        </div>
      }

      <div class="field actions">
        <button type="button" class="btn" (click)="filters.reset()" [disabled]="filters.isDefault()">Reset filters</button>
      </div>

      <p class="range" aria-live="polite">{{ rangeText() }}</p>
      @if (options.error()) {
        <p class="range warn" role="status">Filter choices could not be loaded; date filters still work.</p>
      }
    </form>
  `,
  styles: `
    .bar {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--space-3) var(--space-4);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 9.5rem;
      flex: 1 1 9.5rem;
    }
    .field.actions {
      flex: 0 0 auto;
      min-width: 0;
    }
    label {
      color: var(--color-muted);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
    }
    select,
    input {
      min-height: 2.5rem;
      padding: 0 var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      background: var(--color-surface);
      color: var(--color-ink);
      font: inherit;
    }
    .range {
      flex-basis: 100%;
      margin: 0;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
    }
    .warn {
      color: var(--color-danger);
    }
    @media (max-width: 480px) {
      .field {
        flex-basis: 100%;
      }
      .field.actions .btn {
        width: 100%;
      }
    }
  `,
})
export class FilterBar {
  protected readonly filters = inject(FilterStore);
  protected readonly presets = DATE_PRESETS;

  protected readonly listFields: readonly { field: ListField; label: string; all: string }[] = [
    { field: 'regions', label: 'Region', all: 'All regions' },
    { field: 'channels', label: 'Channel', all: 'All channels' },
    { field: 'categories', label: 'Category', all: 'All categories' },
  ];

  protected readonly options = httpResource<FilterOptions>(() => '/api/filter-options');

  protected readonly rangeText = computed(() => {
    const { from, to } = this.filters.range();
    const options = this.options.hasValue() ? this.options.value() : undefined;
    if (!from && !to) {
      return options?.firstOrderDate && options.lastOrderDate
        ? `Showing all data: ${formatDate(options.firstOrderDate)} to ${formatDate(options.lastOrderDate)}`
        : 'Showing all data';
    }
    if (from && to) {
      return `Showing ${formatDate(from)} to ${formatDate(to)}`;
    }
    return from ? `Showing from ${formatDate(from)}` : `Showing up to ${formatDate(to as string)}`;
  });

  protected values(field: ListField): readonly string[] {
    const options = this.options.hasValue() ? this.options.value() : undefined;
    const known = options ? { regions: options.regions, channels: options.channels, categories: options.categories }[field] : [];
    // A value from a shared URL stays selectable even if it is not (or no longer) in the data.
    const current = this.filters[field]();
    return [...known, ...current.filter((v) => !known.includes(v))];
  }

  protected selected(field: ListField): string {
    return this.filters[field]()[0] ?? '';
  }

  protected setPreset(event: Event): void {
    const preset = (event.target as HTMLSelectElement).value as DatePreset;
    if (preset === 'custom') {
      // Start the custom range from what was on screen, so switching does not jump.
      const { from, to } = this.filters.range();
      this.filters.update({ preset, from, to });
    } else {
      this.filters.update({ preset, from: null, to: null });
    }
  }

  protected setDate(which: 'from' | 'to', event: Event): void {
    const value = (event.target as HTMLInputElement).value || null;
    this.filters.update({ [which]: value });
  }

  protected setList(field: ListField, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.filters.update({ [field]: value ? [value] : [] });
  }
}