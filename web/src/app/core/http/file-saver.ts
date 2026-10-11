import { Injectable } from '@angular/core';

/** Saves a Blob as a file download. A service so tests can replace the browser-only behaviour. */
@Injectable({ providedIn: 'root' })
export class FileSaver {
  save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }
}