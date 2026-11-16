import { Component, ElementRef, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { DATE_FORMATS, DateFormat, DiscountScale, IMPORT_FIELDS, ImportMapping, ImportPreview, MappingChoice } from './import.models';

const DATE_EXAMPLES: Readonly<Record<DateFormat, string>> = {
  'yyyy-MM-dd': '2025-12-31',
  'dd/MM/yyyy': '31/12/2025',
  'MM/dd/yyyy': '12/31/2025',
  'dd.MM.yyyy': '31.12.2025',
};

const MAX_NAME_LENGTH = 80;

/**
 * The mapping step for a file whose column names are not Numera's: one choice per field, the date and discount
 * formats, an optional name to save the mapping under, and the first rows of the file to check against.
 * The server validates the mapping and every row again; this only guides the choice.
 */
@Component({
  selector: 'app-column-mapping',
  template: `
    <section class="mapping" aria-labelledby="mapping-title">
      <h2 id="mapping-title" tabindex="-1">Match your columns</h2>
      <p>
        <strong>{{ fileName() }}</strong> uses its own column names. Choose the column that holds each value. Every row is
        still checked when it is imported.
      </p>
      @if (preview().savedMapping; as saved) {
        <p class="note">Filled in from your saved mapping “{{ saved.name }}”.</p>
      }

      <form novalidate (submit)="submit($event)">
        <fieldset>
          <legend>Columns</legend>
          <div class="grid">
            @for (f of fields; track f.name) {
              <div class="field" [class.invalid]="shownProblem(f.name)">
                <label [for]="'map-' + f.name">{{ f.label }}</label>
                <select
                  [id]="'map-' + f.name"
                  [attr.aria-invalid]="shownProblem(f.name) ? true : null"
                  [attr.aria-describedby]="'map-' + f.name + '-hint'"
                  (change)="setColumn(f.name, $event)"
                >
                  <option value="" [selected]="!columns()[f.name]">Choose a column</option>
                  @for (h of preview().headers; track $index) {
                    <option [value]="h" [selected]="h === columns()[f.name]">{{ h }}</option>
                  }
                </select>
                <span class="hint" [id]="'map-' + f.name + '-hint'">{{ shownProblem(f.name) ?? example(f.name) }}</span>
              </div>
            }
          </div>
        </fieldset>

        <fieldset>
          <legend>Values</legend>
          <div class="grid">
            <div class="field">
              <label for="map-date-format">Dates are written as</label>
              <select id="map-date-format" (change)="setDateFormat($event)" [attr.aria-describedby]="dateWarning() ? 'map-date-warning' : null">
                @for (d of dateFormats; track d) {
                  <option [value]="d" [selected]="d === dateFormat()">{{ d }} ({{ dateExamples[d] }})</option>
                }
              </select>
            </div>
            <div class="field">
              <label for="map-discount-scale">Discounts are written as</label>
              <select id="map-discount-scale" (change)="setDiscount($event)">
                <option value="fraction" [selected]="discount() === 'fraction'">A fraction (0.10 = 10%)</option>
                <option value="percent" [selected]="discount() === 'percent'">A percentage (10 = 10%)</option>
              </select>
            </div>
          </div>
          @if (dateWarning()) {
            <p class="warning" id="map-date-warning">
              The dates in this file fit more than one format: 01/03/2025 could be 1 March or 3 January. Check the date format.
            </p>
          }
        </fieldset>

        <div class="save">
          <label class="check">
            <input type="checkbox" [checked]="save()" (change)="setSave($event)" />
            Save this mapping for files like this one
          </label>
          @if (save()) {
            <div class="field" [class.invalid]="attempted() && nameProblem()">
              <label for="map-name">Mapping name</label>
              <input id="map-name" type="text" [maxLength]="maxName" [value]="name()" (input)="setName($event)"
                [attr.aria-invalid]="attempted() && nameProblem() ? true : null" aria-describedby="map-name-hint" />
              <span class="hint" id="map-name-hint">{{ (attempted() && nameProblem()) || 'Used again when a file has the same columns.' }}</span>
            </div>
          }
        </div>

        @if (attempted() && problemCount() > 0) {
          <p class="error" role="alert">
            {{ problemCount() === 1 ? 'One choice needs' : problemCount() + ' choices need' }} your attention before importing.
          </p>
        }
        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }

        <div class="actions">
          <button type="submit" class="btn primary" [disabled]="busy()">{{ busy() ? 'Saving…' : 'Import with this mapping' }}</button>
          <button type="button" class="btn" (click)="cancel.emit()">Cancel</button>
        </div>
      </form>

      <details class="sample" open>
        <summary>First {{ preview().rows.length }} rows of the file</summary>
        <div class="scroll" tabindex="0" role="region" aria-label="First rows of the file">
          <table class="data-table">
            <thead>
              <tr>
                @for (h of preview().headers; track $index) {
                  <th scope="col">{{ h }}@if (usedFor(h); as label) {<span class="tag">{{ label }}</span>}</th>
                }
              </tr>
            </thead>
            <tbody>
              @for (row of preview().rows; track $index) {
                <tr>
                  @for (cell of row; track $index) {
                    <td>{{ cell }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </div>
      </details>
    </section>
  `,
  styles: `
    .mapping {
      margin-top: var(--space-5);
      padding: var(--space-5);
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }
    h2 { margin: 0 0 var(--space-2); font-size: var(--font-size-lg); }
    fieldset { margin: var(--space-4) 0 0; padding: 0; border: 0; }
    legend { margin-bottom: var(--space-2); font-weight: var(--font-weight-bold); }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(13rem, 1fr));
      gap: var(--space-3) var(--space-4);
    }
    .field { display: flex; flex-direction: column; gap: var(--space-1); min-width: 0; }
    label { font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); }
    select, input[type='text'] {
      min-height: 2.5rem;
      padding: 0 var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      background: var(--color-surface);
      color: var(--color-ink);
      font: inherit;
    }
    .invalid select, .invalid input { border: 2px solid var(--color-danger); }
    .hint { color: var(--color-muted); font-size: var(--font-size-sm); overflow-wrap: anywhere; }
    .invalid .hint, .error { color: var(--color-danger); }
    .note, .warning { margin: var(--space-2) 0 0; font-size: var(--font-size-sm); }
    .warning { padding: var(--space-2) var(--space-3); border-left: 4px solid var(--color-accent); background: color-mix(in srgb, var(--color-accent) 10%, transparent); }
    .save { margin-top: var(--space-4); display: grid; gap: var(--space-2); max-width: 26rem; }
    .check { display: flex; align-items: center; gap: var(--space-2); font-weight: normal; font-size: var(--font-size-md); }
    .check input { width: 1.15rem; height: 1.15rem; }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-3); margin-top: var(--space-4); }
    .sample { margin-top: var(--space-5); font-size: var(--font-size-sm); }
    .sample summary { cursor: pointer; color: var(--color-muted); padding: var(--space-1) 0; }
    .scroll { max-height: 20rem; overflow: auto; }
    .data-table th, .data-table td { text-align: left; white-space: nowrap; }
    .tag { display: block; color: var(--color-primary); font-weight: normal; }
  `,
})
export class ColumnMapping {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly preview = input.required<ImportPreview>();
  readonly fileName = input.required<string>();
  /** True while the mapping is being saved. */
  readonly busy = input(false);
  /** A server error from saving the mapping. */
  readonly error = input<string | null>(null);

  readonly confirm = output<MappingChoice>();
  readonly cancel = output<void>();

  protected readonly fields = IMPORT_FIELDS;
  protected readonly dateFormats = DATE_FORMATS;
  protected readonly dateExamples = DATE_EXAMPLES;
  protected readonly maxName = MAX_NAME_LENGTH;

  // Start from the suggestion (or the saved mapping). The page creates this step anew for every file.
  protected readonly columns = linkedSignal<Record<string, string | null>>(() => ({ ...this.preview().suggestion.columns }));
  protected readonly dateFormat = linkedSignal<DateFormat>(() => this.preview().suggestion.dateFormat);
  protected readonly discount = linkedSignal<DiscountScale>(() => this.preview().suggestion.discount);
  protected readonly save = signal(false);
  protected readonly name = linkedSignal(() => this.preview().savedMapping?.name ?? this.fileName().replace(/\.csv$/i, ''));
  protected readonly attempted = signal(false);
  /** The date format was changed or confirmed by the user, so the ambiguity warning can go. */
  private readonly dateTouched = signal(false);

  protected readonly dateWarning = computed(() => this.preview().dateFormatAmbiguous && !this.dateTouched());

  /** A problem per field: unmapped, or a column already used by an earlier field. */
  private readonly problems = computed(() => {
    const columns = this.columns();
    const firstUse = new Map<string, string>();
    const result: Record<string, string> = {};
    for (const f of this.fields) {
      const column = columns[f.name];
      if (!column) {
        result[f.name] = 'Choose a column.';
        continue;
      }
      const key = column.toLowerCase();
      const earlier = firstUse.get(key);
      if (earlier !== undefined) {
        result[f.name] = `Also chosen for ${earlier}.`;
      } else {
        firstUse.set(key, f.label);
      }
    }
    return result;
  });

  protected readonly nameProblem = computed(() => {
    const name = this.name().trim();
    return !this.save() || (name.length > 0 && name.length <= MAX_NAME_LENGTH) ? null : 'Give the mapping a name.';
  });

  protected readonly problemCount = computed(() => Object.keys(this.problems()).length + (this.nameProblem() ? 1 : 0));

  /** Problems show once the user tried to import, so a fresh form is not covered in red. */
  protected shownProblem(field: string): string | null {
    return this.attempted() ? (this.problems()[field] ?? null) : null;
  }

  protected example(field: string): string {
    const column = this.columns()[field];
    const index = column ? this.preview().headers.indexOf(column) : -1;
    const value = index < 0 ? undefined : this.preview().rows.map((r) => r[index]).find((v) => v);
    return value ? `For example: ${value}` : column ? 'Empty in the first rows' : '';
  }

  protected usedFor(header: string): string | null {
    const labels = this.fields.filter((f) => this.columns()[f.name] === header).map((f) => f.label);
    return labels.length ? labels.join(', ') : null;
  }

  protected setColumn(field: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.columns.update((c) => ({ ...c, [field]: value || null }));
  }

  protected setDateFormat(event: Event): void {
    this.dateFormat.set((event.target as HTMLSelectElement).value as DateFormat);
    this.dateTouched.set(true);
  }

  protected setSave(event: Event): void {
    this.save.set((event.target as HTMLInputElement).checked);
  }

  protected setName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected setDiscount(event: Event): void {
    this.discount.set((event.target as HTMLSelectElement).value as DiscountScale);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.attempted.set(true);
    if (this.problemCount() > 0) {
      // Move to the first choice that needs attention.
      queueMicrotask(() => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
      return;
    }
    const mapping: ImportMapping = { columns: this.columns(), dateFormat: this.dateFormat(), discount: this.discount() };
    this.confirm.emit({ mapping, saveAs: this.save() ? this.name().trim() : null });
  }
}
