import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { FilterStore } from './filter.store';
import { syncFiltersWithUrl } from './sync-filters-with-url';

const pageState = signal<Record<string, string | null>>({ sort: null });

@Component({ template: '' })
class SyncedPage {
  constructor() {
    syncFiltersWithUrl(false, () => pageState());
  }
}

@Component({ imports: [RouterOutlet], template: '<router-outlet />' })
class Host {}

describe('syncFiltersWithUrl', () => {
  let fixture: ComponentFixture<Host>;

  async function settle(): Promise<void> {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }

  beforeEach(async () => {
    pageState.set({ sort: null });
    TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([{ path: 'p', component: SyncedPage }])] });
    fixture = TestBed.createComponent(Host);
  });

  it('writes filter and page params in one navigation, so neither undoes the other', async () => {
    await TestBed.inject(Router).navigateByUrl('/p?region=Europe&sort=region');
    await settle();

    // Change both in the same tick: clear the region filter and change the page's sort.
    TestBed.inject(FilterStore).update({ regions: [] });
    pageState.set({ sort: 'status' });
    await settle();

    expect(TestBed.inject(Router).url).toBe('/p?sort=status');
    expect(TestBed.inject(FilterStore).regions()).toEqual([]);
  });

  it('keeps filters from another page when the URL has none, and lets URL filters win otherwise', async () => {
    TestBed.inject(FilterStore).update({ regions: ['Asia-Pacific'] });

    await TestBed.inject(Router).navigateByUrl('/p');
    await settle();
    expect(TestBed.inject(Router).url).toBe('/p?region=Asia-Pacific');

    await TestBed.inject(Router).navigateByUrl('/p?channel=Mobile%20App');
    await settle();
    expect(TestBed.inject(FilterStore).regions()).toEqual([]);
    expect(TestBed.inject(FilterStore).channels()).toEqual(['Mobile App']);
    fixture.destroy();
  });
});