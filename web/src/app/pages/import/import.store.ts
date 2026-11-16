import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, Observable, catchError, pipe, switchMap, tap } from 'rxjs';
import { problemMessage } from '../../core/http/problem';
import { ImportJob, ImportMapping, ImportPreview, MappingChoice, SavedMapping, checkFile } from './import.models';
import { ImportsApi } from './imports-api';

/**
 * idle -> previewing -> (mapping ->) uploading -> processing -> done | failed. A file that already uses Numera's
 * column names skips the mapping step.
 */
export type UploadPhase = 'idle' | 'previewing' | 'mapping' | 'uploading' | 'processing' | 'done' | 'failed';
export type HistoryStatus = 'loading' | 'loaded' | 'error';

interface ImportState {
  readonly phase: UploadPhase;
  readonly progress: number;
  readonly fileName: string | null;
  /** The chosen file while it is previewed and mapped. */
  readonly file: File | null;
  readonly preview: ImportPreview | null;
  /** Why the mapping could not be saved; the mapping step stays open. */
  readonly mappingError: string | null;
  /** True while the mapping is being saved, before the upload starts. */
  readonly savingMapping: boolean;
  readonly result: ImportJob | null;
  readonly errorMessage: string | null;
  readonly history: readonly ImportJob[];
  readonly historyStatus: HistoryStatus;
  readonly savedMappings: readonly SavedMapping[];
  readonly savedMappingsStatus: HistoryStatus;
  /** Increases after every successfully processed import so other screens can refresh their data. */
  readonly completedImportCount: number;
}

const initialState: ImportState = {
  phase: 'idle',
  progress: 0,
  fileName: null,
  file: null,
  preview: null,
  mappingError: null,
  savingMapping: false,
  result: null,
  errorMessage: null,
  history: [],
  historyStatus: 'loading',
  savedMappings: [],
  savedMappingsStatus: 'loading',
  completedImportCount: 0,
};

const HISTORY_SIZE = 20;

/** Shared import status: preview and mapping, upload progress and result, recent history, saved mappings. */
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

    const loadMappings = rxMethod<void>(
      pipe(
        tap(() => patchState(store, { savedMappingsStatus: 'loading' })),
        switchMap(() =>
          api.listMappings().pipe(
            tap((savedMappings) => patchState(store, { savedMappings, savedMappingsStatus: 'loaded' })),
            catchError(() => {
              patchState(store, { savedMappingsStatus: 'error' });
              return EMPTY;
            }),
          ),
        ),
      ),
    );

    /** Uploads and follows the import to its result; every state change happens here. */
    function uploadFlow(file: File, mapping: ImportMapping | null): Observable<unknown> {
      patchState(store, { phase: 'uploading', progress: 0, fileName: file.name, savingMapping: false, result: null, errorMessage: null });
      return api.upload(file, mapping).pipe(
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
            file: null,
            preview: null,
            result: job,
            history: [job, ...store.history().filter((j) => j.id !== job.id)].slice(0, HISTORY_SIZE),
            historyStatus: 'loaded',
            completedImportCount: store.completedImportCount() + 1,
          });
        }),
        catchError((error: unknown) => {
          patchState(store, {
            phase: 'failed',
            file: null,
            preview: null,
            errorMessage: problemMessage(error, 'The import failed. Please try again.'),
          });
          // A rejected file can still have left a failed job behind; refresh the list.
          loadHistory();
          return EMPTY;
        }),
      );
    }

    function rejectLocally(file: File): boolean {
      const problem = checkFile(file);
      if (problem === null) {
        return false;
      }
      patchState(store, { phase: 'failed', progress: 0, fileName: file.name, file: null, preview: null, result: null, errorMessage: problem });
      return true;
    }

    /** Imports a file as it is (Numera's own column names). */
    const upload = rxMethod<File>(pipe(switchMap((file) => (rejectLocally(file) ? EMPTY : uploadFlow(file, null)))));

    /** Looks at a chosen file first: imports it straight away if it is in Numera's format, otherwise asks for a mapping. */
    const choose = rxMethod<File>(
      pipe(
        switchMap((file) => {
          if (rejectLocally(file)) {
            return EMPTY;
          }
          patchState(store, { phase: 'previewing', progress: 0, fileName: file.name, file, preview: null, mappingError: null, result: null, errorMessage: null });
          return api.preview(file).pipe(
            switchMap((preview) => {
              if (preview.matchesNumeraFormat) {
                return uploadFlow(file, null);
              }
              patchState(store, { phase: 'mapping', preview });
              return EMPTY;
            }),
            catchError((error: unknown) => {
              patchState(store, {
                phase: 'failed',
                file: null,
                errorMessage: problemMessage(error, 'The file could not be read. Please try again.'),
              });
              return EMPTY;
            }),
          );
        }),
      ),
    );

    /**
     * Imports the file being mapped. When asked, saves the mapping first (updating the saved mapping the preview
     * came from if the name is the same); if saving fails, nothing is imported and the mapping step says why.
     */
    const confirmMapping = rxMethod<MappingChoice>(
      pipe(
        switchMap(({ mapping, saveAs }) => {
          const file = store.file();
          const preview = store.preview();
          if (file === null || preview === null) {
            return EMPTY;
          }
          if (saveAs === null) {
            return uploadFlow(file, mapping);
          }
          const existing = preview.savedMapping;
          const sameName = existing !== null && existing.name.toLowerCase() === saveAs.toLowerCase();
          patchState(store, { mappingError: null, savingMapping: true });
          return api.saveMapping(saveAs, mapping, preview.headers, sameName ? existing.id : null).pipe(
            switchMap((saved) => {
              patchState(store, { savedMappings: [...store.savedMappings().filter((m) => m.id !== saved.id), saved].sort(byName) });
              return uploadFlow(file, mapping);
            }),
            catchError((error: unknown) => {
              patchState(store, { savingMapping: false, mappingError: problemMessage(error, 'The mapping could not be saved. Try again.') });
              return EMPTY;
            }),
          );
        }),
      ),
    );

    const deleteMapping = rxMethod<string>(
      pipe(
        switchMap((id) =>
          api.deleteMapping(id).pipe(
            tap(() => patchState(store, { savedMappings: store.savedMappings().filter((m) => m.id !== id) })),
            catchError(() => {
              loadMappings();
              return EMPTY;
            }),
          ),
        ),
      ),
    );

    /** Renames a saved mapping; resolves to an error message, or null when it worked. */
    function renameMapping(id: string, name: string): Observable<string | null> {
      const current = store.savedMappings().find((m) => m.id === id);
      if (current === undefined) {
        return EMPTY;
      }
      return api.saveMapping(name, current.mapping, current.sourceHeaders, id).pipe(
        tap((saved) => patchState(store, { savedMappings: store.savedMappings().map((m) => (m.id === id ? saved : m)).sort(byName) })),
        switchMap(() => [null]),
        catchError((error: unknown) => [problemMessage(error, 'The mapping could not be renamed. Try again.')]),
      );
    }

    return {
      loadHistory,
      loadMappings,
      upload,
      choose,
      confirmMapping,
      deleteMapping,
      renameMapping,
      /** Leaves the mapping step without importing. */
      cancelMapping: () => patchState(store, { phase: 'idle', file: null, preview: null, fileName: null, mappingError: null }),
      /** Clears the current upload result so the drop zone is ready again. */
      reset: () =>
        patchState(store, {
          phase: 'idle',
          progress: 0,
          fileName: null,
          file: null,
          preview: null,
          mappingError: null,
          result: null,
          errorMessage: null,
        }),
    };
  }),
);

function byName(a: SavedMapping, b: SavedMapping): number {
  return a.name.localeCompare(b.name);
}
