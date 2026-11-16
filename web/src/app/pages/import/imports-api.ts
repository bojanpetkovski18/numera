import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import { ImportJob, ImportMapping, ImportPreview, SavedMapping, UploadEvent } from './import.models';

@Injectable({ providedIn: 'root' })
export class ImportsApi {
  private readonly http = inject(HttpClient);

  /** Uploads the file (with a column mapping when it is not in Numera's format), emitting progress and finally the job. */
  upload(file: File, mapping: ImportMapping | null = null): Observable<UploadEvent> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (mapping !== null) {
      form.append('mapping', JSON.stringify(mapping));
    }
    return this.http
      .post<ImportJob>('/api/imports', form, { reportProgress: true, observe: 'events' })
      .pipe(
        map((event): UploadEvent | null => {
          if (event.type === HttpEventType.UploadProgress) {
            const percent = event.total ? Math.round((100 * event.loaded) / event.total) : 0;
            return { kind: 'progress', percent };
          }
          if (event.type === HttpEventType.Response && event.body !== null) {
            return { kind: 'done', job: event.body };
          }
          return null;
        }),
        filter((event): event is UploadEvent => event !== null),
      );
  }

  /** The file's header and first rows, with a suggested mapping. Uploads the file; the server keeps nothing. */
  preview(file: File): Observable<ImportPreview> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ImportPreview>('/api/imports/preview', form);
  }

  list(limit = 20): Observable<ImportJob[]> {
    return this.http.get<ImportJob[]>('/api/imports', { params: { limit } });
  }

  downloadErrorReport(importId: string): Observable<Blob> {
    return this.http.get(`/api/imports/${importId}/errors.csv`, { responseType: 'blob' });
  }

  listMappings(): Observable<SavedMapping[]> {
    return this.http.get<SavedMapping[]>('/api/import-mappings');
  }

  /** Creates a saved mapping, or updates `id` when given. */
  saveMapping(name: string, mapping: ImportMapping, sourceHeaders: readonly string[], id: string | null = null): Observable<SavedMapping> {
    const body = { name, mapping, sourceHeaders };
    return id === null
      ? this.http.post<SavedMapping>('/api/import-mappings', body)
      : this.http.put<SavedMapping>(`/api/import-mappings/${id}`, body);
  }

  deleteMapping(id: string): Observable<void> {
    return this.http.delete<void>(`/api/import-mappings/${id}`);
  }
}
