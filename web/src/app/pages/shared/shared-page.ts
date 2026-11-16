import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, effect, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FilterStore } from '../../core/filters/filter.store';
import { Granularity, formatDate } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { BreakdownChart } from '../dashboard/breakdown-chart';
import { COMPARISON_NAMES } from '../dashboard/comparison';
import { DASHBOARD_SOURCE } from '../dashboard/dashboard-source';
import { KpiCards } from '../dashboard/kpi-cards';
import { ComparisonDates } from '../dashboard/metrics.models';
import { RevenueChart } from '../dashboard/revenue-chart';
import { WhatChanged } from '../dashboard/what-changed';

/** What the shared link describes, from GET /api/shared/{token}. */
interface SharedViewInfo {
  readonly name: string;
  readonly preset: string;
  readonly from: string | null;
  readonly to: string | null;
  readonly regions: readonly string[];
  readonly channels: readonly string[];
  readonly categories: readonly string[];
  readonly granularity: Granularity;
  readonly comparison: ComparisonDates | null;
  readonly expiresAt: string;
}

function token(): string {
  return encodeURIComponent(inject(ActivatedRoute).snapshot.paramMap.get('token') ?? '');
}

/**
 * A saved view opened through a share link, without signing in: the dashboard's figures for the view's saved filter,
 * read-only. The widgets are the dashboard's own, pointed at the shared endpoints (which ignore any filter the page
 * sends) and with drill-down off. Its own filter store holds the view, so nothing leaks into a signed-in session.
 */
@Component({
  selector: 'app-shared-page',
  imports: [BreakdownChart, KpiCards, RevenueChart, WhatChanged],
  providers: [FilterStore, { provide: DASHBOARD_SOURCE, useFactory: () => ({ base: `/api/shared/${token()}`, drillDown: false }) }],
  template: `
    <header class="top">
      <span class="brand">Numera</span>
      <span class="tag">Shared dashboard · read-only</span>
    </header>

    <main id="main">
      @if (info.error(); as error) {
        <section class="card state" [attr.role]="gone(error) ? null : 'alert'">
          @if (gone(error)) {
            <h1>This link no longer works</h1>
            <p>It has expired, was revoked, or the address is incomplete. Ask the person who shared it for a new link.</p>
          } @else {
            <h1>The dashboard could not be loaded</h1>
            <p>{{ message(error) }}</p>
            <button type="button" class="btn" (click)="info.reload()">Try again</button>
          }
        </section>
      } @else if (info.isLoading()) {
        <p role="status">Loading the shared dashboard…</p>
        <div class="skeleton block" aria-hidden="true"></div>
      } @else if (info.hasValue()) {
        <header class="page-header">
          <h1>{{ info.value().name }}</h1>
          <p>{{ summary() }}</p>
          <p class="muted">Harbor &amp; Pine Coffee, fictional demo data. This link works until {{ date(info.value().expiresAt) }}.</p>
        </header>
        <div class="layout">
          <app-kpi-cards />
          @if (info.value().comparison) {
            <app-what-changed />
          }
          <app-revenue-chart />
          <div class="breakdowns">
            <app-breakdown-chart by="category" title="Revenue by category" />
            <app-breakdown-chart by="region" title="Revenue by region" />
          </div>
        </div>
      }
    </main>
  `,
  styles: `
    :host { display: block; min-height: 100vh; background: var(--color-bg); }
    .top {
      display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--space-2) var(--space-4);
      padding: var(--space-3) var(--space-5); background: var(--color-sidebar); color: var(--color-sidebar-ink);
    }
    .brand { font-weight: var(--font-weight-bold); font-size: var(--font-size-lg); }
    .tag { color: var(--color-sidebar-muted); font-size: var(--font-size-sm); }
    main { max-width: 80rem; padding: var(--space-5); }
    .page-header h1 { font-size: var(--font-size-xl); }
    .page-header p { margin: var(--space-2) 0 0; }
    .muted { color: var(--color-muted); font-size: var(--font-size-sm); }
    .page-header .muted { margin-bottom: var(--space-5); }
    .layout { display: grid; gap: var(--space-4); }
    .layout > *, .breakdowns > * { min-width: 0; }
    .breakdowns { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-4); }
    .state h1 { font-size: var(--font-size-lg); }
    .block { height: 20rem; margin-top: var(--space-4); }
    @media (max-width: 900px) { .breakdowns { grid-template-columns: 1fr; } }
    @media (max-width: 480px) { main { padding: var(--space-4); } .top { padding: var(--space-3) var(--space-4); } }
  `,
})
export class SharedPage {
  private readonly filters = inject(FilterStore);
  private readonly source = inject(DASHBOARD_SOURCE);

  protected readonly info = httpResource<SharedViewInfo>(() => this.source.base);

  /** "Jul 1, 2026 to Sep 30, 2026 · Europe · compared with the previous period" */
  protected readonly summary = computed(() => {
    if (!this.info.hasValue()) {
      return '';
    }
    const v = this.info.value();
    const dates =
      v.from && v.to ? `${formatDate(v.from)} to ${formatDate(v.to)}`
      : v.from ? `From ${formatDate(v.from)}`
      : v.to ? `Up to ${formatDate(v.to)}`
      : 'All data';
    const filters = [...v.regions, ...v.channels, ...v.categories];
    const compare = v.comparison ? `compared with the ${COMPARISON_NAMES[v.comparison.mode]}` : null;
    return [dates, ...(filters.length ? filters : ['all regions, channels and categories']), compare].filter((p) => p).join(' · ');
  });

  constructor() {
    // The page's own filter store takes the view's resolved dates, so the widgets show and compare what the
    // server serves (the shared endpoints apply the saved view themselves, whatever the page sends).
    effect(() => {
      if (!this.info.hasValue()) {
        return;
      }
      const v = this.info.value();
      const bounded = v.from !== null || v.to !== null;
      this.filters.replace({
        preset: bounded ? 'custom' : 'all',
        from: v.from,
        to: v.to,
        regions: [...v.regions],
        channels: [...v.channels],
        categories: [...v.categories],
        granularity: v.granularity,
        compare: v.comparison?.mode ?? 'none',
      });
    });
  }

  protected gone(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 404;
  }

  protected message(error: unknown): string {
    return problemMessage(error, 'Check your connection and try again.');
  }

  protected date(value: string): string {
    return formatDate(value.slice(0, 10));
  }
}
