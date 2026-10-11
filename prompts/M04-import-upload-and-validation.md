# M04: CSV import: upload and validation

## Goal
A robust backend import pipeline: accept a CSV, validate every row, import valid rows, record rejects.

## Context
Read `docs/SPEC.md` sections 4 and 7. M01 to M03 are complete.

## Scope
1. `POST /api/imports` (multipart upload). Enforce limits: 10 MB and 100,000 rows. Reject with a clear problem-details error if exceeded or if the file is not a CSV.
2. Parse with a proven CSV library, streaming (do not load the whole file into memory). Handle quoted fields, embedded commas, BOM and different line endings.
3. Header check: required columns present (order_id, order_date, customer_id, region, channel, product_sku, product_category, quantity, unit_price, discount, status). Header matching is case-insensitive. Missing required columns reject the whole file with a clear message. Unknown columns are ignored and reported as a warning.
4. Row validation exactly as in the spec: valid date not in the future; quantity integer > 0; unit_price >= 0; discount between 0 and 1; status in completed/refunded/cancelled; duplicate (order_id, product_sku) within the file rejected; also reject rows that already exist in the database (decide the rule, record it in `DECISIONS.md`).
5. A row error never aborts the import. Valid rows are bulk-inserted in batches inside a safe transaction strategy (document your choice); invalid rows are written to `import_errors` with row number, field, message and the raw row.
6. Track the job in `import_jobs` (status: pending, processing, completed, failed; counts and timestamps). Choose synchronous or background processing and justify it in `DECISIONS.md`. If background, `GET /api/imports/{id}` returns progress and the final summary.
7. `GET /api/imports/{id}` returns status and counts.

## Out of scope
The error-report download and all UI (M05).

## Tests required
- Unit tests for each validation rule, including boundaries (quantity 0, discount 1.0 and 1.01, future date, bad status, duplicates, empty file, header-only file, missing column).
- Integration tests (Testcontainers): a valid file imports every row with correct counts; a file with mixed good and bad rows imports the good ones and records exactly the bad ones; oversize and wrong-type uploads are rejected; import is not possible without authentication.
- Provide small fixture CSVs under `tests/Fixtures/`.

## Acceptance criteria
- A valid file imports every row and the counts are correct.
- A file with bad rows imports the good rows and rejects the bad ones; `import_errors` contains exactly the rejects.
- A 100,000-row file imports without excessive memory use. Measure the time and peak memory roughly and record them in `PROGRESS.md`; do not guess.

## Verification required
Run all tests, then import a generated 100,000-row file against the real database and report the actual counts and timing.
