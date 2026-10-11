import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FileSaver } from '../../core/http/file-saver';
import { ImportJob } from './import.models';
import { ImportPage } from './import-page';

function job(overrides: Partial<ImportJob> = {}): ImportJob {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    fileName: 'orders.csv',
    status: 'completed',
    totalRows: 10,
    importedRows: 10,
    rejectedRows: 0,
    warnings: [],
    failureReason: null,
    createdAt: '2026-10-11T12:00:00Z',
    completedAt: '2026-10-11T12:00:02Z',
    ...overrides,
  };
}

function csvFile(name = 'orders.csv', size = 100): File {
  const file = new File(['order_id\n'], name, { type: 'text/csv' });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('ImportPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<ImportPage>;
  let root: HTMLElement;
  const saved: { blob: Blob; name: string }[] = [];

  async function setup(history: ImportJob[] | 'error' = []): Promise<void> {
    saved.length = 0;
    TestBed.configureTestingModule({
      imports: [ImportPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FileSaver, useValue: { save: (blob: Blob, name: string) => saved.push({ blob, name }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ImportPage);
    root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    const request = http.expectOne((r) => r.url === '/api/imports' && r.method === 'GET');
    if (history === 'error') {
      request.flush({}, { status: 500, statusText: 'Server Error' });
    } else {
      request.flush(history);
    }
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function chooseFile(file: File): Promise<void> {
    const input = root.querySelector<HTMLInputElement>('#file-input')!;
    Object.defineProperty(input, 'files', { value: { item: () => file, length: 1 }, configurable: true });
    input.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function respond(body: ImportJob): Promise<void> {
    http.expectOne((r) => r.url === '/api/imports' && r.method === 'POST').flush(body, { status: 201, statusText: 'Created' });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = () => root.textContent ?? '';
  const button = (label: string) =>
    Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes(label));

  afterEach(() => http.verify());

  it('shows a loading state, then an empty history message', async () => {
    TestBed.configureTestingModule({
      imports: [ImportPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ImportPage);
    root = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    expect(text()).toContain('Loading import history');

    http.expectOne('/api/imports?limit=20').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text()).toContain('No imports yet');
  });

  it('shows an error with a retry button when the history cannot be loaded', async () => {
    await setup('error');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    button('Try again')!.click();
    http.expectOne((r) => r.url === '/api/imports' && r.method === 'GET').flush([job()]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text()).toContain('orders.csv');
  });

  it('lists recent imports with their counts', async () => {
    await setup([job({ fileName: 'march.csv', importedRows: 8, rejectedRows: 2 })]);

    expect(text()).toContain('march.csv');
    expect(text()).toContain('8 imported');
    expect(text()).toContain('2 rejected');
    expect(button('Download error report')).toBeTruthy();
  });

  it('shows success after a clean upload, with no error report button', async () => {
    await setup();

    await chooseFile(csvFile());
    const request = http.expectOne((r) => r.url === '/api/imports' && r.method === 'POST');
    expect(request.request.body).toBeInstanceOf(FormData);
    request.flush(job(), { status: 201, statusText: 'Created' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text()).toContain('Import complete');
    expect(root.querySelector('.counts')?.textContent).toContain('10');
    expect(button('Download error report')).toBeUndefined();
  });

  it('shows partial success with counts, warnings and a working error report download', async () => {
    await setup();
    await chooseFile(csvFile());
    await respond(
      job({
        id: '22222222-2222-2222-2222-222222222222',
        totalRows: 10,
        importedRows: 4,
        rejectedRows: 6,
        warnings: ["Unknown column 'notes' was ignored."],
      }),
    );

    expect(text()).toContain('Imported with rejected rows');
    expect(text()).toContain("Unknown column 'notes' was ignored.");
    const counts = Array.from(root.querySelectorAll('.counts dd')).map((d) => d.textContent?.trim());
    expect(counts).toEqual(['10', '4', '6', '1']);

    root.querySelector<HTMLButtonElement>('section[aria-label="Import result"] button')!.click();
    const download = http.expectOne('/api/imports/22222222-2222-2222-2222-222222222222/errors.csv');
    expect(download.request.responseType).toBe('blob');
    download.flush(new Blob(['row_number,field,message,raw_row\r\n']));

    expect(saved).toHaveLength(1);
    expect(saved[0].name).toBe('import-22222222-2222-2222-2222-222222222222-errors.csv');
  });

  it('shows the API message when the error report download fails', async () => {
    await setup([job({ id: '33333333-3333-3333-3333-333333333333', importedRows: 1, rejectedRows: 1 })]);

    button('Download error report')!.click();
    http
      .expectOne('/api/imports/33333333-3333-3333-3333-333333333333/errors.csv')
      .flush(new Blob([JSON.stringify({ title: 'Import not found', detail: 'No import exists with that id.' })]), {
        status: 404,
        statusText: 'Not Found',
      });
    await new Promise((resolve) => setTimeout(resolve));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('p[role="alert"]')?.textContent).toContain('No import exists with that id.');
    expect(saved).toHaveLength(0);
  });

  it('shows the API problem detail when the import is rejected', async () => {
    await setup();
    await chooseFile(csvFile());

    http
      .expectOne((r) => r.url === '/api/imports' && r.method === 'POST')
      .flush(
        { title: 'Import rejected', detail: 'The file is missing required columns: unit_price.', importId: 'x' },
        { status: 422, statusText: 'Unprocessable Entity' },
      );
    // The store refreshes the history after a failed import.
    http.expectOne((r) => r.url === '/api/imports' && r.method === 'GET').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = root.querySelector('section[role="alert"]');
    expect(alert?.textContent).toContain('Import failed');
    expect(alert?.textContent).toContain('missing required columns: unit_price');
  });

  it('blocks an oversize file on the client without calling the API', async () => {
    await setup();

    await chooseFile(csvFile('huge.csv', 10 * 1024 * 1024 + 1));

    expect(text()).toContain('larger than 10 MB');
    http.expectNone((r) => r.method === 'POST');
  });

  it('blocks a file that is not a CSV on the client', async () => {
    await setup();

    await chooseFile(csvFile('orders.xlsx'));

    expect(text()).toContain('Choose a .csv file.');
    http.expectNone((r) => r.method === 'POST');
  });

  it('accepts a file dropped on the drop zone', async () => {
    await setup();
    const zone = root.querySelector('.dropzone')!;
    const drop = new Event('drop', { cancelable: true });
    Object.assign(drop, { dataTransfer: { files: { item: () => csvFile('dropped.csv'), length: 1 } } });

    zone.dispatchEvent(drop);
    await fixture.whenStable();

    const request = http.expectOne((r) => r.url === '/api/imports' && r.method === 'POST');
    expect((request.request.body as FormData).get('file')).toBeInstanceOf(File);
    request.flush(job({ fileName: 'dropped.csv' }), { status: 201, statusText: 'Created' });
  });

  it('shows the all-rejected state when nothing was imported', async () => {
    await setup();
    await chooseFile(csvFile());

    await respond(job({ totalRows: 5, importedRows: 0, rejectedRows: 5 }));

    expect(text()).toContain('No rows were imported');
    expect(button('Download error report')).toBeTruthy();
  });

  it('links to the sample CSV template', async () => {
    await setup();

    const link = root.querySelector<HTMLAnchorElement>('a[download]');
    expect(link?.getAttribute('href')).toBe('sample-orders.csv');
  });
});