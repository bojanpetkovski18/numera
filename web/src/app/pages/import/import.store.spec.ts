import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ImportJob } from './import.models';
import { ImportStore } from './import.store';

const completed: ImportJob = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  fileName: 'orders.csv',
  status: 'completed',
  totalRows: 3,
  importedRows: 3,
  rejectedRows: 0,
  warnings: [],
  failureReason: null,
  createdAt: '2026-10-11T12:00:00Z',
  completedAt: '2026-10-11T12:00:01Z',
};

describe('ImportStore', () => {
  let store: InstanceType<typeof ImportStore>;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    store = TestBed.inject(ImportStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts idle with no completed imports', () => {
    expect(store.phase()).toBe('idle');
    expect(store.completedImportCount()).toBe(0);
  });

  it('increments the completed import counter and puts the job first in the history', () => {
    store.upload(new File(['x'], 'orders.csv', { type: 'text/csv' }));
    expect(store.phase()).toBe('uploading');

    http.expectOne('/api/imports').flush(completed, { status: 201, statusText: 'Created' });

    expect(store.phase()).toBe('done');
    expect(store.result()).toEqual(completed);
    expect(store.completedImportCount()).toBe(1);
    expect(store.history()[0].id).toBe(completed.id);
  });

  it('does not count a failed import', () => {
    store.upload(new File(['x'], 'orders.csv', { type: 'text/csv' }));
    http.expectOne('/api/imports').flush({ detail: 'nope' }, { status: 422, statusText: 'Unprocessable Entity' });
    http.expectOne((r) => r.method === 'GET').flush([]);

    expect(store.phase()).toBe('failed');
    expect(store.errorMessage()).toBe('nope');
    expect(store.completedImportCount()).toBe(0);
  });

  it('reset clears the result', () => {
    store.upload(new File(['x'], 'orders.csv', { type: 'text/csv' }));
    http.expectOne('/api/imports').flush(completed, { status: 201, statusText: 'Created' });

    store.reset();

    expect(store.phase()).toBe('idle');
    expect(store.result()).toBeNull();
  });
});