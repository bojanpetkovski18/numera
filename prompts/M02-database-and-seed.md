# M02: Database schema and seed generator

## Goal
A migrated PostgreSQL schema and a deterministic generator for realistic fictional order data.

## Context
Read `docs/SPEC.md` sections 2, 4 and 6. M01 is complete.

## Scope
1. EF Core entities, configurations and an initial migration for: `orders`, `import_jobs`, `import_errors`, `users`.
   - `orders`: id, order_id, order_date, customer_id, region, channel, product_sku, product_category, quantity, unit_price (`numeric(12,2)`), discount (`numeric(4,3)`, 0 to 1), status (completed, refunded, cancelled), import_job_id (nullable).
   - Unique constraint on (order_id, product_sku). Check constraints for quantity > 0, unit_price >= 0, discount between 0 and 1.
   - Indexes on order_date, region, channel, product_category, status. Justify each in `DECISIONS.md`.
   - `import_jobs`: id, file_name, status, total_rows, imported_rows, rejected_rows, created_at, completed_at.
   - `import_errors`: id, import_job_id, row_number, field, message, raw_row.
   - `users`: id, email, password_hash, created_at. (Authentication itself is M03.)
2. A seed generator for "Harbor & Pine Coffee": about 200,000 order lines over 24 months, with believable seasonality, regional and channel mixes, product categories (for example single origin, blends, equipment, subscriptions), a small share of refunds and cancellations, and discounts. It must be **deterministic** (fixed random seed) and use fictional names/IDs only.
3. A way to run the seed from the command line and by calling a service (the demo reset in M11 will reuse it). Seeding must use bulk insertion and be reasonably fast; measure the time and record it in `PROGRESS.md`.
4. A small CLI or startup option to migrate and seed a local database.

## Out of scope
Authentication logic, import pipeline, any API endpoints beyond health.

## Tests required
- Unit tests: the generator is deterministic (same seed gives identical output), produces only valid rows (statuses, ranges, uniqueness).
- Integration test (Testcontainers PostgreSQL): migration applies cleanly, seeding inserts the expected row count, and the unique constraint rejects a duplicate.

## Acceptance criteria
- Migration applies to a fresh database.
- Seeding produces about 200,000 valid rows and the count is verified by query.
- Re-running the seed after a reset yields identical data.
- Constraints and indexes exist (verified by inspecting the database).

## Verification required
Run the migration, run the seed against the Docker PostgreSQL, run the tests, and report real counts and timings.
