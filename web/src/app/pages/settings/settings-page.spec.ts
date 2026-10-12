import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ImportStore } from '../import/import.store';
import { SettingsPage } from './settings-page';

/**
 * jsdom's <dialog> support is incomplete, so the tests install the small part of it the page relies on:
 * showModal() opens, close(value) sets returnValue, closes and fires "close" (as browsers do, also for Escape).
 */
function installDialog(): () => void {
  const proto = HTMLDialogElement.prototype;
  const original = { showModal: proto.showModal, close: proto.close };
  proto.showModal = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  proto.close = function (this: HTMLDialogElement, value?: string) {
    if (value !== undefined) {
      this.returnValue = value;
    }
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
  return () => Object.assign(proto, original);
}

describe('SettingsPage', () => {
  let fixture: ComponentFixture<SettingsPage>;
  let http: HttpTestingController;
  let restoreDialog: () => void;
  let importResets: number;
  const el = () => fixture.nativeElement as HTMLElement;
  const dialog = () => el().querySelector('dialog') as HTMLDialogElement;
  const opener = () => el().querySelector('section button') as HTMLButtonElement;
  const dialogButton = (label: string) =>
    Array.from(dialog().querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  const status = () => el().querySelector('[role="status"]')?.textContent?.trim() ?? '';
  const alert = () => el().querySelector('[role="alert"]')?.textContent?.trim() ?? null;

  async function settle(): Promise<void> {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  }

  function resetRequest(): TestRequest {
    const request = http.expectOne('/api/demo/reset');
    expect(request.request.method).toBe('POST');
    return request;
  }

  beforeEach(async () => {
    restoreDialog = installDialog();
    importResets = 0;
    TestBed.configureTestingModule({
      imports: [SettingsPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const imports = TestBed.inject(ImportStore);
    const originalReset = imports.reset;
    Object.assign(imports, { reset: () => { importResets++; originalReset(); } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SettingsPage);
    await settle();
  });

  afterEach(() => {
    http.verify();
    restoreDialog();
  });

  it('asks for confirmation first; Cancel closes the dialog without resetting and returns focus', async () => {
    opener().click();
    await settle();
    expect(dialog().hasAttribute('open')).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe('confirm-title');
    expect(dialogButton('Cancel').hasAttribute('autofocus')).toBe(true);

    dialogButton('Cancel').click();
    await settle();

    expect(dialog().hasAttribute('open')).toBe(false);
    http.expectNone('/api/demo/reset');
    expect(document.activeElement).toBe(opener());
  });

  it('Escape (a close without a value) does not reset', async () => {
    opener().click();
    await settle();
    dialog().close(); // what the browser does on Escape
    await settle();

    http.expectNone('/api/demo/reset');
  });

  it('confirming resets, shows progress, then the result, and clears the stale import result', async () => {
    opener().click();
    await settle();
    dialogButton('Reset data').click();
    await settle();

    const request = resetRequest();
    expect(opener().getAttribute('aria-disabled')).toBe('true');
    expect(opener().getAttribute('aria-busy')).toBe('true');
    expect(opener().textContent?.trim()).toBe('Resetting…');
    expect(status()).toBe('Resetting the demo data. This takes a few seconds…');
    expect(document.activeElement).toBe(opener()); // focus is kept while busy

    opener().click(); // ignored while busy
    await settle();
    expect(dialog().hasAttribute('open')).toBe(false);

    request.flush({ orderLines: 200000, completedAt: '2026-10-12T09:05:00Z' });
    await settle();

    expect(status()).toMatch(/^Demo data reset at \d{1,2}:\d{2} (AM|PM): 200,000 order lines loaded\.$/);
    expect(opener().getAttribute('aria-disabled')).toBe('false');
    expect(importResets).toBe(1);
    expect(alert()).toBeNull();
  });

  it('a refused reset explains why and can be tried again', async () => {
    opener().click();
    await settle();
    dialogButton('Reset data').click();
    await settle();
    resetRequest().flush(
      { title: 'Demo data is busy', detail: 'An import or another reset is running. Try again in a moment.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(alert()).toBe('The demo data was not reset. An import or another reset is running. Try again in a moment.');
    expect(status()).toBe('');
    expect(importResets).toBe(0);

    opener().click();
    await settle();
    expect(dialog().hasAttribute('open')).toBe(true);
    dialogButton('Cancel').click();
    await settle();
  });

  it('a lost connection gets a plain message', async () => {
    opener().click();
    await settle();
    dialogButton('Reset data').click();
    await settle();
    resetRequest().error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    await settle();

    expect(alert()).toBe('The demo data was not reset. Could not reach the server. Check your connection and try again.');
  });
});
