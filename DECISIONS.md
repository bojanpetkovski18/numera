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

_Add new decisions below as D037, D038, ..._
