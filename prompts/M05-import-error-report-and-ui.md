# M05: CSV import: error report and import UI

## Goal
Let a user upload a file in the browser, see the result, and download the rejected rows.

## Context
M04 is complete. Read `docs/SPEC.md` section 5 (Import screen).

## Scope
1. `GET /api/imports/{id}/errors.csv`: returns a CSV of the rejected rows with row number, field, message and the original values. It must match `import_errors` exactly. Apply the CSV-injection protection from `CLAUDE.md`. Return a clear 404 for unknown jobs.
2. Angular Import screen:
   - Drag-and-drop and file-picker upload with client-side pre-checks (file type, size).
   - Upload progress and processing status (poll the job if processing is asynchronous).
   - Result summary: imported, rejected and warning counts, with success, partial-success and failure states.
   - A "Download error report" button when there are rejects.
   - An import history list (recent jobs with status and counts).
   - Clear error messages from the API problem-details responses.
3. A downloadable sample CSV template link so users know the expected format.
4. Import status held in shared state (NgRx or SignalStore) so other screens can react to a completed import.

## Out of scope
Dashboard and grid features.

## Tests required
- Integration: the error CSV for a mixed file matches the rejects exactly (row numbers and messages); fields starting with `=`, `+`, `-` or `@` are neutralized.
- Angular component tests: upload success, partial success with error report button, API failure message, oversize file blocked on the client.

## Acceptance criteria
- Uploading a file with bad rows shows correct imported and rejected counts, and the downloaded error report matches the rejects exactly.
- All four states (loading, empty history, error, success) are visible and correct.
- The screen is usable at 375 px width.

## Verification required
Exercise the screen in a real browser with a good file, a mixed file and a broken file, and report what you saw. Run all tests.
