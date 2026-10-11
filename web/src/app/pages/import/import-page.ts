import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FileSaver } from '../../core/http/file-saver';
import { problemMessage } from '../../core/http/problem';
import { ImportJob } from './import.models';
import { ImportStore } from './import.store';
import { ImportsApi } from './imports-api';

type ResultKind = 'success' | 'partial' | 'nothing-imported';

@Component({
  selector: 'app-import-page',
  imports: [DatePipe, DecimalPipe],
  templateUrl: './import-page.html',
  styleUrl: './import-page.css',
})
export class ImportPage implements OnInit {
  protected readonly store = inject(ImportStore);
  private readonly api = inject(ImportsApi);
  private readonly saver = inject(FileSaver);

  protected readonly dragging = signal(false);
  protected readonly downloadError = signal<string | null>(null);
  protected readonly downloadingId = signal<string | null>(null);

  protected readonly busy = computed(() => {
    const phase = this.store.phase();
    return phase === 'uploading' || phase === 'processing';
  });

  protected readonly resultKind = computed<ResultKind | null>(() => {
    const job = this.store.result();
    if (job === null || this.store.phase() !== 'done') {
      return null;
    }
    if (job.importedRows === 0) {
      return 'nothing-imported';
    }
    return job.rejectedRows > 0 ? 'partial' : 'success';
  });

  ngOnInit(): void {
    this.store.loadHistory();
  }

  protected onFileInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    if (file !== null) {
      this.start(file);
    }
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (!this.busy()) {
      this.dragging.set(true);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.item(0) ?? null;
    if (file !== null) {
      this.start(file);
    }
  }

  protected downloadErrorReport(job: ImportJob): void {
    this.downloadError.set(null);
    this.downloadingId.set(job.id);
    this.api.downloadErrorReport(job.id).subscribe({
      next: (blob) => {
        this.saver.save(blob, `import-${job.id}-errors.csv`);
        this.downloadingId.set(null);
      },
      error: (error: unknown) => {
        this.downloadingId.set(null);
        this.downloadError.set(problemMessage(error, 'The error report could not be downloaded. Try again.'));
      },
    });
  }

  protected retryHistory(): void {
    this.store.loadHistory();
  }

  private start(file: File): void {
    if (this.busy()) {
      return;
    }
    this.downloadError.set(null);
    this.store.upload(file);
  }
}