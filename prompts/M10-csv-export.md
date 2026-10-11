# M10: CSV export of the filtered view

## Goal
Download exactly what the grid is showing.

## Context
M08 is complete. Export must use the same filter model and sorting as the grid.

## Scope
1. `GET /api/orders/export`: streams a CSV of all rows matching the current filters and sort (not just the current page). Stream from the database; do not buffer the whole result in memory. Set a sensible maximum (for example 100,000 rows) and return a clear error or truncation notice if exceeded; record the decision.
2. CSV details: UTF-8 with BOM for Excel friendliness, header row, ISO dates, plain decimal numbers, proper quoting. Apply the CSV-injection protection from `CLAUDE.md`.
3. File name includes a timestamp and a short description of the filters.
4. Angular: an "Export CSV" button on the Orders screen with a loading state, success message and error handling. Download must carry the auth token (a plain link will not).

## Out of scope
PDF or Excel export.

## Tests required
- Integration: exported rows equal the rows the grid endpoint returns for the same filter and sort across all pages (compare as sets and order); a filter with no matches returns a header-only file; injection-prone cell values are neutralized; the endpoint requires authentication.
- Angular: button triggers the download and shows loading and error states.

## Acceptance criteria
- Export contains exactly the rows the grid is showing.
- A large export streams without excessive memory use; measure and record it.

## Verification required
Download an export from the running app, open it, and compare row count and a few values with the grid. Report what you saw.
