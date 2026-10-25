# Numera Decisions Log

One entry per meaningful decision. Format: context, decision, alternatives, trade-offs. The agent adds entries as it works; review them yourself, because you should be able to explain every one in an interview.

## D001: Target .NET 10 (LTS)

- **Context:** Microsoft has announced that .NET 8 and .NET 9 reach end of support on November 10, 2026. .NET 10 is an LTS release supported through November 2028.
- **Decision:** Build on .NET 10.
- **Alternatives:** .NET 8 (support ends within weeks), .NET 9 (same end date).
- **Trade-offs:** Newer tooling requirements (check the SDK and IDE versions), fewer third-party samples.

## D002: PostgreSQL as the database

- **Context:** Free hosting tiers for Postgres exist (for example Neon and Supabase); a free SQL Server host is harder to find.
- **Decision:** PostgreSQL with EF Core (Npgsql).
- **Trade-offs:** Slightly different SQL features from SQL Server; fine for this scope. Free-tier limits (storage, compute) must be checked when deploying.

## D003: Light CQRS, no mediator framework

- **Context:** The app has a few write operations (import, reset) and many read queries (aggregates, grid).
- **Decision:** Separate command and query handlers in `Numera.Application`, called directly from endpoints. Reads use SQL aggregation; no loading rows into memory.
- **Alternatives:** Full CQRS with separate read database; a mediator library.
- **Trade-offs:** Simple and explainable, but fewer cross-cutting pipeline features.

## D004: NgRx only where state is shared

- **Context:** Filters and import status are shared across screens; most other state is local.
- **Decision:** NgRx (or SignalStore) for shared filters and import status; Signals for component-local state.
- **Trade-offs:** Less boilerplate than using NgRx everywhere.

## D005: Shared build props, warnings as errors everywhere

- **Context:** CLAUDE.md requires nullable reference types and warnings treated as errors in CI.
- **Decision:** A root `Directory.Build.props` sets `Nullable`, `ImplicitUsings` and `TreatWarningsAsErrors` for all projects, so local builds and CI behave identically.
- **Trade-offs:** New analyzer warnings after an SDK upgrade break the build until fixed; accepted as the point of the rule.

## D006: Angular 22 zoneless with the Vitest runner; `.slnx` solution

- **Context:** `ng new` with the latest CLI (22.x) generates a zoneless app and uses Vitest via `@angular/build:unit-test`. `dotnet new sln` on .NET 10 generates `Numera.slnx`.
- **Decision:** Keep both generator defaults rather than switching to Karma or the classic `.sln` format.
- **Trade-offs:** `.slnx` needs a recent SDK/IDE (fine with the .NET 10 requirement in D001).

## D007: Login lives outside the app shell; placeholder pages share one component

- **Context:** M01 needs routing with placeholder pages and a shell with sidebar and top bar.
- **Decision:** `/login` renders without the shell; the other four pages are children of the `Shell` route. All placeholders use one `PlaceholderPage` driven by route data, to be replaced page by page in later milestones.
- **Trade-offs:** Slightly unusual to have one component behind five routes, but it avoids five throwaway files.

## D008: Structured logging via built-in JSON console logger

- **Context:** M01 requires structured logging; no logging package is needed yet.
- **Decision:** `builder.Logging.AddJsonConsole()` with message templates. Revisit (for example Serilog) only if a later milestone needs sinks or enrichment.

## D009: Schema conventions, entity placement and index rationale

- **Context:** M02 defines the schema for `orders`, `import_jobs`, `import_errors`, `users`.
- **Decision:**
  - Entities are plain classes in `Numera.Application/Entities`; the DbContext, configurations and migrations live in `Numera.Infrastructure`. Column names are snake_case via a loop in `OnModelCreating` (no naming-convention package).
  - `orders.id` is a `bigint` identity; `import_jobs.id` and `users.id` are `uuid`. `orders.status` is stored as lowercase text (`completed`, `refunded`, `cancelled`) with a check constraint, matching the CSV vocabulary. `order_date` is a `date`.
  - `import_jobs.status` values (`Pending`, `Processing`, `Completed`, `Failed`) are an assumption; M04 may refine them. `import_errors.field` is nullable for row-level errors.
  - Deleting an import job sets `orders.import_job_id` to null and cascades to its errors.
- **Index rationale:**
  - `ux_orders_order_id_product_sku` (unique): enforces the duplicate rule; its leading column also serves order_id search.
  - `ix_orders_order_date`: date-range filter and time series, the most selective filter.
  - `ix_orders_region`, `ix_orders_channel`, `ix_orders_product_category`, `ix_orders_status`: grid filters and breakdown group-bys. These are low cardinality, so on their own they help little for broad filters; they are required by the spec and will be re-evaluated against measured query plans in M06/M08.
  - `ix_orders_import_job_id` and `ix_import_errors_import_job_id`: foreign-key lookups (error report download, job cleanup).
- **Trade-offs:** More indexes slow bulk inserts and use space; accepted for a read-heavy reporting app.

## D010: Deterministic seed generator and binary COPY seeding

- **Context:** The demo needs about 200,000 realistic rows, reproducible across machines and resets.
- **Decision:** `DemoDataGenerator` is pure: a seeded `System.Random` and a fixed `EndDate` (2026-09-30, not the clock) so output never changes and is never in the future. `DemoSeeder` truncates `import_errors`, `import_jobs`, `orders` (keeps `users`) and bulk-loads with Npgsql binary `COPY` in one transaction.
- **Alternatives:** EF `AddRange` + `SaveChanges` (much slower), `EFCore.BulkExtensions` (extra dependency).
- **Trade-offs:** The window is fixed, so the demo data ages as time passes; the end date can be moved deliberately via `SeedOptions`.

## D011: Maintenance commands as positional API arguments

- **Context:** M02 asks for a small CLI/startup option to migrate and seed.
- **Decision:** `dotnet run --project src/Numera.Api -- migrate` and `-- seed` (migrate, then reset and seed) run against `ConnectionStrings__Numera` and exit. Positional words are used because `--flag` style arguments are consumed by the .NET configuration provider.
- **Trade-offs:** Reuses the API's DI container instead of a separate console project.

## D012: Pin EF Core to one version

- **Context:** `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 pulled EF Core 10.0.4 while `Microsoft.EntityFrameworkCore.Design` was 10.0.12, producing assembly-conflict warnings (errors under warnings-as-errors).
- **Decision:** Reference `Microsoft.EntityFrameworkCore` and `.Relational` 10.0.12 explicitly in Infrastructure.
- **Trade-offs:** Package versions are bumped manually together.

## D013: Token storage in sessionStorage

- **Context:** The Angular app needs to keep a JWT across a page refresh until it expires (M03 acceptance criteria).
- **Decision:** Store the access token (and its expiry) in `sessionStorage`. The interceptor only attaches it to `/api/` requests. The token lifetime is 60 minutes (configurable), and there is no refresh-token flow.
- **Alternatives:**
  - `localStorage`: survives tab close and is shared across tabs, but the token stays on a shared machine longer.
  - httpOnly, SameSite cookie: not readable by scripts, so safer against XSS, but needs CSRF handling and credentialed CORS, and complicates a deploy where the SPA and API are on different hosts.
  - In memory only: safest, but a refresh logs the user out, which fails the acceptance criteria.
- **Trade-offs:** Any script that runs in the page (XSS) can read the token. Mitigations: Angular's template escaping, a short lifetime, no third-party scripts, and a later Content-Security-Policy. Revisit cookies if the app ever holds real data.

## D014: Handlers live in Application and use an `INumeraDbContext` interface

- **Context:** M02 left open how Application handlers reach the database, because Infrastructure references Application. M03's login handler is the first one.
- **Decision:** `Numera.Application` declares `INumeraDbContext` (the four `DbSet`s and `SaveChangesAsync`) and references `Microsoft.EntityFrameworkCore` (no provider). `NumeraDbContext` implements it. Handlers use LINQ so aggregation still runs in SQL.
- **Alternatives:** Put handlers in Infrastructure (breaks D003), or a repository layer (premature abstraction).
- **Trade-offs:** Application depends on the EF Core package, so queries are not storage-agnostic. That is accepted; this app will not swap ORMs.

## D015: Authentication policy, login behaviour and `GET /api/auth/me`

- **Decision:**
  - A fallback authorization policy requires an authenticated user for every endpoint. Only `/health` and `/api/auth/login` opt out with `AllowAnonymous`, so a new endpoint is protected by default.
  - Unknown routes also answer 401 to anonymous callers (404 once signed in), so the API surface cannot be probed without a token.
  - Wrong email and wrong password return the same 401, and an unknown email still performs a hash verification so timing does not reveal which accounts exist. Emails are trimmed and compared case-insensitively.
  - `GET /api/auth/me` is an addition beyond the API draft: it gives the integration tests a protected endpoint to assert 401/200 against, and lets a client confirm a token is still accepted.
  - Passwords use ASP.NET Core's `PasswordHasher<User>`. The demo credentials (`demo@numera.example` / `Demo-Pass-2026!`) are public demo values shown on the login page.
- **Trade-offs:** The demo user is created idempotently by the `migrate` and `seed` commands; an existing user is never modified.

## D016: Login rate limiting keyed by client IP

- **Decision:** Fixed window, 10 attempts per 60 seconds per remote IP, configurable under `RateLimiting`. Rejections return 429 problem details with `Retry-After`.
- **Trade-offs:** Behind a reverse proxy every client shares the proxy's IP unless forwarded headers are configured, so this must be revisited for deployment in M12. Limits are per instance (in memory).

## D017: Angular dev proxy instead of cross-origin calls

- **Decision:** The SPA calls relative `/api/...` URLs. `ng serve` proxies `/api` and `/health` to `http://localhost:5080` (`web/proxy.conf.json`), so no CORS is needed in development and the same relative URLs work behind a reverse proxy in production. The API's CORS policy remains for direct calls.

## D018: Write text files without a BOM

- **Context:** Windows PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM, which crept into many files in M01 to M03 (including JSON).
- **Decision:** BOMs were removed in one commit. Files are written without a BOM (`.editorconfig` already says `charset = utf-8`).

## D019: Imports are processed synchronously

- **Context:** M04 asks to choose synchronous or background processing.
- **Decision:** `POST /api/imports` does the whole import inside the request and returns the final job summary (201). `GET /api/imports/{id}` returns the same summary later. The `pending` status is reserved; a job is `processing` while the request runs.
- **Why:** The limits (10 MB, 100,000 rows) bound the work. A measured 100,000-row import takes a few seconds (see `PROGRESS.md`), which fits a request, and there is no queue, worker or polling to build and test.
- **Trade-offs:** A client that disconnects mid-import cancels it (the transaction rolls back). A reverse proxy needs a request timeout comfortably above the import time. If the limits grow, move to a background worker (Channel + hosted service) and keep the same `GET` endpoint for progress.

## D020: One transaction per import, staging table, `ON CONFLICT DO NOTHING`

- **Context:** "A row error never aborts the import" but an infrastructure failure must not leave half a file.
- **Decision:**
  - The job row is created and committed first (so a failure is always recorded).
  - The file is scanned once for header and row-count problems without touching the data tables.
  - Valid rows, error rows and the job's final counts are then written in a single transaction: all-or-nothing for the data, while bad rows are recorded, never fatal.
  - Valid rows are streamed in batches of 5,000 through a temporary staging table (binary `COPY`) and moved into `orders` with `INSERT ... ON CONFLICT (order_id, product_sku) DO NOTHING RETURNING`. Rows that did not come back already existed. **Superseded by D028** (single COPY, anti-join, advisory lock).
- **Alternatives:** Per-batch transactions (partial imports on failure), pre-checking existing keys with a `SELECT` (race-prone), EF `AddRange` (slow).
- **Trade-offs:** One long transaction for up to 100,000 rows (a few seconds). Race safety now comes from serializing imports (D028).

## D021: Rows that already exist are rejected, not updated

- **Decision:** A row whose `(order_id, product_sku)` already exists in `orders` is rejected with "already exist" and recorded in `import_errors`; the existing row is kept. Within one file, the first valid occurrence wins and later ones are rejected with "first seen on row N".
- **Why:** The import is an append operation; silently overwriting history would make dashboards change without notice. Re-uploading the same file is therefore safe (every row is rejected, nothing is duplicated).
- **Trade-offs:** Correcting an existing order requires a future "update" feature (not in the MVP).

## D022: Validation details beyond the spec text

- Dates must be `yyyy-MM-dd`; other formats are rejected because `03/04/2025` is ambiguous. "Not in the future" means not later than today in the earliest time zone, UTC+14 (D030; originally today's UTC date).
- `status` is matched case-insensitively and stored lowercase.
- `unit_price` allows at most 2 decimals and `discount` at most 3, matching `numeric(12,2)` and `numeric(4,3)`; values are never silently rounded. Quantity must be a whole number (`2.0` is rejected).
- Text fields are trimmed, required, and limited to their column lengths.
- Every failing field of a row gets its own `import_errors` row, so `import_errors` can have more rows than there are rejected rows; `rejected_rows` counts rows, and the number of distinct `row_number` values equals it.
- `row_number` is the 1-based record number with the header as row 1 (the number you see in a spreadsheet). A wrong column count or malformed quoting rejects that row only.
- Raw rows are stored truncated to 2,000 characters.

## D023: File-level rejection and extra `import_jobs` columns

- **Decision:** A file that cannot be processed at all (empty, header only, missing or repeated required columns, more than 100,000 rows) produces a `failed` job with a `failure_reason` and an HTTP problem response: 422 for content problems, 413 for too many rows or more than 10 MB, 415 for wrong file type or binary content, 400 for no file. The 413/415/400 cases for size and type are detected before a job is created.
- Unknown columns are not an error: they are ignored and listed in `import_jobs.warnings`.
- **Schema:** migration `AddImportJobDetails` adds `warnings text[]` and `failure_reason varchar(500)` to `import_jobs` (an addition to the original schema, so the reason is queryable later, for example by the M05 UI). `import_jobs.status` is stored lowercase.
- **Security:** Antiforgery is disabled on the upload endpoint because the API uses bearer tokens, not cookies. Uploads are streamed to a temporary file by the framework above 64 KB, the type is checked by extension, content type and a NUL-byte sniff, and CSV content is only ever passed to parameterised/binary COPY, never interpolated into SQL.

## D024: CsvHelper's low-level parser and handler placement

- **Decision:** `CsvHelper` is used through `CsvParser` (not the object mapper), with a header index built by `HeaderEvaluation`, so the pure rules in `Numera.Application` are unit-testable without a file or database. The streaming reader, scanner and import handler live in `Numera.Infrastructure` because they depend on CsvHelper and PostgreSQL `COPY`; this is an exception to D003/D014 for I/O-bound handlers.
- **Trade-offs:** The file is read twice (scan, then load). Parsing 100,000 rows is cheap compared with rolling back a database transaction for a file that was over the limit anyway.

## D025: Error report format and the import history endpoint

- **Context:** M05 needs a downloadable report and a recent-imports list.
- **Decision:**
  - `GET /api/imports/{id}/errors.csv` streams one line per `import_errors` row, in row order: `row_number,field,message,raw_row`. `raw_row` is the original row as a single quoted cell (the stored value), so the report matches the table exactly. It is UTF-8 with a BOM so Excel reads it correctly, with CRLF line endings.
  - An import with no rejects returns just the header row; an unknown id returns a 404 problem.
  - `GET /api/imports?limit=N` (default 20, maximum 100) lists recent jobs, newest first. This is an addition to the API draft, needed by the history panel.
- **Alternatives:** Splitting `raw_row` back into the 11 original columns (needs the uploaded header, which is not stored).
- **Trade-offs:** The original values are in one cell, not in separate columns.

## D026: CSV injection guard is shared and prefixes with an apostrophe

- **Decision:** `CsvCell` (Application) quotes cells per RFC 4180 and prefixes any text cell that starts with `=`, `+`, `-`, `@`, tab or carriage return with `'`, which spreadsheet programs display as plain text. It is applied to every text cell of the error report and is the helper the M10 export will reuse. The stored data is never altered; only the exported file is.
- **Trade-offs:** A legitimate value such as `-5` in a text cell appears as `'-5` in the file. Numeric cells (row numbers) are written as plain numbers.

## D027: SignalStore for shared import state, authenticated downloads through the HTTP client

- **Decision:**
  - `@ngrx/signals` `ImportStore` (root-provided) holds the upload phase and progress, the last result, recent history and a `completedImportCount` that other screens (dashboard, grid) can watch to refresh after an import. Component-local UI state (drag-over, download in progress) stays in component signals.
  - The error report is fetched through `HttpClient` as a blob and saved with a small `FileSaver` service, because a plain link cannot send the bearer token. `FileSaver` is a service so tests can replace the browser-only code.
  - Client-side checks (extension, empty, over 10 MB) are a convenience; the server stays authoritative.
  - API problem details (`detail`, then `title`) are shown to the user as written.
- **Trade-offs:** The whole report is held in the browser as a blob before saving, which is fine for at most 100,000 error lines of 2,000 characters.

## D028: Import pipeline revised: one COPY, set-based duplicate check, serialized imports

- **Context:** Phase timing showed database work was about 4.9 s of a 5.5 s, 100,000-row import. Measured in PostgreSQL on 100,000 staged rows (rolled back): `INSERT ... ON CONFLICT DO NOTHING` 2.34 s (with or without `RETURNING`), plain `INSERT` 0.88 s, anti-join `DELETE ... USING orders` 0.08 s.
- **Decision:**
  - All valid rows (with their raw text) stream into the temporary staging table through one binary `COPY`; no batches are held in application memory.
  - One CTE moves staging rows whose key already exists into `import_errors`.
  - A plain `INSERT ... SELECT` moves the rest into `orders`.
  - `pg_advisory_xact_lock` serializes imports so no other transaction can add the same keys in between; a test runs three concurrent uploads of the same file.
  - The handler checks that inserted plus already-existing rows equals the staged count.
- **Result (Release build, local Docker PostgreSQL, 3 runs each):** 5.8–6.3 s became 4.4–4.5 s per 100,000-row import; peak working set 171–181 MB became 167–170 MB. Synchronous `COPY` field writes were also tried and made no measurable difference, so the async API stays.
- **Trade-offs:** Concurrent imports wait for each other instead of running in parallel (fine for a single-user demo; each takes a few seconds).

## D029: Request hardening

- **Decision:**
  - Request bodies are limited to 1 MB by default (Kestrel); only `POST /api/imports` raises its limit to 10 MB plus 64 KB for the multipart envelope, via `RequestSizeLimitAttribute` metadata. Verified on real Kestrel: a 2 MB login body gets 413, a 9 MB CSV imports, an 11 MB upload gets a 413 problem.
  - Malformed multipart bodies return 400 problem details. Previously they surfaced as a 500, because the framework throws an `IOException`. Kestrel's 413 exception is also an `IOException`, so it is caught first.
  - Once the upload has been received, the import runs with the application-stopping token instead of the request token, so a client that disconnects no longer rolls back a running import.
  - Every response carries `X-Content-Type-Options: nosniff`.
  - Optional `ForwardedHeaders:Enabled` (default off) makes the login rate limit use the client address from `X-Forwarded-For`, trusting only the nearest proxy (`ForwardLimit = 1`). Enable it only when the API is reachable solely through that proxy (deployment, M12).
  - EF Core's per-command SQL logging is set to Warning so normal operation does not log every statement.

## D030: "Not in the future" means today in the earliest time zone

- **Context:** Comparing `order_date` with the server's UTC date rejected orders dated "today" for users ahead of UTC for up to 14 hours.
- **Decision:** The latest accepted date is today's date in UTC+14 (`OrderRowValidator.LatestAllowedDate`), so an order dated today anywhere in the world is accepted; a date that is in the future everywhere is still rejected.
- **Trade-offs:** For a few hours a day, a date that is "tomorrow" for a UTC user is accepted.

## D031: Developer tooling fixes

- `dotnet-ef` is pinned to 10.0.12 in a repo-local tool manifest (`dotnet-tools.json`; run `dotnet tool restore`), replacing a stale global 10.0.7.
- `angular.json` sets `cli.analytics: false`, so `ng serve` no longer blocks on a prompt in non-interactive shells.
- The API's launch profile uses port 5080, matching the Angular dev proxy, so `dotnet run` and `npm start` work together without extra flags (previously the profile used 5131).
- `.gitattributes` normalizes text files to LF (matching `.editorconfig`) and keeps CSV fixtures byte-exact.

## D032: Metrics are hand-written PostgreSQL behind a shared filter

- **Context:** M06 needs summary, timeseries (with empty periods filled) and breakdown aggregates, all in SQL.
- **Decision:**
  - The KPI definitions, filter model, validation and period arithmetic live in `Numera.Application/Metrics` (pure, unit-tested). The SQL lives in `Numera.Infrastructure/Metrics`, following D024: it uses PostgreSQL features EF Core LINQ does not express cleanly (`FILTER`, `COUNT(DISTINCT ...)` per group, `generate_series` for empty periods, `date_trunc` on `date`).
  - `OrderFilterSql` builds the WHERE clause; every value is a parameter (`= ANY(@array)` for lists). Column names and the date unit come from fixed switches, never from input.
  - Revenue and orders count completed lines only, so metrics endpoints reject a `status` filter rather than silently ignoring it. Filters accept repeated values ("any of"), which costs little now and serves the grid's filters later.
  - Rounding happens once, in C#, on exact sums (`MetricDefinitions`).
  - Timeseries without dates spans the matching data. A request is capped at 1,000 points.
  - `date_trunc` is applied to `timestamp`, not `timestamptz`, so the database session time zone cannot move dates between buckets.
- **Alternatives:** EF Core LINQ `GroupBy` (no clean translation for filtered distinct counts and empty periods), a reporting view or materialized view (premature at this data size).

## D033: Query plans and index findings on the 200,000-row seed

Measured with `EXPLAIN (ANALYZE)` on the local Docker PostgreSQL 17 after `ANALYZE orders`:
- **Date filters** use `ix_orders_order_date`: one month runs in about 8 ms, one week plus a region in under 1 ms.
- **Region-only filters** use a bitmap scan on `ix_orders_region` (about 18 ms for Latin America, about 9% of rows). The low-cardinality indexes (region, channel, category) help for selective values only; broad filters fall back to scans, as expected.
- **Whole-table summary:** about 86 ms. The planner walks `ux_orders_order_id_product_sku` so that `COUNT(DISTINCT order_id)` needs no sort.
- **Per-group distinct counts:** `COUNT(DISTINCT order_id)` per group sorted. Aggregating per (group, order) first and then per group measured 182 ms to 133 ms (median of 5) for a one-year category breakdown, with identical results, so the breakdown uses that form. The same rewrite was slower for the summary (86 to 134 ms) and the day series (141 to 177 ms), so those keep `COUNT(DISTINCT ...)`.
- `ix_orders_status` was not used by any metrics query; it stays for the grid (M08) and will be re-checked there.
- No index changes in M06.

## D034: Chart.js 4, used directly

- **Context:** M07 needs a mature charting library with readable, accessible, responsive charts.
- **Decision:** Chart.js 4.5 through a thin `ChartCanvas` component, registering only the controllers, elements and scales the dashboard uses.
- **Why:** Mature and widely used; canvas rendering stays fast with 730 daily points; tree-shakable; responsive out of the box. Using it directly avoids an Angular wrapper library whose releases lag behind Angular majors (this project is on Angular 22).
- **Accessibility:** Canvas charts are opaque to screen readers, so every chart has a `role="img"` text summary (range, totals, peak) and a "Show data table" disclosure with the exact values. Each chart has a single series, so nothing is encoded by color alone; colors come from the design tokens (primary on white passes contrast); animations are disabled under `prefers-reduced-motion`.
- **Alternatives:** ECharts (more chart types, much larger bundle), ApexCharts (SVG, heavier with many points), ngx-charts (D3/SVG; ties to Angular versions).
- **Trade-offs:** The library adds about 196 kB (59 kB transferred) to the lazy dashboard chunk. Feature pages are lazy-loaded so the initial bundle stays at about 345 kB, under the 500 kB budget (it was 555 kB before lazy loading).

## D035: Filter state in a SignalStore, mirrored in the URL

- **Decision:** `FilterStore` (`@ngrx/signals`) holds the date preset or custom range, region, channel, category and the chart granularity.
  - Pure functions (`filter-state.ts`) map between the store, the URL query string and API parameters, and are unit-tested for round-trips.
  - The dashboard page syncs both ways: URL to store on load, refresh and back/forward; store to URL with `replaceUrl`, so tweaking filters does not flood browser history.
  - Defaults are omitted from the URL, so the plain dashboard URL stays clean.
  - Malformed URL values fall back to defaults instead of erroring, because shared URLs get edited by hand.
- **Presets** (last 30 days, last 90 days, year to date) are stored as presets, not dates, so a shared "last 30 days" link means the last 30 days whenever it is opened. They resolve against the browser's local date. "All time" sends no dates; a custom range is stored as explicit dates.
- **Widgets** use Angular's `httpResource` (stable in Angular 22), which gives each widget loading, error and reload for free and refetches when the filter signal changes.
- **Filter bar:** single-select dropdowns per dimension for now. The API and store already accept several values, for the grid in M08.

## D036: Filter options endpoint, presentation choices

- `GET /api/filter-options` is an addition to the API draft. It returns the distinct regions, channels and categories in the data plus the first and last order date, for the filter dropdowns and the "showing all data" range text. The grid in M08 will reuse it.
- Amounts are shown in US dollars (the fictional company's currency is not specified). Revenue KPIs above $1,000 drop cents; tables and tooltips keep cents.
- The revenue chart's subtitle shows the period count and the best period, not a total. Summing per-period values that were each rounded to the cent drifts from the Revenue KPI by a few cents (seen in the browser: $10,376,613.91 by week, $10,376,615.58 by day, versus $10,376,613.65), which reads as an error.
- Dark mode was skipped: the tokens would allow it, but the charts would also need a second color set. It is optional in the spec.

## D037: Orders API: exact count, OFFSET paging, whitelisted sort

- **Endpoint:** `GET /api/orders` takes the dashboard filters plus `status` (repeatable), `q`, `sort`, `dir`, `page` and `pageSize`. Like the metrics, it is hand-written, parameterized PostgreSQL in Infrastructure; the page and the count are two statements sent in one round trip.
- **Sort:** only the twelve grid columns are accepted, through a fixed map from name to column (never interpolated from input). An unknown name gives 400. `id` is always the tie-breaker, so pages never overlap or skip rows on equal values. With no `sort`, the order is newest first. `sort` without `dir` is ascending.
- **Page size:** 25 by default; 100 at most. A larger size is rejected with 400 rather than clamped, so a client never silently gets fewer rows than it asked for. A page past the end returns an empty list with the real total.
- **Exact count on every request.** Measured with `EXPLAIN (ANALYZE)` on the 200,000-row seed: 12.9 ms unfiltered, 3.8 ms for a region, 0.9 ms for a month. That is cheap enough to give the user exact "1–25 of N" text and a last-page button. An estimate or a capped count would only pay off at much larger tables.
- **OFFSET paging, not keyset.** Page 1 by date takes 0.4 ms; page 4,000 takes 42 ms; the last page takes 93 ms. Keyset paging would make deep pages cheaper but makes "jump to page N" and "last page" awkward for every sort column. Deep pages are rare in a grid that is filtered first, so OFFSET wins on simplicity at this size.
- **Line total** is computed with the same `MetricDefinitions` rounding as revenue. With `status=completed`, the grid's total equals the summary's `completedLines` (checked by an integration test and in the browser).
- `ix_orders_status` (see D033) is used by the grid's status filter: an index-only count of 0.4 ms for refunded and 6 ms for completed.

## D038: Order ID search is a case-insensitive "contains" backed by a trigram index

- `q` matches anywhere in the order ID (`ILIKE '%term%'`). `%`, `_` and `\` in the input are escaped, so they match literally. The term is capped at 40 characters. People remember fragments like "0123" more often than full IDs, so a prefix match would miss them.
- A leading wildcard cannot use a B-tree index: the contains query took 20–24 ms as a scan. A `pg_trgm` GIN index (`ix_orders_order_id_trgm`, about 4 MB, migration `AddOrderIdSearchIndex`) brings terms of three or more characters down to 0.3–0.5 ms. Shorter terms still scan (about 24 ms), which is acceptable.
- Search is limited to order IDs, as the spec says. Customer and SKU searches are out of scope.

## D039: Grid state in the URL, with one URL writer per page

- **Grid state:** status, search, sort, direction, page and page size live in the URL next to the shared filters, defaults omitted. A reload or a shared link restores the exact view. The state is local to the orders page (a signal, not NgRx), because nothing else reads it.
- **One writer:** `syncFiltersWithUrl` is now the only code that writes the URL. Pages pass their own extra parameters to it. With two writers, two merge navigations in the same tick each started from the old URL and dropped the other's changes. The dashboard and grid share it, and the grid drops the dashboard-only `granularity` from its URL.
- **Filters carry over:** arriving at a page with no filter parameters keeps the store's filters. Moving from the dashboard to the grid therefore keeps the region, channel, category and date range.
- **Page reset:** a filter, status, search, sort or page-size change returns to page 1. For shared filters, this happens synchronously in a computed signal, so a stale page number is never requested.
- **Search** is debounced by 300 ms. The search box is not part of the URL until the debounce fires.
- **Loading:** the first load shows skeleton rows. Later loads keep the previous page dimmed under a progress bar, so the table does not jump.

## D040: Grid layout: sticky header on wide screens, cards on phones

- Twelve columns do not fit a phone. Under 700 px, each row becomes a labelled card (`data-label` on each cell), and sorting moves to a "Sort by" select. The table header stays in the accessibility tree, visually hidden, so screen readers still get table semantics.
- On wider screens the table scrolls inside its card with a sticky header. At 1280 px with the sidebar open, the last columns scroll horizontally inside the table region; the region is focusable so keyboard users can scroll it. The page itself never scrolls sideways (checked at 1280, 768 and 375 px).
- Sortable headers are buttons inside `th` elements that carry `aria-sort`, so screen readers announce the current sort and keyboard users can sort with Enter or Space.

## D041: What a drill-down shows

- **Status:** a drill-down adds `status=completed`, so the grid shows the completed lines that make up the clicked revenue. The bars and points show completed revenue, so these are "exactly the matching rows" (spec). Without the status, refunded and cancelled lines of the same segment would appear and the row count would not match anything on the dashboard. The status shows as a removable chip. When a drill-down has no completed lines, the empty state says so and offers "Show all statuses".
- **Count that must match:** the number of grid rows equals the summary's `completedLines` for the drilled filter. The distinct order ids among those rows equal the clicked value's `orders`, and the revenue recomputed from the rows equals its `revenue`. `DrillDownConsistencyTests` checks all three for every point and bar of several charts, including partial first and last periods.
- **Breakdown bar:** the clicked dimension is set to the bar's key; the date range and the other dimensions are kept. If that dimension already had several values, it narrows to the one clicked.
- **Time point:** the date range becomes the period, Monday to Sunday for weeks and the 1st to the last day for months, intersected with the active range. The API cuts periods at calendar boundaries, so the first and last points can cover part of a period only; the intersection is exactly what the point was computed from. Presets become explicit dates, because the drilled view is a specific period.
- **Navigation:** a normal (pushed) navigation to `/orders` with the filter in the query string, so the link is shareable and the browser Back button returns to the dashboard.
- The rule lives in one pure, unit-tested module (`drill-down.ts`); the integration test mirrors it.

## D042: Chart interaction: click, keyboard and table links

- A click anywhere in a bar's row, or nearest a point on the time axis, opens that item. Thin bars and the 730-point day chart are still easy targets. The cursor turns into a pointer over items, and a line under each chart says what a click does.
- **Keyboard:** each selectable chart canvas is focusable. The arrow keys, Home and End move between items and show the item's tooltip; a polite live region announces it ("Europe: $1,040,832.40, 3,956 orders. Press Enter to open."). Enter or Space opens it; Escape clears the highlight.
- **Screen readers:** the "Show data table" rows are now links to the same drill-downs, a standard path that does not depend on the canvas.
- Chart.js stays behind `ChartCanvas`. The widgets pass one description per item and receive the selected index; they do not handle canvas events themselves.

## D043: Back/forward and scroll behavior

- Back/forward now restores the page's own filters even when its URL has none (`Router.currentNavigation().trigger === 'popstate'`). Before, the "keep the filters from the other page" rule (D039) meant Back from a drill-down showed the default dashboard with the drilled filters still applied.
- Router scroll restoration is enabled, so a new page opens at the top: a drill-down from the bottom of the dashboard on a phone used to open the grid half-way down its cards, hiding the filter chips. The URL-sync navigation passes `scroll: 'manual'` (Angular 22), so changing a filter, sort or page keeps the scroll position.
- Known limit: Back to the dashboard lands at the top rather than the previous position, because the charts are still loading (the page is shorter) when the router restores it.

## D044: Export is the grid query, streamed from one snapshot

- **Same query:** `GET /api/orders/export` takes the grid's parameters (filters, `status`, `q`, `sort`, `dir`; paging is ignored). The grid and the export build their SQL with one shared helper (`OrdersSql`), so they cannot drift. An integration test compares the export with the grid read page by page, cell by cell and in order, for several filter and sort combinations.
- **One snapshot:** the export opens a read-only REPEATABLE READ transaction, counts, then streams the rows in the same transaction. The size check and the file therefore always describe the same data, even while an import runs.
- **Streaming:** each row goes from the Npgsql reader through a 64 KB `StreamWriter` into the response body; the result is never collected. Measured on the seed: 95,924 rows (9.1 MB) in 180–265 ms, with the API's memory up by 0–10 MB. With the client throttled to 1 MB/s (9.1 s), memory rose by about 1 MB, so the server holds no more than its buffers while a slow client downloads.
- **Limit:** at most 100,000 rows, the same as the import. More is refused with a 422 problem (`rowCount`, `maxRows`, a message saying to narrow the filters) before any CSV is sent, not truncated: a truncated file would silently hold less than the grid, which breaks "exactly what the grid shows". The grid knows its total, so the button is disabled above the limit with the reason shown. The unfiltered seed (200,000 rows) is above the limit by design.
- `Cache-Control: no-store`, because the file is the user's data.

## D045: Export file format and name

- **Columns:** the import columns in the same order, then `line_total`. An export can be imported again: the importer ignores `line_total` with a warning. ISO dates; plain invariant decimals (`18.50`, discount `0.1`, no currency or thousands separators); lowercase status; RFC 4180 quoting; CRLF line ends; UTF-8 with a BOM so Excel shows non-ASCII text correctly.
- **CSV injection:** every text cell goes through `CsvCell`, which prefixes `=`, `+`, `-`, `@`, tab and CR with an apostrophe. Numbers are formatted by the server and never start with those characters.
- **Name:** `numera-orders_{yyyyMMdd-HHmmss}Z_{filters}.csv`, for example `numera-orders_20261011-234750Z_europe_wholesale_refunded.csv`. UTC, because a server-local time would be ambiguous. The filter part is reduced to lowercase letters, digits and dashes and capped at 80 characters, so it is safe in a header and on any file system. With no filter it is `all`.

## D046: Downloading with the token

- A plain link cannot send the bearer token, so the web app fetches the file with `HttpClient` (`responseType: 'blob'`) and saves it through `FileSaver`, as the import error report does. The browser holds the file in memory once (about 9 MB at the limit), which is acceptable at this size. A one-time signed download URL would stream to disk, but needs token issuing and expiry for little gain here.
- The file name comes from `Content-Disposition` (path separators removed) and the row count from `X-Row-Count`. The CORS policy is set to expose both headers for a separately hosted front end; this is not exercised yet, because locally the app is served from the same origin through the dev proxy (check in M12).
- **States:** "Exporting…" with a spinner and `aria-busy` while the request runs; a status message with the row count and file name when done; an alert with the server's problem detail on failure. Messages hide once the filters change, so they never describe another view.

## D047: Demo reset

- `POST /api/demo/reset` reuses the `seed` command's `DemoSeeder`: one transaction that clears the order data and bulk-loads the same deterministic 200,000 rows. Order ids restart at 1, so a reset gives back exactly the seeded state. An integration test compares the dashboard summary and an md5 fingerprint of every row before and after an import plus reset.
- **Concurrency:** imports and the reset share one transaction-scoped advisory lock (`DataLocks.OrderWrites`, previously the import's own lock, D028).
  - The reset uses `pg_try_advisory_xact_lock` and answers 409 "Demo data is busy" at once, rather than queueing behind a long import.
  - Imports keep waiting for the lock, so an import that arrives during a reset runs right after it, against the fresh data.
  - The `seed` command waits too.
- **Import history:** a running import registers its job row before it takes the lock (it scans the file first). The reset therefore deletes only finished import jobs. Truncating every job would make that import fail when it inserts its orders.
- **Rate limit:** 3 resets per client IP per 10 minutes, configurable through `RateLimiting:ResetPermitLimit` and `RateLimiting:ResetWindowSeconds`. A reset is several seconds of database work, and the demo is public. The 429 problem names the policy ("Too many demo resets") and gives a `Retry-After`.
- Like imports, it runs on the application-stopping token, so a closed browser tab does not roll back a reset half-way.
- Any signed-in user may reset: everyone shares the one demo account, and the confirmation dialog says the change affects everyone.

## D048: Settings and confirmation dialog

- The confirmation uses the native `<dialog>` with `showModal()`, which provides focus containment, Escape to close and an inert background without a library.
  - Focus starts on Cancel, the safe choice for a destructive action, and returns to the "Reset demo data" button on close.
  - Only an explicit "Reset data" choice resets; Escape and Cancel do not.
- While the reset runs, the button is `aria-disabled` rather than `disabled`, so keyboard focus is not dropped. A status message reports progress and then the result; a failure is an alert with the server's reason (busy, rate limited, offline).
- After a reset, the Import screen's last upload result is cleared, because it no longer describes the data.

## D049: App-wide error handling and the not-found page

- Unexpected errors reach a custom `ErrorHandler`: logged to the console for developers (the app's only intentional console call) and shown as a dismissible banner with "Reload page". Failed lazy page loads (connection lost, new version deployed) also go there, through the router's navigation error handler, with a message that explains it.
- Expected failures stay with each screen's own error states (D035, M05, M08, M10); the banner is the last resort.
- Unknown addresses show a "Page not found" page with a link to the dashboard, instead of silently redirecting. It sits outside the signed-in shell, so it also works before signing in.

## D050: Accessibility and responsive pass

- **Checked:** axe-core 4.10 (WCAG 2.0, 2.1 and 2.2 A/AA plus best practices) on login, dashboard, orders (data and empty), import, settings (with the dialog open) and the not-found page, signed in and out, at 375, 768 and 1280 px. A headless-Edge walk-through checked keyboard flow and scrolling at the same widths.
- **Fixed:**
  - duplicate landmark name on the orders table;
  - "Sign out" not at the right edge at tablet width;
  - disabled `.btn` buttons that looked enabled;
  - small targets (data-table toggles, back link, import format toggle and sample link) raised to at least 24 px tall;
  - links now use the theme colour;
  - "Retry" and "Try again" unified to "Try again".
- **Added:**
  - a "Skip to main content" link;
  - focus moves to the main content when the page changes, including after signing in (screen readers announce the new page);
  - Escape closes the phone menu and returns focus to the menu button.
- **Left as is:** data-table links are 19 px tall, but their rows are 28 px apart, which meets WCAG 2.5.8's spacing exception. The import file input is visually hidden by design; its visible label is the target.
- **Not checked by tools:** a screen-reader run with NVDA or VoiceOver.

## D051: One production image; the API serves the web app

- The root `Dockerfile` builds the Angular app (Node 24), publishes the API (.NET 10 SDK) and copies the app into the API's `wwwroot`. The runtime image is ASP.NET Core only and runs as the image's non-root `app` user.
- The API serves the app when `wwwroot/index.html` exists. Built files are anonymous, hashed files are cached for a year, and `index.html` is revalidated. App paths fall back to `index.html`; unknown `/api/...` paths stay a JSON 404.
- **Why one image:** one free service to host and one cold start, same origin (no CORS, no cross-site cookie questions), and Compose, CI and the hosted demo all run exactly the same artifact.
- A first version had a separate nginx image for the app. It worked, but it meant two images to keep in step and two services to wake on a free host, so it was dropped.
- `Database__PrepareOnStartup=true` applies migrations, ensures the demo user and loads the demo data into an empty database on start. Existing data is never replaced.
  - Measured with the image limited to 0.1 CPU and 512 MB (Render's free size) on an empty database: healthy after 32 s, including the 200,000-row seed; peak memory 126 MiB.
  - On a brand-new database, EF Core logs one error while it checks for the migrations history table before creating it. This is harmless and expected.
- The container's connection string sets `Gss Encryption Mode=Disable`: otherwise Npgsql looks for a Kerberos library the slim image does not have, and logs an error on every start.
- In Compose, the separate dev database command is now `docker compose up -d postgres`, because `docker compose up` starts the whole stack.

## D052: Hosting: Render free web service and Neon free PostgreSQL

Checked 2026-10-12 against provider docs and recent third-party summaries:

| Option | Free tier at the time of checking | Verdict |
|---|---|---|
| Render web service | 512 MB, 0.1 CPU, Docker supported; sleeps after 15 min idle, about 1 min to wake; 750 instance hours a month | chosen for the app |
| Render PostgreSQL | expires 30 days after creation | not used |
| Neon PostgreSQL | 0.5 GB storage per project, 100 compute hours a month, sleeps after 5 min idle, does not expire | chosen for the database (the demo database measured 63 MB) |
| Google Cloud Run | generous monthly free allowance, fast cold starts, but needs a billing account | second choice |
| Fly.io, Koyeb | no dependable free tier for new accounts in 2026 | not used |

- **Cost:** $0 a month.
- **Limits, stated honestly in the README:**
  - the first request after 15 idle minutes waits about a minute for the service to wake;
  - Neon adds a short delay after 5 idle minutes;
  - at 0.1 CPU a demo reset took 10.4 s (measured locally with the same limits), against 4.6 s on a full CPU.
- **Memory:** idle 126 MiB, peak 182 MiB during a reset, under the 512 MB limit.
- **Secrets:** Render generates the JWT signing key (`generateValue`); the Neon connection string is pasted into Render's environment settings (`sync: false`). Nothing secret is in `render.yaml`.
- **Proxy:** forwarded headers are on in the hosted demo (Render's proxy), so the rate limits see the real client address. In Compose there is no proxy, so they stay off.
- The demo deploys from `dev`, the repository's default branch. (Changed in D055: it now deploys from `main`.)

## D053: Content-Security-Policy and critical CSS

- Every response carries a strict CSP: same-origin scripts only, no inline scripts. Styles allow `'unsafe-inline'`, because Angular adds component styles as `<style>` elements.
- Angular's production build inlines critical CSS and loads the full stylesheet with an inline `onload` handler, which the CSP blocks: the global styles never applied in the production image. The development server has no CSP, so this only showed up in the production screenshots. Critical-CSS inlining is now off (`optimization.styles.inlineCritical: false`), so the stylesheet is a plain `<link>`. Allowing inline scripts would have weakened the policy for a small first-paint gain.
- The end-to-end test now fails on any browser console error, so a CSP violation fails CI.

## D054: End-to-end test

- One Playwright test (`e2e/`) covers the spec's main flow: sign in, import a CSV with good and bad rows, check that the dashboard counts the new orders, drill down to exactly those rows, export them and check the file content, then reset the demo.
  - The imported rows use a region name unique to the run, so the drill-down and export checks are exact whatever else is in the data.
  - Ending with the reset leaves a shared demo as it was found.
- It runs against the production image started with Compose, in CI as well, so the Dockerfile, the static-file hosting and the CSP are covered too.
- Locally it can use an installed Edge (`E2E_BROWSER_CHANNEL=msedge`); CI installs Playwright's Chromium. Playwright is pinned to 1.63.0, the newest release older than two weeks at the time.

## D055: Releases on main; the demo deploys from main

- `dev` stays the default branch, where milestone and fix branches are merged through pull requests. `main` holds releases only: `dev` is merged into `main` for each release, and the release is tagged (`v1.0.0` is the first).
- The live demo now follows `main` (`render.yaml`, replacing the `dev` branch named in D052), so it changes only when a release is made, not with every merge into `dev`.
- Versions follow semantic versioning: patch for fixes and docs, minor for new features, major for breaking changes to the API or the data.

_Add new decisions below as D056, D057, ..._
