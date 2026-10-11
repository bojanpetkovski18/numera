import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, catchError, pipe, switchMap, tap } from 'rxjs';
import { problemMessage } from '../../core/http/problem';
import { ImportJob, checkFile } from './import.models';
import { ImportsApi } from './imports-api';

export type UploadPhase = 'idle' | 'uploading' | 'processing' | 'done' | 'failed';
export type HistoryStatus = 'loading' | 'loaded' | 'error';

interface ImportState {
  readonly phase: UploadPhase;
  readonly progress: number;
  readonly fileName: string | null;
  readonly result: ImportJob | null;
  readonly errorMessage: string | null;
  readonly history: readonly ImportJob[];
  readonly historyStatus: HistoryStatus;
  /** Increases after every successfully processed import so other screens can refresh their data. */
  readonly completedImportCount: number;
}

const initialState: ImportState = {
  phase: 'idle',
  progress: 0,
  fileName: null,
  result: null,
  errorMessage: null,
  history: [],
  historyStatus: 'loading',
  completedImportCount: 0,
};

const HISTORY_SIZE = 20;

/** Shared import status: upload progress and result, recent history, and a counter other screens can watch. */
export const ImportStore = signalStore(
  { providedIn: 'root' },
  withState<ImportState>(initialState),
  withMethods((store, api = inject(ImportsApi)) => {
    const loadHistory = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { historyStatus: 'loading' })),
        switchMap(() =>
          api.list(HISTORY_SIZE).pipe(
            tap((history) => patchState(store, { history, historyStatus: 'loaded' })),
            catchError(() => {
              patchState(store, { historyStatus: 'error' });
              return EMPTY;
            }),
          ),
        ),
      ),
    );

    const upload = rxMethod<File>(
      pipe(
        switchMap((file) => {
          const problem = checkFile(file);
          if (problem !== null) {
            patchState(store, {
              phase: 'failed',
              progress: 0,
              fileName: file.name,
              result: null,
              errorMessage: problem,
            });
            return EMPTY;
          }

          patchState(store, {
            phase: 'uploading',
            progress: 0,
            fileName: file.name,
            result: null,
            errorMessage: null,
          });
          return api.upload(file).pipe(
            tap((event) => {
              if (event.kind === 'progress') {
                patchState(store, {
                  progress: event.percent,
                  phase: event.percent >= 100 ? 'processing' : 'uploading',
                });
                return;
              }
              const job = event.job;
              patchState(store, {
                phase: 'done',
                progress: 100,
                result: job,
                history: [job, ...store.history().filter((j) => j.id !== job.id)].slice(0, HISTORY_SIZE),
                historyStatus: 'loaded',
                completedImportCount: store.completedImportCount() + 1,
              });
            }),
            catchError((error: unknown) => {
              patchState(store, {
                phase: 'failed',
                errorMessage: problemMessage(error, 'The import failed. Please try again.'),
              });
              // A rejected file can still have left a failed job behind; refresh the list.
              loadHistory();
              return EMPTY;
            }),
          );
        }),
      ),
    );

    return {
      loadHistory,
      upload,
      /** Clears the current upload result so the drop zone is ready again. */
      reset: () =>
        patchState(store, {
          phase: 'idle',
          progress: 0,
          fileName: null,
          result: null,
          errorMessage: null,
        }),
    };
  }),
);