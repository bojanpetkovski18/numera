import { HttpClient, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, filter, map } from 'rxjs';
import { ImportJob, UploadEvent } from './import.models';

@Injectable({ providedIn: 'root' })
export class ImportsApi {
  private readonly http = inject(HttpClient);

  /** Uploads the file, emitting progress events and finally the job summary. */
  upload(file: File): Observable<UploadEvent> {
    const form = new FormData();
    form.append('file', file, file.name);
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

  list(limit = 20): Observable<ImportJob[]> {
    return this.http.get<ImportJob[]>('/api/imports', { params: { limit } });
  }

  downloadErrorReport(importId: string): Observable<Blob> {
    return this.http.get(`/api/imports/${importId}/errors.csv`, { responseType: 'blob' });
  }
}