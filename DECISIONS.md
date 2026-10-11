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

_Add new decisions below as D009, D010, ..._
