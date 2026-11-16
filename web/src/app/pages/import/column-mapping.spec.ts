import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ColumnMapping } from './column-mapping';
import { ImportPreview, MappingChoice } from './import.models';
import { COLUMNS, MAPPED_PREVIEW } from './testing';

describe('ColumnMapping', () => {
  let fixture: ComponentFixture<ColumnMapping>;
  let root: HTMLElement;
  const confirmed: MappingChoice[] = [];
  let cancelled = 0;

  async function render(preview: ImportPreview = MAPPED_PREVIEW): Promise<void> {
    confirmed.length = 0;
    cancelled = 0;
    TestBed.configureTestingModule({ imports: [ColumnMapping] });
    fixture = TestBed.createComponent(ColumnMapping);
    fixture.componentRef.setInput('preview', preview);
    fixture.componentRef.setInput('fileName', 'shop-export.csv');
    fixture.componentInstance.confirm.subscribe((c) => confirmed.push(c));
    fixture.componentInstance.cancel.subscribe(() => cancelled++);
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const select = (id: string) => root.querySelector<HTMLSelectElement>(`#${id}`)!;

  async function choose(id: string, value: string): Promise<void> {
    select(id).value = value;
    select(id).dispatchEvent(new Event('change'));
    await settle();
  }

  async function submit(): Promise<void> {
    root.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle();
  }

  it('starts from the suggestion, with an example value under each choice and the columns tagged in the sample', async () => {
    await render();

    expect(select('map-order_date').value).toBe('Date');
    expect(select('map-quantity').value).toBe('Qty');
    expect(select('map-date-format').value).toBe('dd/MM/yyyy');
    expect(select('map-discount').value).toBe('Discount %');
    expect(select('map-discount-scale').value).toBe('percent');
    expect(root.querySelector('#map-order_date-hint')?.textContent).toBe('For example: 01/03/2025');
    const tags = Array.from(root.querySelectorAll('thead .tag')).map((t) => t.textContent);
    expect(tags.length).toBe(11); // every column but Notes
    expect(root.querySelector('thead th:last-child')?.textContent).toBe('Notes');
  });

  it('warns about ambiguous dates until the date format is chosen', async () => {
    await render();
    expect(root.querySelector('#map-date-warning')?.textContent).toContain('01/03/2025 could be 1 March or 3 January');
    expect(select('map-date-format').getAttribute('aria-describedby')).toBe('map-date-warning');

    await choose('map-date-format', 'MM/dd/yyyy');

    expect(root.querySelector('#map-date-warning')).toBeNull();
  });

  it('imports with the chosen mapping', async () => {
    await render();
    await choose('map-date-format', 'MM/dd/yyyy');

    await submit();

    expect(confirmed).toEqual([{ mapping: { columns: COLUMNS, dateFormat: 'MM/dd/yyyy', discount: 'percent' }, saveAs: null }]);
  });

  it('marks unmapped fields and a column chosen twice only after an import attempt, and moves focus to the first', async () => {
    await render({ ...MAPPED_PREVIEW, suggestion: { ...MAPPED_PREVIEW.suggestion, columns: { ...COLUMNS, status: null } } });
    expect(root.querySelector('[aria-invalid="true"]')).toBeNull();

    await choose('map-channel', 'Qty');
    await submit();
    await new Promise((resolve) => setTimeout(resolve));

    expect(confirmed).toEqual([]);
    expect(root.querySelector('[role="alert"]')?.textContent?.trim()).toBe('2 choices need your attention before importing.');
    expect(root.querySelector('#map-status-hint')?.textContent).toBe('Choose a column.');
    expect(root.querySelector('#map-quantity-hint')?.textContent).toBe('Also chosen for Channel.');
    expect(Array.from(root.querySelectorAll('[aria-invalid="true"]')).map((e) => e.id)).toEqual(['map-quantity', 'map-status']);
  });

  it('saves under a name when asked, and needs a name', async () => {
    await render();
    root.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    await settle();
    const name = root.querySelector<HTMLInputElement>('#map-name')!;
    expect(name.value).toBe('shop-export');

    name.value = '  ';
    name.dispatchEvent(new Event('input'));
    await submit();
    expect(confirmed).toEqual([]);
    expect(root.querySelector('#map-name-hint')?.textContent).toBe('Give the mapping a name.');

    name.value = 'Webshop export';
    name.dispatchEvent(new Event('input'));
    await submit();
    expect(confirmed[0].saveAs).toBe('Webshop export');
  });

  it('names the saved mapping it was filled from, shows a save error and can be cancelled', async () => {
    await render({
      ...MAPPED_PREVIEW,
      dateFormatAmbiguous: false,
      savedMapping: { id: 'm1', name: 'Webshop', mapping: MAPPED_PREVIEW.suggestion, sourceHeaders: [], updatedAt: '2026-11-16T10:00:00Z' },
    });
    expect(root.querySelector('.note')?.textContent).toContain('Filled in from your saved mapping “Webshop”.');
    expect(root.querySelector('#map-date-warning')).toBeNull();

    fixture.componentRef.setInput('error', 'You already have a mapping with this name.');
    fixture.componentRef.setInput('busy', true);
    await settle();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('You already have a mapping with this name.');
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);

    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Cancel')!.click();
    expect(cancelled).toBe(1);
  });
});
