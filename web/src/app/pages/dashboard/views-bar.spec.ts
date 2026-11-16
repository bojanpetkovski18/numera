import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterStore } from '../../core/filters/filter.store';
import { SavedView, fromViewSettings, toViewSettings } from '../../core/views/views-api';
import { dashboardTestProviders, settle } from './testing';
import { ViewsBar } from './views-bar';

const EUROPE: SavedView = {
  id: 'v1',
  name: 'Europe Q3',
  settings: {
    preset: 'custom',
    from: '2026-07-01',
    to: '2026-09-30',
    regions: ['Europe'],
    channels: [],
    categories: [],
    granularity: 'week',
    compare: 'previous',
  },
  updatedAt: '2026-11-16T10:00:00Z',
};

const RECENT: SavedView = {
  ...EUROPE,
  id: 'v2',
  name: 'Last 90 days',
  settings: { ...EUROPE.settings, preset: 'last90', from: null, to: null, regions: [], granularity: 'month', compare: 'none' },
};

describe('ViewsBar', () => {
  let fixture: ComponentFixture<ViewsBar>;
  let http: HttpTestingController;
  let store: InstanceType<typeof FilterStore>;
  const el = () => fixture.nativeElement as HTMLElement;
  const button = (label: string, root: ParentNode = el()) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.replace(/\s+/g, ' ').trim().startsWith(label))!;

  async function render(views: SavedView[] = [EUROPE, RECENT]): Promise<void> {
    TestBed.configureTestingModule({ imports: [ViewsBar], providers: dashboardTestProviders() });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(FilterStore);
    fixture = TestBed.createComponent(ViewsBar);
    await settle(fixture);
    http.expectOne('/api/views').flush(views);
    await settle(fixture);
  }

  afterEach(() => http.verify());

  it('lists the views with a short description', async () => {
    await render();
    button('Saved views').click();
    await settle(fixture);

    expect(button('Saved views').textContent?.trim()).toBe('Saved views (2)');
    expect(button('Saved views').getAttribute('aria-expanded')).toBe('true');
    const rows = Array.from(el().querySelectorAll('li > .row')).map((r) => [r.querySelector('.name')?.textContent, r.querySelector('.muted')?.textContent]);
    expect(rows).toEqual([
      ['Europe Q3', 'Jul 1, 2026 to Sep 30, 2026 · Europe · vs previous period'],
      ['Last 90 days', 'Last 90 days'],
    ]);
  });

  it('opens a view by applying its settings, and says which view is on screen', async () => {
    await render();
    expect(el().querySelector('.current')).toBeNull();
    button('Saved views').click();
    await settle(fixture);

    button('Open', el().querySelector('li')!).click();
    await settle(fixture);

    expect(store.filter()).toEqual(fromViewSettings(EUROPE.settings));
    expect(el().querySelector('.current')?.textContent).toBe('Saved view: Europe Q3');
    expect(el().querySelector('#views-list')).toBeNull();
  });

  it('saves what is on screen under a name, and shows a server refusal', async () => {
    await render([]);
    store.update({ preset: 'ytd', categories: ['Blends'], compare: 'year', granularity: 'day' });
    button('Save view').click();
    await settle(fixture);
    const input = el().querySelector<HTMLInputElement>('#view-name')!;

    input.value = 'Blends this year';
    input.dispatchEvent(new Event('input'));
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    const first = http.expectOne((r) => r.url === '/api/views' && r.method === 'POST');
    expect(first.request.body).toEqual({ name: 'Blends this year', settings: toViewSettings(store.filter()) });
    expect(first.request.body.settings).toEqual({
      preset: 'ytd', from: null, to: null, regions: [], channels: [], categories: ['Blends'], granularity: 'day', compare: 'year',
    });
    first.flush({ detail: 'You already have a view with this name. Choose another name.' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('You already have a view with this name.');

    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    http.expectOne((r) => r.url === '/api/views' && r.method === 'POST').flush({ ...RECENT, id: 'v3', name: 'Blends this year', settings: toViewSettings(store.filter()) });
    await settle(fixture);

    expect(el().querySelector('#views-save')).toBeNull();
    expect(el().querySelector('.current')?.textContent).toBe('Saved view: Blends this year');
  });

  it('needs a name', async () => {
    await render([]);
    button('Save view').click();
    await settle(fixture);
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    expect(el().querySelector('[role="alert"]')?.textContent).toBe('Give the view a name.');
    http.expectNone('/api/views');
  });

  it('creates a share link for the chosen number of days and shows it once, ready to copy', async () => {
    await render();
    button('Saved views').click();
    await settle(fixture);
    const row = el().querySelector('li')!;

    button('Share', row).click();
    await settle(fixture);
    const days = row.querySelector<HTMLSelectElement>('#share-days-v1')!;
    expect(Array.from(days.options).map((o) => o.textContent)).toEqual(['1 day', '7 days', '30 days']);
    days.value = '30';
    days.dispatchEvent(new Event('change'));
    button('Create link', row).click();

    const request = http.expectOne((r) => r.url === '/api/views/v1/shares' && r.method === 'POST');
    expect(request.request.body).toEqual({ expiresInDays: 30 });
    request.flush({
      link: { id: 'l1', viewId: 'v1', viewName: 'Europe Q3', createdAt: '2026-11-16T10:00:00Z', expiresAt: '2026-12-16T10:00:00Z' },
      token: 'T'.repeat(43),
      url: `/shared/${'T'.repeat(43)}`,
    });
    await settle(fixture);

    expect(row.querySelector<HTMLInputElement>('#share-url-v1')!.value).toBe(`${location.origin}/shared/${'T'.repeat(43)}`);
    expect(row.querySelector('label')?.textContent).toBe('Read-only link, works until Dec 16, 2026');
    expect(row.textContent).toContain('no order rows');
  });

  it('renames and deletes a view', async () => {
    await render();
    button('Saved views').click();
    await settle(fixture);

    button('Rename', el().querySelector('li')!).click();
    await settle(fixture);
    const input = el().querySelector<HTMLInputElement>('#rename-v1')!;
    input.value = 'Europe, third quarter';
    input.dispatchEvent(new Event('input'));
    el().querySelector('li form')!.dispatchEvent(new Event('submit'));
    const rename = http.expectOne((r) => r.url === '/api/views/v1' && r.method === 'PUT');
    expect(rename.request.body).toEqual({ name: 'Europe, third quarter', settings: EUROPE.settings });
    rename.flush({ ...EUROPE, name: 'Europe, third quarter' });
    await settle(fixture);
    expect(Array.from(el().querySelectorAll('li .name')).map((n) => n.textContent)).toEqual(['Europe, third quarter', 'Last 90 days']);

    button('Delete', el().querySelectorAll('li')[1]).click();
    http.expectOne((r) => r.url === '/api/views/v2' && r.method === 'DELETE').flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    expect(el().querySelectorAll('li').length).toBe(1);
  });
});
