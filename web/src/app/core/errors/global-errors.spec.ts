import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from '../../app';
import { NotFoundPage } from '../../pages/not-found/not-found-page';
import { AppErrorHandler, GlobalErrors, messageFor } from './global-errors';

describe('global error handling', () => {
  it('explains a page that failed to load and suggests reloading', () => {
    expect(messageFor(new TypeError('Failed to fetch dynamically imported module: /chunk-ABC.js'))).toContain('could not be loaded');
    expect(messageFor(new Error('Loading chunk 7 failed.'))).toContain('Reload the page');
  });

  it('gives a calm generic message for anything else', () => {
    expect(messageFor(new Error('Cannot read properties of undefined'))).toBe(
      'Something went wrong on this page. Reload to continue. If it keeps happening, sign out and sign in again.',
    );
    expect(messageFor('a thrown string')).toContain('Something went wrong');
  });

  it('the handler logs the error and the app shows a dismissible alert', async () => {
    const log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: ErrorHandler, useClass: AppErrorHandler }],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();

    const boom = new Error('boom');
    TestBed.inject(ErrorHandler).handleError(boom);
    fixture.detectChanges();

    expect(log).toHaveBeenCalledWith(boom);
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain('Something went wrong on this page.');
    const buttons = Array.from(alert.querySelectorAll('button')).map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['Reload page', 'Dismiss']);

    (alert.querySelectorAll('button')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(TestBed.inject(GlobalErrors).message()).toBeNull();
    log.mockRestore();
  });
});

describe('NotFoundPage', () => {
  it('unknown addresses show a not-found page with a way back, instead of silently redirecting', async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([{ path: 'dashboard', children: [] }, { path: '**', component: NotFoundPage }])],
    });
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/no/such/page');
    fixture.detectChanges();

    const page = fixture.nativeElement as HTMLElement;
    expect(page.querySelector('h1')?.textContent).toBe('Page not found');
    expect(page.querySelector('main')).not.toBeNull();
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/dashboard');
    expect(TestBed.inject(Router).url).toBe('/no/such/page');
  });
});
