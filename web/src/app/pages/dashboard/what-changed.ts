import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { drillQueryParams, drillToGroup } from '../../core/filters/drill-down';
import { FilterStore } from '../../core/filters/filter.store';
import { formatCurrency, formatDate } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { COMPARISON_SHORT_TITLES, compareParams } from './comparison';
import { DRIVER_DIMENSIONS, barWidth, formatSignedMoney, splitDrivers, summarySentence } from './drivers';
import { Driver, DriverDimension, RevenueDrivers } from './metrics.models';

/**
 * "What changed?": which regions, channels or categories drove the revenue change against the comparison period.
 * Shown by the dashboard only while a comparison is active. Each group opens its rows in either period.
 */
@Component({
  selector: 'app-what-changed',
  imports: [RouterLink],
  template: `
    <section class="card" aria-labelledby="changed-title">
      <header class="head">
        <h2 id="changed-title" class="widget-title">What changed?</h2>
        <div class="tabs" role="tablist" aria-label="Explain the change by" (keydown)="moveTab($event)">
          @for (d of dimensions; track d.value) {
            <button
              type="button"
              role="tab"
              class="btn"
              [id]="'changed-tab-' + d.value"
              [class.active]="by() === d.value"
              [attr.aria-selected]="by() === d.value"
              [attr.aria-controls]="'changed-panel'"
              [tabIndex]="by() === d.value ? 0 : -1"
              (click)="by.set(d.value)"
            >
              {{ d.label }}
            </button>
          }
        </div>
      </header>

      <div id="changed-panel" role="tabpanel" [attr.aria-labelledby]="'changed-tab-' + by()" aria-live="polite" [attr.aria-busy]="drivers.isLoading()">
        @if (drivers.error(); as error) {
          <div class="state error" role="alert">
            <p>The change could not be explained. {{ message(error) }}</p>
            <button type="button" class="btn" (click)="drivers.reload()">Try again</button>
          </div>
        } @else if (drivers.isLoading()) {
          <div aria-hidden="true">
            <div class="skeleton line long"></div>
            @for (i of [1, 2, 3]; track i) {
              <div class="skeleton line"></div>
            }
          </div>
          <span class="visually-hidden">Loading what changed…</span>
        } @else if (result(); as d) {
          <p class="summary">{{ sentence() }}</p>
          @if (d.drivers.length === 0) {
            <p class="state">No completed revenue in either period.</p>
          } @else {
            <p class="widget-subtitle">
              By {{ by() }}, largest change first. This period vs {{ comparisonTitle().toLowerCase() }}
              ({{ date(d.comparison.from) }} to {{ date(d.comparison.to) }}).
            </p>
            <ul class="drivers">
              @for (x of split().shown; track x.key) {
                <li class="driver">
                  <a class="name" routerLink="/orders" [queryParams]="currentRows(x)">
                    {{ x.key }}<span class="visually-hidden">: this period's orders</span>
                  </a>
                  <span class="amount" [class.up]="x.contribution > 0" [class.down]="x.contribution < 0">{{ signed(x.contribution) }}</span>
                  <span class="track" aria-hidden="true">
                    <span class="bar" [class.down]="x.contribution < 0" [style.width.%]="width(x.contribution) / 2"></span>
                  </span>
                  <span class="detail">
                    {{ money(x.currentRevenue) }} vs {{ money(x.comparisonRevenue) }} ·
                    <a routerLink="/orders" [queryParams]="comparisonRows(x)">
                      {{ comparisonTitle() }} orders<span class="visually-hidden"> for {{ x.key }}</span>
                    </a>
                  </span>
                </li>
              }
              @if (split().others; as o) {
                <li class="driver others">
                  <span class="name">{{ o.count }} others</span>
                  <span class="amount" [class.up]="o.contribution > 0" [class.down]="o.contribution < 0">{{ signed(o.contribution) }}</span>
                  <span class="track" aria-hidden="true">
                    <span class="bar" [class.down]="o.contribution < 0" [style.width.%]="width(o.contribution) / 2"></span>
                  </span>
                  <span class="detail">{{ money(o.currentRevenue) }} vs {{ money(o.comparisonRevenue) }}</span>
                </li>
              }
            </ul>
          }
        }
      </div>
    </section>
  `,
  styles: `
    .head {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-3);
      margin-bottom: var(--space-3);
    }
    .tabs {
      display: inline-flex;
    }
    .tabs .btn {
      min-height: 2.25rem;
      border-radius: 0;
      margin-left: -1px;
    }
    .tabs .btn:first-child {
      border-radius: var(--radius-sm) 0 0 var(--radius-sm);
    }
    .tabs .btn:last-child {
      border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
    }
    .tabs .btn.active {
      position: relative;
      background: var(--color-primary);
      border-color: var(--color-primary);
      color: var(--color-primary-ink);
    }
    .summary {
      margin: 0 0 var(--space-2);
      font-size: var(--font-size-md);
      font-weight: var(--font-weight-medium);
    }
    .drivers {
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
    }
    .driver {
      display: grid;
      grid-template-columns: minmax(7rem, 12rem) 6.5rem minmax(6rem, 1fr);
      grid-template-areas: 'name amount track' 'detail detail detail';
      align-items: center;
      gap: var(--space-1) var(--space-3);
      padding: var(--space-2) 0;
      border-top: 1px solid var(--color-border);
    }
    .name {
      grid-area: name;
      font-weight: var(--font-weight-medium);
      overflow-wrap: anywhere;
    }
    .amount {
      grid-area: amount;
      text-align: right;
      font-variant-numeric: tabular-nums;
      font-weight: var(--font-weight-medium);
    }
    .amount.up {
      color: var(--color-success);
    }
    .amount.down {
      color: var(--color-danger);
    }
    /* A diverging bar: increases grow right from the centre line, decreases grow left. */
    .track {
      grid-area: track;
      position: relative;
      height: 0.75rem;
      background: linear-gradient(var(--color-border), var(--color-border)) center / 1px 100% no-repeat;
    }
    .bar {
      position: absolute;
      top: 0;
      bottom: 0;
      left: 50%;
      border-radius: 0 3px 3px 0;
      background: var(--color-success);
    }
    .bar.down {
      left: auto;
      right: 50%;
      border-radius: 3px 0 0 3px;
      background: var(--color-danger);
    }
    .detail {
      grid-area: detail;
      color: var(--color-muted);
      font-size: var(--font-size-sm);
      font-variant-numeric: tabular-nums;
    }
    .others .name {
      color: var(--color-muted);
      font-weight: normal;
    }
    .line {
      height: 0.9rem;
      margin: var(--space-3) 0;
    }
    .line.long {
      height: 1.1rem;
      width: 80%;
    }
    @media (max-width: 480px) {
      .driver {
        grid-template-columns: minmax(0, 1fr) auto;
        grid-template-areas: 'name amount' 'track track' 'detail detail';
      }
    }
  `,
})
export class WhatChanged {
  private readonly filters = inject(FilterStore);
  protected readonly dimensions = DRIVER_DIMENSIONS;
  protected readonly by = signal<DriverDimension>('region');

  protected readonly drivers = httpResource<RevenueDrivers>(() => {
    const compare = this.filters.compareParam();
    // Not requested without a comparison (the dashboard hides the panel then anyway).
    return compare ? { url: '/api/metrics/drivers', params: { ...this.filters.apiParams(), by: this.by(), ...compareParams(compare) } } : undefined;
  });

  protected readonly result = computed(() => (this.drivers.hasValue() ? this.drivers.value() : undefined));
  protected readonly sentence = computed(() => {
    const d = this.result();
    return d ? summarySentence(d) : '';
  });
  protected readonly split = computed(() => splitDrivers(this.result()?.drivers ?? []));
  protected readonly comparisonTitle = computed(() => {
    const mode = this.result()?.comparison.mode;
    return mode ? COMPARISON_SHORT_TITLES[mode] : '';
  });
  private readonly largest = computed(() => {
    const { shown, others } = this.split();
    return Math.max(0, ...shown.map((d) => Math.abs(d.contribution)), Math.abs(others?.contribution ?? 0));
  });

  /** The grid on this period's completed lines for the group (the rows behind its current revenue). */
  protected currentRows(driver: Driver): Record<string, string | string[]> {
    return drillQueryParams(drillToGroup(this.filters.filter(), this.dimension(), driver.key));
  }

  /** The same group's completed lines in the comparison period. */
  protected comparisonRows(driver: Driver): Record<string, string | string[]> {
    const comparison = this.result()!.comparison;
    const group = drillToGroup(this.filters.filter(), this.dimension(), driver.key);
    return drillQueryParams({ ...group, preset: 'custom', from: comparison.from, to: comparison.to });
  }

  /** Arrow keys, Home and End move between the tabs (WAI-ARIA tabs pattern, automatic activation). */
  protected moveTab(event: KeyboardEvent): void {
    const values = this.dimensions.map((d) => d.value);
    const current = values.indexOf(this.by());
    const next =
      event.key === 'ArrowRight' ? (current + 1) % values.length
      : event.key === 'ArrowLeft' ? (current - 1 + values.length) % values.length
      : event.key === 'Home' ? 0
      : event.key === 'End' ? values.length - 1
      : null;
    if (next === null) {
      return;
    }
    event.preventDefault();
    this.by.set(values[next]);
    (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
  }

  protected width(contribution: number): number {
    return barWidth(contribution, this.largest());
  }

  protected signed(value: number): string {
    return formatSignedMoney(value);
  }

  protected money(value: number): string {
    return formatCurrency(value);
  }

  protected date(value: string): string {
    return formatDate(value);
  }

  protected message(error: unknown): string {
    return problemMessage(error, 'Try again.');
  }

  /** The dimension the shown result was loaded for (the tab may already have moved on while it reloads). */
  private dimension(): DriverDimension {
    return this.result()?.by ?? this.by();
  }
}
