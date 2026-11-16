import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { FilterStore } from '../../core/filters/filter.store';
import { formatDate, formatPeriod } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { ChartCanvas } from '../../shared/chart/chart-canvas';
import { ASK_EXAMPLES, RequestChip, formatMetric, requestChips, requestFilter, requestQuery, requestTitle } from './ask';
import { answerChartConfig, readChartColors } from './chart-configs';
import { COMPARISON_NAMES, COMPARISON_TITLES, changeView } from './comparison';
import { AskAnswer, AskRequest, MetricsSummary, RevenueBreakdown, RevenueTimeseries } from './metrics.models';

interface AskStatus {
  readonly enabled: boolean;
  readonly maxQuestionLength: number;
}

type Result = MetricsSummary | RevenueTimeseries | RevenueBreakdown;

interface Row {
  readonly label: string;
  readonly value: string;
  readonly comparison: string | null;
}

/**
 * "Ask a question": a question in plain English becomes one dashboard request, chosen by an AI model from Numera's
 * own metrics and filters and checked by the API. The answer shows the request it ran as chips the user can remove
 * (which re-runs it through the normal dashboard endpoints, without asking the model again) and can be applied to
 * the dashboard. Shown only when the API has the feature on.
 */
@Component({
  selector: 'app-ask-box',
  imports: [ChartCanvas],
  template: `
    @if (status.hasValue() && status.value().enabled) {
      <section class="card" aria-labelledby="ask-title">
        <h2 id="ask-title" class="widget-title">Ask a question</h2>
        <form class="ask" (submit)="submit($event)">
          <label for="ask-question" class="visually-hidden">Question about the sales data</label>
          <input
            id="ask-question"
            type="text"
            [maxLength]="status.value().maxQuestionLength"
            placeholder="For example: which channel grew most in Q3 2026?"
            [value]="question()"
            (input)="question.set(value($event))"
            aria-describedby="ask-hint"
          />
          <button type="submit" class="btn primary" [disabled]="busy()">{{ busy() ? 'Working…' : 'Ask' }}</button>
        </form>
        <p class="hint" id="ask-hint">
          An AI model turns your question into one of the dashboard's own requests, which Numera checks and runs. Try:
          @for (example of examples; track example; let last = $last) {
            <button type="button" class="link" (click)="ask(example)">{{ example }}</button>{{ last ? '' : ' · ' }}
          }
        </p>

        <div aria-live="polite">
          @if (busy()) {
            <p class="muted" role="status">Working out your question…</p>
          } @else if (error(); as message) {
            <p class="error" role="alert">{{ message }}</p>
          } @else if (answer(); as a) {
            @if (a.status === 'cannot_answer' || !request()) {
              <p class="note" role="status">{{ a.reason }}</p>
            } @else if (request(); as r) {
              <div class="answer">
                <h3>{{ title() }}</h3>
                <ul class="chips" aria-label="What was looked up">
                  @for (chip of chips(); track chip.label) {
                    <li>
                      {{ chip.label }}
                      <button type="button" class="remove" (click)="edit(chip)">
                        <span aria-hidden="true">×</span><span class="visually-hidden">Remove {{ chip.label }}</span>
                      </button>
                    </li>
                  } @empty {
                    <li>All data</li>
                  }
                </ul>

                @if (summary(); as s) {
                  <p class="figure">{{ metricValue(s) }}</p>
                  @if (s.comparison; as c) {
                    <p class="change" [class.good]="change()?.tone === 'good'" [class.bad]="change()?.tone === 'bad'">
                      @if (change(); as ch) {
                        <span aria-hidden="true">{{ ch.arrow }} {{ ch.text }}</span><span class="visually-hidden">{{ ch.spoken }}</span>
                      } @else {
                        Nothing to compare with
                      }
                      <span class="muted"> vs {{ comparisonName(c.mode) }} ({{ date(c.from) }} to {{ date(c.to) }}): {{ comparedFigure(s) }}</span>
                    </p>
                  }
                } @else if (rows().length) {
                  <app-chart-canvas [config]="chart()" [label]="title() + ': ' + rowsText()" [style.--chart-height]="chartHeight()" />
                  <details class="table-toggle">
                    <summary>Show data table</summary>
                    <div class="table-scroll">
                      <table class="data-table">
                        <caption class="visually-hidden">{{ title() }}</caption>
                        <thead>
                          <tr>
                            <th scope="col">{{ r.view === 'over_time' ? 'Period' : 'Group' }}</th>
                            <th scope="col">{{ metricLabel() }}</th>
                            @if (comparisonTitle(); as t) {
                              <th scope="col">{{ t }}</th>
                            }
                          </tr>
                        </thead>
                        <tbody>
                          @for (row of rows(); track row.label) {
                            <tr><th scope="row">{{ row.label }}</th><td>{{ row.value }}</td>@if (row.comparison !== null) {<td>{{ row.comparison }}</td>}</tr>
                          }
                        </tbody>
                      </table>
                    </div>
                  </details>
                } @else {
                  <p class="note">No data for this request.</p>
                }

                <button type="button" class="btn" (click)="showOnDashboard()">Show on the dashboard</button>
              </div>
            }
          }
        </div>
      </section>
    }
  `,
  styles: `
    .ask { display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-3); margin-top: var(--space-3); }
    .ask input {
      flex: 1 1 16rem; min-width: 0; min-height: 2.75rem; padding: 0 var(--space-3); font: inherit; color: var(--color-ink);
      border: 1px solid var(--color-border); border-radius: var(--radius-sm); background: var(--color-surface);
    }
    .hint, .muted { color: var(--color-muted); font-size: var(--font-size-sm); }
    .hint { margin: var(--space-2) 0 0; }
    .link {
      padding: 0; border: 0; background: none; font: inherit; color: var(--color-primary); text-decoration: underline; cursor: pointer; text-align: left;
    }
    .answer { margin-top: var(--space-4); padding-top: var(--space-4); border-top: 1px solid var(--color-border); }
    h3 { margin: 0; font-size: var(--font-size-md); }
    .chips { display: flex; flex-wrap: wrap; gap: var(--space-2); margin: var(--space-2) 0 var(--space-3); padding: 0; list-style: none; }
    .chips li {
      display: inline-flex; align-items: center; gap: var(--space-1); padding: 0 0 0 var(--space-3); min-height: 2rem;
      border: 1px solid var(--color-border); border-radius: 999px; font-size: var(--font-size-sm);
    }
    .chips li:not(:has(button)) { padding-right: var(--space-3); }
    .remove { min-width: 2rem; min-height: 2rem; border: 0; border-radius: 999px; background: none; font: inherit; cursor: pointer; color: var(--color-muted); }
    .figure { margin: 0; font-size: var(--font-size-xl); font-weight: var(--font-weight-bold); font-variant-numeric: tabular-nums; }
    .change { margin: var(--space-1) 0 0; font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); }
    .change.good { color: var(--color-success); }
    .change.bad { color: var(--color-danger); }
    .note { margin: var(--space-3) 0 0; }
    .error { color: var(--color-danger); }
    .answer > .btn { margin-top: var(--space-3); }
  `,
})
export class AskBox {
  private readonly http = inject(HttpClient);
  private readonly filters = inject(FilterStore);
  private readonly colors = readChartColors();

  protected readonly examples = ASK_EXAMPLES;
  protected readonly status = httpResource<AskStatus>(() => '/api/ask/status');
  protected readonly question = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly answer = signal<AskAnswer | null>(null);
  /** The request on screen: the model's, or the user's edit of it. */
  protected readonly request = signal<AskRequest | null>(null);
  private readonly result = signal<Result | null>(null);

  protected readonly title = computed(() => (this.request() ? requestTitle(this.request()!) : ''));
  protected readonly chips = computed(() => (this.request() ? requestChips(this.request()!) : []));
  protected readonly metricLabel = computed(() => requestTitle({ ...this.request()!, view: 'total' }));

  protected readonly summary = computed(() => (this.request()?.view === 'total' ? (this.result() as MetricsSummary | null) : null));

  protected readonly change = computed(() => {
    const s = this.summary();
    const metric = this.request()?.metric;
    if (!s?.changes || !metric) {
      return null;
    }
    const c = s.changes;
    return metric === 'refund_rate'
      ? changeView(c.refundRateDifference, 'points', false)
      : changeView(metric === 'revenue' ? c.revenue : metric === 'orders' ? c.orders : c.averageOrderValue, 'percent', true);
  });

  protected readonly comparisonTitle = computed(() => {
    const result = this.result();
    return result && 'points' in result && result.comparison ? COMPARISON_TITLES[result.comparison.mode] : null;
  });

  /** One row per period or group, in the metric's format. */
  protected readonly rows = computed<Row[]>(() => {
    const r = this.request();
    const result = this.result();
    if (!r || !result || r.view === 'total') {
      return [];
    }
    const orders = r.metric === 'orders';
    if ('points' in result) {
      return result.points.map((p) => ({
        label: formatPeriod(p.periodStart, result.granularity),
        value: formatMetric(r.metric, orders ? p.orders : p.revenue),
        comparison: result.comparison ? formatMetric(r.metric, (orders ? p.comparisonOrders : p.comparisonRevenue) ?? 0) : null,
      }));
    }
    const groups = 'groups' in result ? [...result.groups] : [];
    if (orders) {
      groups.sort((a, b) => b.orders - a.orders || a.key.localeCompare(b.key));
    }
    return groups.map((g) => ({ label: g.key, value: formatMetric(r.metric, orders ? g.orders : g.revenue), comparison: null }));
  });

  protected readonly rowsText = computed(() => this.rows().map((row) => `${row.label} ${row.value}`).join(', '));

  protected readonly chart = computed(() => {
    const r = this.request()!;
    const result = this.result();
    const orders = r.metric === 'orders';
    const format = (v: number) => formatMetric(r.metric, v);
    if (result && 'points' in result) {
      const comparison = result.comparison
        ? { label: COMPARISON_TITLES[result.comparison.mode], values: result.points.map((p) => (orders ? p.comparisonOrders : p.comparisonRevenue) ?? 0) }
        : null;
      return answerChartConfig('line', this.rows().map((x) => x.label), result.points.map((p) => (orders ? p.orders : p.revenue)), this.metricLabel(), format, this.colors, comparison);
    }
    const groups = result && 'groups' in result ? result.groups : [];
    const values = new Map(groups.map((g) => [g.key, orders ? g.orders : g.revenue]));
    return answerChartConfig('bar', this.rows().map((x) => x.label), this.rows().map((x) => values.get(x.label) ?? 0), this.metricLabel(), format, this.colors);
  });

  protected readonly chartHeight = computed(() => (this.request()?.view === 'by_dimension' ? `${Math.max(8, this.rows().length * 2.5 + 3)}rem` : '16rem'));

  protected submit(event: Event): void {
    event.preventDefault();
    this.ask(this.question());
  }

  protected ask(question: string): void {
    const text = question.trim();
    if (!text || this.busy()) {
      return;
    }
    this.question.set(text);
    this.run(this.http.post<AskAnswer>('/api/ask', { question: text }), (answer) => {
      this.answer.set(answer);
      this.request.set(answer.request);
      this.result.set(answer.result);
    });
  }

  /** Removes one part of the request and runs it again through the dashboard endpoints (no new question). */
  protected edit(chip: RequestChip): void {
    const { url, params } = requestQuery(chip.without);
    this.run(this.http.get<Result>(url, { params }), (result) => {
      this.request.set(chip.without);
      this.result.set(result);
    });
  }

  protected showOnDashboard(): void {
    const r = this.request();
    if (r) {
      this.filters.replace(requestFilter(r, this.filters.filter()));
    }
  }

  protected comparedFigure(s: MetricsSummary): string {
    return s.comparison ? this.metricValue(s.comparison) : '';
  }

  protected comparisonName(mode: 'previous' | 'year'): string {
    return COMPARISON_NAMES[mode];
  }

  protected date(value: string): string {
    return formatDate(value);
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected metricValue(s: Pick<MetricsSummary, 'revenue' | 'orders' | 'averageOrderValue' | 'refundRate'>): string {
    const metric = this.request()!.metric;
    return formatMetric(metric, { revenue: s.revenue, orders: s.orders, average_order_value: s.averageOrderValue, refund_rate: s.refundRate }[metric]);
  }

  private run<T>(request: Observable<T>, done: (value: T) => void): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        done(value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(problemMessage(error, 'The question could not be answered. Try again.'));
      },
    });
  }
}
