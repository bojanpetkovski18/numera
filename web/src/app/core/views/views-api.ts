import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Granularity } from '../format/format';
import { CompareMode, DatePreset, FilterState } from '../filters/filter-state';

/** A saved view's settings, as the API stores them. Presets stay relative ("last 90 days" moves with the date). */
export interface ViewSettings {
  readonly preset: DatePreset;
  readonly from: string | null;
  readonly to: string | null;
  readonly regions: readonly string[];
  readonly channels: readonly string[];
  readonly categories: readonly string[];
  readonly granularity: Granularity;
  readonly compare: CompareMode;
}

export interface SavedView {
  readonly id: string;
  readonly name: string;
  readonly settings: ViewSettings;
  readonly updatedAt: string;
}

export interface ShareLink {
  readonly id: string;
  readonly viewId: string;
  readonly viewName: string;
  readonly createdAt: string;
  readonly expiresAt: string;
}

/** A new link. The token (in `url`) is only ever returned here: the server keeps a hash. */
export interface CreatedShareLink {
  readonly link: ShareLink;
  readonly token: string;
  readonly url: string;
}

export const SHARE_DAYS = [1, 7, 30] as const;
export type ShareDays = (typeof SHARE_DAYS)[number];

/** The dashboard's filter as view settings: dates only for a custom range. */
export function toViewSettings(state: FilterState): ViewSettings {
  const custom = state.preset === 'custom';
  return {
    preset: state.preset,
    from: custom ? state.from : null,
    to: custom ? state.to : null,
    regions: [...state.regions],
    channels: [...state.channels],
    categories: [...state.categories],
    granularity: state.granularity,
    compare: state.compare,
  };
}

export function fromViewSettings(settings: ViewSettings): FilterState {
  const custom = settings.preset === 'custom';
  return {
    preset: settings.preset,
    from: custom ? settings.from : null,
    to: custom ? settings.to : null,
    regions: [...settings.regions],
    channels: [...settings.channels],
    categories: [...settings.categories],
    granularity: settings.granularity,
    compare: settings.compare,
  };
}

@Injectable({ providedIn: 'root' })
export class ViewsApi {
  private readonly http = inject(HttpClient);

  list(): Observable<SavedView[]> {
    return this.http.get<SavedView[]>('/api/views');
  }

  /** Creates a view, or updates `id` when given (also used to rename). */
  save(name: string, settings: ViewSettings, id: string | null = null): Observable<SavedView> {
    const body = { name, settings };
    return id === null ? this.http.post<SavedView>('/api/views', body) : this.http.put<SavedView>(`/api/views/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/views/${id}`);
  }

  share(viewId: string, expiresInDays: ShareDays): Observable<CreatedShareLink> {
    return this.http.post<CreatedShareLink>(`/api/views/${viewId}/shares`, { expiresInDays });
  }

  listShares(): Observable<ShareLink[]> {
    return this.http.get<ShareLink[]>('/api/shares');
  }

  revoke(linkId: string): Observable<void> {
    return this.http.delete<void>(`/api/shares/${linkId}`);
  }
}
