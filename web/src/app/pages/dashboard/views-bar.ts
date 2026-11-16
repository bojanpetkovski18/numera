import { Component, computed, inject, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { DATE_PRESETS, sameFilter } from '../../core/filters/filter-state';
import { FilterStore } from '../../core/filters/filter.store';
import { formatDate } from '../../core/format/format';
import { problemMessage } from '../../core/http/problem';
import { CreatedShareLink, SHARE_DAYS, SavedView, ShareDays, ViewsApi, fromViewSettings, toViewSettings } from '../../core/views/views-api';

type Panel = 'none' | 'save' | 'list';

/**
 * Saved views on the dashboard: save what is on screen under a name, reopen, rename or delete a view, and create
 * a read-only link to it. Shows which saved view the screen currently matches.
 */
@Component({
  selector: 'app-views-bar',
  template: `
    <div class="bar">
      @if (current(); as view) {
        <p class="current">Saved view: <strong>{{ view.name }}</strong></p>
      }
      <button type="button" class="btn" aria-controls="views-save" [attr.aria-expanded]="panel() === 'save'" (click)="toggle('save')">
        Save view
      </button>
      <button type="button" class="btn" aria-controls="views-list" [attr.aria-expanded]="panel() === 'list'" (click)="toggle('list')">
        Saved views{{ status() === 'loaded' ? ' (' + views().length + ')' : '' }}
      </button>
    </div>

    @if (panel() === 'save') {
      <form id="views-save" class="card panel" (submit)="save($event)">
        <label for="view-name">Name this view</label>
        <div class="row">
          <input id="view-name" type="text" maxlength="80" [value]="name()" (input)="name.set(value($event))" aria-describedby="view-name-hint" />
          <button type="submit" class="btn primary" [disabled]="busy()">Save</button>
          <button type="button" class="btn" (click)="panel.set('none')">Cancel</button>
        </div>
        <p class="muted" id="view-name-hint">Keeps the dates, filters, grouping and comparison. Relative dates such as "Last 90 days" move with the calendar.</p>
        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </form>
    }

    @if (panel() === 'list') {
      <section id="views-list" class="card panel" aria-label="Saved views">
        @switch (status()) {
          @case ('loading') {
            <p class="muted" role="status">Loading saved views…</p>
          }
          @case ('error') {
            <p class="error" role="alert">Saved views could not be loaded. <button type="button" class="btn" (click)="load()">Try again</button></p>
          }
          @default {
            @if (views().length === 0) {
              <p class="muted">No saved views yet. Set the filters you want, then choose "Save view".</p>
            }
            <ul>
              @for (view of views(); track view.id) {
                <li>
                  <div class="row">
                    <span class="name">{{ view.name }}</span>
                    <span class="muted">{{ describe(view) }}</span>
                    <span class="actions">
                      <button type="button" class="btn" (click)="open(view)">Open<span class="visually-hidden"> {{ view.name }}</span></button>
                      <button type="button" class="btn" [attr.aria-expanded]="sharing() === view.id" (click)="startShare(view)">
                        Share<span class="visually-hidden"> {{ view.name }}</span>
                      </button>
                      <button type="button" class="btn" [attr.aria-expanded]="renaming() === view.id" (click)="startRename(view)">
                        Rename<span class="visually-hidden"> {{ view.name }}</span>
                      </button>
                      <button type="button" class="btn" (click)="remove(view)">Delete<span class="visually-hidden"> {{ view.name }}</span></button>
                    </span>
                  </div>

                  @if (renaming() === view.id) {
                    <form class="row sub" (submit)="rename($event, view)">
                      <label class="visually-hidden" [for]="'rename-' + view.id">New name for {{ view.name }}</label>
                      <input [id]="'rename-' + view.id" type="text" maxlength="80" [value]="view.name" (input)="name.set(value($event))" />
                      <button type="submit" class="btn primary" [disabled]="busy()">Save name</button>
                      <button type="button" class="btn" (click)="renaming.set(null)">Cancel</button>
                    </form>
                  }

                  @if (sharing() === view.id) {
                    <div class="sub share">
                      @if (created(); as link) {
                        <label [for]="'share-url-' + view.id">Read-only link, works until {{ date(link.link.expiresAt) }}</label>
                        <div class="row">
                          <input [id]="'share-url-' + view.id" type="text" readonly [value]="fullUrl(link)" />
                          <button type="button" class="btn primary" (click)="copy(link)">{{ copied() ? 'Copied' : 'Copy link' }}</button>
                        </div>
                        <p class="muted">
                          Anyone with this link sees this view's figures without signing in, but no order rows. The link is
                          shown only now; you can revoke it in Settings.
                        </p>
                      } @else {
                        <div class="row">
                          <label [for]="'share-days-' + view.id">Link works for</label>
                          <select [id]="'share-days-' + view.id" (change)="days.set(daysValue($event))">
                            @for (d of shareDays; track d) {
                              <option [value]="d" [selected]="d === days()">{{ d === 1 ? '1 day' : d + ' days' }}</option>
                            }
                          </select>
                          <button type="button" class="btn primary" [disabled]="busy()" (click)="share(view)">Create link</button>
                        </div>
                      }
                    </div>
                  }
                </li>
              }
            </ul>
            @if (error(); as message) {
              <p class="error" role="alert">{{ message }}</p>
            }
          }
        }
      </section>
    }
  `,
  styles: `
    :host { display: block; }
    .bar { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-3); }
    .current { margin: 0 auto 0 0; font-size: var(--font-size-sm); color: var(--color-muted); }
    .panel { margin-top: var(--space-3); }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-3); }
    .sub { margin-top: var(--space-2); }
    label { font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); }
    input, select {
      min-height: 2.5rem; padding: 0 var(--space-2); font: inherit; color: var(--color-ink);
      border: 1px solid var(--color-border); border-radius: var(--radius-sm); background: var(--color-surface);
    }
    input[type='text'] { flex: 1 1 14rem; min-width: 0; }
    ul { margin: 0; padding: 0; list-style: none; }
    li { padding: var(--space-3) 0; border-top: 1px solid var(--color-border); }
    li:first-child { border-top: 0; padding-top: 0; }
    .name { font-weight: var(--font-weight-medium); }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-left: auto; }
    .muted { margin: var(--space-1) 0 0; color: var(--color-muted); font-size: var(--font-size-sm); }
    .error { color: var(--color-danger); }
  `,
})
export class ViewsBar {
  private readonly filters = inject(FilterStore);
  private readonly api = inject(ViewsApi);

  protected readonly shareDays = SHARE_DAYS;
  protected readonly panel = signal<Panel>('none');
  protected readonly views = signal<readonly SavedView[]>([]);
  protected readonly status = signal<'loading' | 'loaded' | 'error'>('loading');
  protected readonly name = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly renaming = signal<string | null>(null);
  protected readonly sharing = signal<string | null>(null);
  protected readonly days = signal<ShareDays>(7);
  protected readonly created = signal<CreatedShareLink | null>(null);
  protected readonly copied = signal(false);

  /** The saved view the screen shows right now, if any. */
  protected readonly current = computed(() => {
    const filter = this.filters.filter();
    return this.views().find((v) => sameFilter(fromViewSettings(v.settings), filter)) ?? null;
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.status.set('loading');
    this.api.list().subscribe({
      next: (views) => {
        this.views.set(views);
        this.status.set('loaded');
      },
      error: () => this.status.set('error'),
    });
  }

  protected toggle(panel: Panel): void {
    this.error.set(null);
    this.name.set(panel === 'save' ? (this.current()?.name ?? '') : '');
    this.panel.set(this.panel() === panel ? 'none' : panel);
  }

  protected save(event: Event): void {
    event.preventDefault();
    const name = this.name().trim();
    if (!name) {
      this.error.set('Give the view a name.');
      return;
    }
    this.run(this.api.save(name, toViewSettings(this.filters.filter())), (view) => {
      this.views.set(sortByName([...this.views(), view]));
      this.panel.set('none');
    });
  }

  protected open(view: SavedView): void {
    this.filters.replace(fromViewSettings(view.settings));
    this.panel.set('none');
  }

  protected startRename(view: SavedView): void {
    this.sharing.set(null);
    this.name.set(view.name);
    this.renaming.set(this.renaming() === view.id ? null : view.id);
  }

  protected rename(event: Event, view: SavedView): void {
    event.preventDefault();
    const name = this.name().trim();
    if (!name) {
      this.error.set('Give the view a name.');
      return;
    }
    this.run(this.api.save(name, view.settings, view.id), (saved) => {
      this.views.set(sortByName(this.views().map((v) => (v.id === saved.id ? saved : v))));
      this.renaming.set(null);
    });
  }

  protected remove(view: SavedView): void {
    this.run(this.api.delete(view.id), () => this.views.set(this.views().filter((v) => v.id !== view.id)));
  }

  protected startShare(view: SavedView): void {
    this.renaming.set(null);
    this.created.set(null);
    this.copied.set(false);
    this.sharing.set(this.sharing() === view.id ? null : view.id);
  }

  protected share(view: SavedView): void {
    this.run(this.api.share(view.id, this.days()), (link) => this.created.set(link));
  }

  protected fullUrl(link: CreatedShareLink): string {
    return `${location.origin}${link.url}`;
  }

  protected copy(link: CreatedShareLink): void {
    // The field stays selectable as a fallback where the clipboard is not available.
    navigator.clipboard?.writeText(this.fullUrl(link)).then(
      () => this.copied.set(true),
      () => this.copied.set(false),
    );
  }

  /** "Jul 1, 2026 to Sep 30, 2026 · Europe · vs previous period" */
  protected describe(view: SavedView): string {
    const s = view.settings;
    const dates =
      s.preset === 'custom'
        ? `${s.from ? formatDate(s.from) : 'start'} to ${s.to ? formatDate(s.to) : 'today'}`
        : (DATE_PRESETS.find((p) => p.value === s.preset)?.label ?? s.preset);
    const filters = [...s.regions, ...s.channels, ...s.categories];
    const compare = s.compare === 'previous' ? 'vs previous period' : s.compare === 'year' ? 'vs last year' : null;
    return [dates, ...filters, compare].filter((part) => part).join(' · ');
  }

  protected date(value: string): string {
    return formatDate(value.slice(0, 10));
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected daysValue(event: Event): ShareDays {
    return Number((event.target as HTMLSelectElement).value) as ShareDays;
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
        this.error.set(problemMessage(error, 'That did not work. Try again.'));
      },
    });
  }
}

function sortByName(views: SavedView[]): SavedView[] {
  return views.sort((a, b) => a.name.localeCompare(b.name));
}
