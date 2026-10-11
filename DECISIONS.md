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

_Add new decisions below as D019, D020, ..._
