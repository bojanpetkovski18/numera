import { Component } from '@angular/core';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { FilterStore } from '../../core/filters/filter.store';
import { dashboardTestProviders, settle } from '../dashboard/testing';
import { OrdersPage, SEARCH_DEBOUNCE_MS } from './orders-page';
import { OrderRow, OrdersPage as PageData } from './orders.models';

@Component({ imports: [RouterOutlet], template: '<router-outlet />' })
class Host {}

const OPTIONS = { regions: ['Europe', 'Latin America'], channels: ['Mobile App'], categories: ['Blends'], firstOrderDate: '2024-10-01', lastOrderDate: '2026-09-30' };

function row(i: number, overrides: Partial<OrderRow> = {}): OrderRow {
  return {
    id: i,
    orderId: `HP-${String(i).padStart(6, '0')}`,
    orderDate: '2026-09-30',
    customerId: 'C-00001',
    region: 'Europe',
    channel: 'Online Store',
    productSku: 'HP-SO-ETH',
    productCategory: 'Single Origin',
    quantity: 2,
    unitPrice: 18.5,
    discount: 0.05,
    lineTotal: 35.15,
    status: 'completed',
    ...overrides,
  };
}

function page(items: OrderRow[], totalCount: number, pageNumber = 1, pageSize = 25): PageData {
  return { items, totalCount, page: pageNumber, pageSize, totalPages: Math.ceil(totalCount / pageSize) };
}

describe('OrdersPage', () => {
  let fixture: ComponentFixture<Host>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;

  async function open(url: string): Promise<void> {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [...dashboardTestProviders(), provideRouter([{ path: 'orders', component: OrdersPage }, { path: 'dashboard', children: [] }])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    await TestBed.inject(Router).navigateByUrl(url);
    await settle(fixture);
    for (const r of http.match('/api/filter-options')) r.flush(OPTIONS);
    await settle(fixture);
  }

  function ordersRequest(): TestRequest {
    const requests = http.match((r) => r.url === '/api/orders');
    expect(requests.length).toBe(1);
    return requests[0];
  }

  async function respond(data: PageData): Promise<void> {
    ordersRequest().flush(data);
    await settle(fixture);
  }

  function header(label: string): HTMLTableCellElement {
    return Array.from(root().querySelectorAll<HTMLTableCellElement>('th')).find((th) => th.textContent?.includes(label))!;
  }

  function button(label: string): HTMLButtonElement {
    return Array.from(root().querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;
  }

  afterEach(() => {
    vi.useRealTimers();
    for (const r of http.match(() => true)) r.flush(r.request.url === '/api/filter-options' ? OPTIONS : page([], 0));
    http.verify();
  });

  it('loads newest first and renders formatted rows with status badges', async () => {
    await open('/orders');

    const request = ordersRequest();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');
    expect(request.request.params.has('sort')).toBe(false);
    expect(root().querySelector('.skeleton')).not.toBeNull();
    request.flush(page([row(1), row(2, { status: 'refunded', discount: 0 })], 2));
    await settle(fixture);

    const cells = Array.from(root().querySelectorAll('tbody tr:first-child td')).map((td) => td.textContent?.trim());
    expect(cells).toEqual(['Sep 30, 2026', 'HP-000001', 'C-00001', 'Europe', 'Online Store', 'Single Origin', 'HP-SO-ETH', '2', '$18.50', '5%', '$35.15', 'completed']);
    expect(root().querySelector('tbody tr:nth-child(2) .badge.refunded')?.textContent).toBe('refunded');
    expect(header('Date').getAttribute('aria-sort')).toBe('descending');
    expect(root().querySelector('.summary')?.textContent).toContain('1–2 of 2 order lines');
  });

  it('a header click sorts by that column and a second click flips the direction', async () => {
    await open('/orders');
    await respond(page([row(1)], 1));

    header('Region').querySelector('button')!.click();
    await settle(fixture);
    let request = ordersRequest();
    expect([request.request.params.get('sort'), request.request.params.get('dir')]).toEqual(['region', 'asc']);
    request.flush(page([row(1)], 1));
    await settle(fixture);
    expect(header('Region').getAttribute('aria-sort')).toBe('ascending');
    expect(header('Date').getAttribute('aria-sort')).toBe('none');

    header('Region').querySelector('button')!.click();
    await settle(fixture);
    request = ordersRequest();
    expect(request.request.params.get('dir')).toBe('desc');
    request.flush(page([row(1)], 1));
    await settle(fixture);
    expect(TestBed.inject(Router).url).toContain('sort=region&dir=desc');
  });

  it('the order-id search waits for a pause in typing', async () => {
    await open('/orders');
    await respond(page([row(1)], 1));
    vi.useFakeTimers();

    const input = root().querySelector<HTMLInputElement>('#g-search')!;
    // Effects run after every input event in the browser; flush them explicitly between keystrokes here.
    for (const text of ['0', '01', '012', '0123']) {
      input.value = text;
      input.dispatchEvent(new Event('input'));
      TestBed.tick();
      vi.advanceTimersByTime(100);
    }
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 101); // 299 ms after the last keystroke
    TestBed.tick();
    expect(http.match((r) => r.url === '/api/orders')).toHaveLength(0);

    vi.advanceTimersByTime(1);
    TestBed.tick();
    const requests = http.match((r) => r.url === '/api/orders');
    expect(requests).toHaveLength(1);
    expect(requests[0].request.params.get('q')).toBe('0123');
    expect(requests[0].request.params.get('page')).toBe('1');
    requests[0].flush(page([row(123)], 1));
  });

  it('shows an empty state with a way out', async () => {
    await open('/orders?region=Europe&q=zzz');
    await respond(page([], 0));

    expect(root().textContent).toContain('No orders match these filters.');
    button('Clear search and filters').click();
    await settle(fixture);

    const request = ordersRequest();
    expect(request.request.params.has('q')).toBe(false);
    expect(request.request.params.has('region')).toBe(false);
    request.flush(page([row(1)], 1));
  });

  it('shows an error with retry', async () => {
    await open('/orders');
    ordersRequest().flush({ detail: 'Database unavailable.' }, { status: 503, statusText: 'Unavailable' });
    await settle(fixture);

    expect(root().querySelector('[role="alert"]')?.textContent).toContain('Orders could not be loaded. Database unavailable.');
    button('Retry').click();
    await settle(fixture);
    await respond(page([row(1)], 1));
    expect(root().querySelectorAll('tbody tr').length).toBe(1);
  });

  it('pages forward and changes the page size from the first page', async () => {
    await open('/orders');
    await respond(page([row(1)], 120));
    expect(root().querySelector('.page-info')?.textContent).toContain('Page 1 of 5');

    button('Next').click();
    await settle(fixture);
    let request = ordersRequest();
    expect(request.request.params.get('page')).toBe('2');
    request.flush(page([row(26)], 120, 2));
    await settle(fixture);
    expect(root().querySelector('.summary')?.textContent).toContain('26–50 of 120 order lines');

    const size = root().querySelector<HTMLSelectElement>('#g-size')!;
    size.value = '50';
    size.dispatchEvent(new Event('change'));
    await settle(fixture);
    request = ordersRequest();
    expect([request.request.params.get('page'), request.request.params.get('pageSize')]).toEqual(['1', '50']);
    request.flush(page([row(1)], 120, 1, 50));
  });

  it('restores filters, status, search, sort and page from the URL', async () => {
    await open('/orders?region=Europe&status=refunded&q=HP-01&sort=lineTotal&dir=desc&page=3&pageSize=50');

    const params = ordersRequest().request.params;
    expect(params.getAll('region')).toEqual(['Europe']);
    expect(params.getAll('status')).toEqual(['refunded']);
    expect(params.get('q')).toBe('HP-01');
    expect([params.get('sort'), params.get('dir'), params.get('page'), params.get('pageSize')]).toEqual(['lineTotal', 'desc', '3', '50']);
    expect(root().querySelector<HTMLInputElement>('#g-search')!.value).toBe('HP-01');
    expect(root().querySelector<HTMLSelectElement>('#g-status')!.value).toBe('refunded');
  });

  it('a change to the shared filters starts again from page 1', async () => {
    await open('/orders?page=4');
    expect(ordersRequest().request.params.get('page')).toBe('4');

    TestBed.inject(FilterStore).update({ channels: ['Mobile App'] });
    await settle(fixture);

    const request = ordersRequest();
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.getAll('channel')).toEqual(['Mobile App']);
    request.flush(page([row(1)], 1));
  });

  it('keeps the filters chosen on the dashboard when opened from the menu', async () => {
    await open('/dashboard');
    TestBed.inject(FilterStore).update({ preset: 'ytd', regions: ['Latin America'] });

    await TestBed.inject(Router).navigateByUrl('/orders');
    await settle(fixture);

    const params = ordersRequest().request.params;
    expect(params.getAll('region')).toEqual(['Latin America']);
    expect(params.get('from')).toBe('2026-01-01');
    expect(TestBed.inject(Router).url).toContain('region=Latin%20America');
    expect(TestBed.inject(Router).url).not.toContain('granularity');
  });
});