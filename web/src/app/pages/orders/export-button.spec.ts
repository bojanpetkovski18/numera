import { Component, signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FileSaver } from '../../core/http/file-saver';
import { ExportButton } from './export-button';

@Component({
  imports: [ExportButton],
  template: `<app-export-button [params]="params()" [rowCount]="rows()" />`,
})
class Host {
  readonly params = signal<Record<string, string | string[]>>({ region: ['Europe', 'Asia-Pacific'], status: ['completed'], sort: 'lineTotal', dir: 'desc' });
  readonly rows = signal<number | null>(6558);
}

describe('ExportButton', () => {
  let fixture: ComponentFixture<Host>;
  let http: HttpTestingController;
  let saved: { blob: Blob; name: string }[];
  const el = () => fixture.nativeElement as HTMLElement;
  const button = () => el().querySelector('button') as HTMLButtonElement;
  const status = () => el().querySelector('[role="status"]')?.textContent?.trim() ?? '';
  const alert = () => el().querySelector('[role="alert"]')?.textContent?.trim() ?? null;

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      TestBed.tick();
      await new Promise((resolve) => setTimeout(resolve));
    }
    fixture.detectChanges();
  }

  function exportRequest(): TestRequest {
    return http.expectOne((r) => r.url === '/api/orders/export');
  }

  beforeEach(async () => {
    saved = [];
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FileSaver, useValue: { save: (blob: Blob, name: string) => saved.push({ blob, name }) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    await settle();
  });

  afterEach(() => http.verify());

  it('downloads the current view with the grid parameters, showing progress, then the file name and row count', async () => {
    expect(button().textContent?.trim()).toBe('Export CSV');
    button().click();
    await settle();

    const request = exportRequest();
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.getAll('region')).toEqual(['Europe', 'Asia-Pacific']);
    expect([request.request.params.get('status'), request.request.params.get('sort'), request.request.params.get('dir')]).toEqual(['completed', 'lineTotal', 'desc']);
    expect(request.request.params.has('page')).toBe(false);
    expect(button().disabled).toBe(true);
    expect(button().getAttribute('aria-busy')).toBe('true');
    expect(button().textContent).toContain('Exporting…');
    expect(status()).toBe('Preparing the CSV file…');

    const file = new Blob(['﻿header\r\n'], { type: 'text/csv' });
    request.flush(file, {
      headers: {
        'Content-Disposition': "attachment; filename=numera-orders_20261012-133045Z_europe.csv; filename*=UTF-8''numera-orders_20261012-133045Z_europe.csv",
        'X-Row-Count': '6558',
      },
    });
    await settle();

    expect(saved).toHaveLength(1);
    expect(saved[0].name).toBe('numera-orders_20261012-133045Z_europe.csv');
    // Byte for byte, BOM included (Blob.text() would drop the BOM while decoding).
    expect(Array.from(new Uint8Array(await saved[0].blob.arrayBuffer()))).toEqual(Array.from(new TextEncoder().encode('﻿header\r\n')));
    expect(status()).toBe('6,558 order lines exported to numera-orders_20261012-133045Z_europe.csv.');
    expect(button().disabled).toBe(false);
    expect(alert()).toBeNull();
  });

  it('shows the server problem when the export is refused, and allows another try', async () => {
    button().click();
    await settle();
    const problem = { title: 'Export too large', detail: '120,000 order lines match these filters. An export can hold at most 100,000; narrow the filters and try again.' };
    exportRequest().flush(new Blob([JSON.stringify(problem)], { type: 'application/problem+json' }), { status: 422, statusText: 'Unprocessable Entity' });
    await settle();

    expect(alert()).toBe(`Export failed. ${problem.detail}`);
    expect(saved).toHaveLength(0);
    expect(button().disabled).toBe(false);

    button().click();
    await settle();
    exportRequest().flush(new Blob(['x']), { headers: { 'X-Row-Count': '1' } });
    await settle();
    expect(alert()).toBeNull();
    expect(saved[0].name).toBe('numera-orders.csv'); // no Content-Disposition: a safe default name
  });

  it('a network failure gets a plain message', async () => {
    button().click();
    await settle();
    exportRequest().error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await settle();

    expect(alert()).toBe('Export failed. Could not reach the server. Check your connection and try again.');
  });

  it('explains the row limit before the user tries, and only above it', async () => {
    fixture.componentInstance.rows.set(100_001);
    await settle();
    expect(button().disabled).toBe(true);
    const note = el().querySelector(`#${button().getAttribute('aria-describedby')}`);
    expect(note?.textContent).toContain('at most 100,000 order lines');

    fixture.componentInstance.rows.set(100_000);
    await settle();
    expect(button().disabled).toBe(false);
    expect(button().hasAttribute('aria-describedby')).toBe(false);
  });

  it('hides the result message once the filters change, so it never describes another view', async () => {
    button().click();
    await settle();
    exportRequest().flush(new Blob(['x']), { headers: { 'X-Row-Count': '3' } });
    await settle();
    expect(status()).toContain('3 order lines exported');

    fixture.componentInstance.params.set({ region: ['Europe'] });
    await settle();
    expect(status()).toBe('');
  });
});
