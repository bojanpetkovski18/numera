import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FilterStore } from '../../core/filters/filter.store';
import { AskBox } from './ask-box';
import { AskAnswer, AskRequest } from './metrics.models';
import { COMPARED_SUMMARY, GROUPS, SERVER_ERROR, createdCharts, dashboardTestProviders, settle } from './testing';

const REQUEST: AskRequest = {
  metric: 'revenue',
  view: 'total',
  granularity: null,
  dimension: null,
  from: '2025-03-01',
  to: '2025-03-31',
  regions: ['Europe'],
  channels: [],
  categories: [],
  compare: 'previous',
};

const ANSWER: AskAnswer = { status: 'answered', request: REQUEST, result: COMPARED_SUMMARY, reason: null };

describe('AskBox', () => {
  let fixture: ComponentFixture<AskBox>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  async function render(enabled = true): Promise<void> {
    TestBed.configureTestingModule({ imports: [AskBox], providers: dashboardTestProviders() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AskBox);
    await settle(fixture);
    http.expectOne('/api/ask/status').flush({ enabled, maxQuestionLength: 300 });
    await settle(fixture);
  }

  async function ask(question: string, answer: AskAnswer | { error: object; status: number; statusText: string }): Promise<void> {
    const input = el().querySelector<HTMLInputElement>('#ask-question')!;
    input.value = question;
    input.dispatchEvent(new Event('input'));
    el().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    expect(el().textContent).toContain('Working out your question');
    const request = http.expectOne((r) => r.url === '/api/ask' && r.method === 'POST');
    expect(request.request.body).toEqual({ question: question.trim() });
    if ('status' in answer && typeof answer.status === 'number') {
      request.flush(answer.error, { status: answer.status, statusText: answer.statusText });
    } else {
      request.flush(answer);
    }
    await settle(fixture);
  }

  afterEach(() => http.verify());

  it('stays hidden when the feature is off', async () => {
    await render(false);
    expect(el().querySelector('section')).toBeNull();
  });

  it('answers with the figure, its change and the request as chips', async () => {
    await render();

    await ask('  Revenue in Europe in March 2025 vs the month before?  ', ANSWER);

    expect(el().querySelector('h3')?.textContent).toBe('Revenue');
    expect(el().querySelector('.figure')?.textContent).toBe('$95.00');
    expect(el().querySelector('.change')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      '▲ +5.6%Up 5.6% vs previous period (Jan 29, 2025 to Feb 28, 2025): $90.00',
    );
    expect(Array.from(el().querySelectorAll('.chips li')).map((li) => li.firstChild?.textContent?.trim())).toEqual([
      'Mar 1, 2025 to Mar 31, 2025',
      'Europe',
      'vs previous period',
    ]);
    expect(el().querySelector('.chips button')?.textContent).toContain('Remove Mar 1, 2025 to Mar 31, 2025');
  });

  it('removing a chip re-runs the request through the dashboard endpoint, without a new question', async () => {
    await render();
    await ask('Revenue in Europe in March 2025?', ANSWER);

    Array.from(el().querySelectorAll<HTMLButtonElement>('.chips button')).find((b) => b.textContent?.includes('Remove Europe'))!.click();
    const request = http.expectOne((r) => r.url === '/api/metrics/summary');
    expect(request.request.params.get('from')).toBe('2025-03-01');
    expect(request.request.params.has('region')).toBe(false);
    expect(request.request.params.get('compare')).toBe('previous');
    request.flush({ ...COMPARED_SUMMARY, revenue: 130 });
    await settle(fixture);

    expect(el().querySelector('.figure')?.textContent).toBe('$130.00');
    expect(el().textContent).not.toContain('Remove Europe');
    http.expectNone('/api/ask');
  });

  it('shows a split by group as bars and a table, ordered by the metric asked for', async () => {
    await render();
    await ask('Orders by category?', {
      status: 'answered',
      request: { ...REQUEST, metric: 'orders', view: 'by_dimension', dimension: 'category', from: null, to: null, regions: [], compare: 'none' },
      result: { by: 'category', groups: [...GROUPS, { key: 'Accessories', revenue: 5, orders: 4 }] },
      reason: null,
    });

    expect(el().querySelector('h3')?.textContent).toBe('Orders by category');
    expect(el().querySelector('.chips')?.textContent?.trim()).toBe('All data');
    const rows = Array.from(el().querySelectorAll('tbody tr')).map((tr) => Array.from(tr.children).map((c) => c.textContent));
    expect(rows).toEqual([['Accessories', '4'], ['Blends', '3'], ['Equipment', '1']]);
    expect(createdCharts.at(-1)?.type).toBe('bar');
    expect(createdCharts.at(-1)?.data.datasets[0].data).toEqual([4, 3, 1]);
  });

  it('applies the request to the dashboard', async () => {
    await render();
    await ask('Revenue in Europe in March 2025?', ANSWER);

    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Show on the dashboard')!.click();

    expect(TestBed.inject(FilterStore).filter()).toEqual(expect.objectContaining({
      preset: 'custom', from: '2025-03-01', to: '2025-03-31', regions: ['Europe'], compare: 'previous',
    }));
  });

  it('explains when a question cannot be answered, and shows limits and errors', async () => {
    await render();
    await ask('What was our profit?', { status: 'cannot_answer', request: null, result: null, reason: 'Numera has no profit figures.' });
    expect(el().querySelector('[role="status"]')?.textContent).toBe('Numera has no profit figures.');

    await ask('And now?', {
      error: { title: 'Daily question limit reached', detail: 'The demo answers up to 200 questions a day across all visitors.' },
      status: 429,
      statusText: 'Too Many Requests',
    });
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('The demo answers up to 200 questions a day');

    await ask('Again?', { error: { detail: 'The question could not be answered right now. Try again in a minute.' }, ...SERVER_ERROR, status: 503 });
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('Try again in a minute.');
  });

  it('asks an example question with one click', async () => {
    await render();
    el().querySelector<HTMLButtonElement>('.examples button')!.click();
    await settle(fixture);

    const request = http.expectOne('/api/ask');
    expect(request.request.body).toEqual({ question: 'How did revenue in Q3 2026 compare with the previous quarter?' });
    request.flush(ANSWER);
  });
});
