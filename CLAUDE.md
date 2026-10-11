# Numera: Rules for AI Coding Agents

Read this file and `docs/SPEC.md` at the start of every session. They are the source of truth. If a request conflicts with them, stop and ask.

## Project in one paragraph

Numera ("Reporting Reimagined") is an independent portfolio project: a web app where a user uploads a sales CSV, sees KPIs and charts, drills into the underlying rows, and exports what they find. All data is fictional ("Harbor & Pine Coffee"). It is built by one developer with AI assistance, and the README must say so.

## Stack

- Backend: ASP.NET Core Web API on **.NET 10 (LTS)**, EF Core, PostgreSQL.
- Frontend: Angular (latest stable; confirm with `ng version`), standalone components, Signals, NgRx only for shared state (filters, import status).
- Tests: xUnit, Testcontainers (real PostgreSQL), Angular component tests, one Playwright end-to-end flow.
- Infra: Docker Compose for local dev, GitHub Actions for CI.

## Target repository layout

```
numera/
  src/
    Numera.Api/              # controllers/endpoints, auth, DI, middleware
    Numera.Application/      # commands and queries (light CQRS), validators, DTOs
    Numera.Infrastructure/   # EF Core DbContext, migrations, seeding, CSV parsing
  tests/
    Numera.UnitTests/
    Numera.IntegrationTests/
  web/                       # Angular app
  e2e/                       # Playwright
  docs/                      # SPEC.md, ARCHITECTURE.md, screenshots
  prompts/                   # milestone prompts
  docker-compose.yml
  PROGRESS.md
  DECISIONS.md
```

Keep this layout unless a change is logged in `DECISIONS.md`. Fill in the exact build, test and run commands in the section below once milestone 1 has established and verified them.

## Commands (verified in M01 unless noted)

- Build API: `dotnet build` (repo root)
- Test API: `dotnet test` (repo root)
- Run API: `dotnet run --project src/Numera.Api --urls http://localhost:5080`
- Build web: `npm run build` (in `web/`)
- Test web: `npm test -- --watch=false` (in `web/`)
- Run web: `npm start` (in `web/`, serves on http://localhost:4200)
- Start PostgreSQL: `docker compose up -d` (copy `.env.example` to `.env` first; not yet verified because the Docker daemon was not running in M01)
- Run everything: start PostgreSQL, then run the API and the web app in two terminals

## Working rules

1. **One milestone at a time.** Implement only the milestone you were given. Do not start the next one, and do not add features that are not in the milestone.
2. **Small, verifiable steps.** Prefer several small commits over one large one. Each commit should build and pass its tests.
3. **Inspect before changing.** Read the existing files first. Preserve existing work. Do not delete or rewrite files outside the milestone's scope without asking.
4. **Never claim what you did not verify.** Do not say code compiles, tests pass, an endpoint works, or the app runs unless you actually ran it in this session and saw the result. Paste the real command output summary in your report. If you could not run something, say so plainly.
5. **Tests are part of the feature.** Every milestone ships with the tests listed in its prompt. A failing test is fixed or reported, never deleted or skipped to get green.
6. **Ask when blocked.** If a requirement is ambiguous in a way that changes the design, ask one clear question. Otherwise choose the simplest reasonable option, state the assumption, and log it in `DECISIONS.md`.
7. **No scope creep, no premature abstraction.** Use the simplest design that satisfies the acceptance criteria. Light CQRS means separate command and query handlers, not a framework of interfaces.
8. **Update `PROGRESS.md`** at the end of each milestone: status, what was verified, known issues, next step.

## Code standards

- C#: nullable reference types on, warnings treated as errors in CI, async all the way down, no business logic in controllers.
- Aggregations (KPIs, charts) must run **in SQL** through EF Core or raw SQL. Never load all orders into memory to sum them.
- Validation: server-side is authoritative. Client-side validation is a convenience only.
- Angular: standalone components, Signals for local state, strict TypeScript, no `any` unless justified in a comment, accessible labels and keyboard navigation on all interactive elements.
- Every screen has loading, empty, error and success states.
- Errors return RFC 7807 problem details from the API. Log with structured logging. Never log secrets or full request bodies.
- Money is stored as `numeric(12,2)`, never floating point.

## Security rules

- No secrets, passwords or API keys in the repository. Use environment variables and an `.env.example` with placeholder values only.
- JWT signing key comes from configuration. The demo user's credentials are documented demo values, not real ones.
- All endpoints except login and health require authentication.
- Validate file uploads: size limit (10 MB), row limit (100,000), content type, and treat file contents as untrusted. Protect against CSV/formula injection in exports by prefixing cells that start with `=`, `+`, `-` or `@`.
- Use parameterized queries only.

## Data and legal rules

- Fictional data only. No employer code, business rules, screenshots or confidential information, ever.
- Do not state performance numbers, test counts or coverage figures anywhere (README, docs) unless they were measured and recorded in `PROGRESS.md`.

## Milestone report format

End every milestone with a report in exactly this shape:

```
## Milestone report: <name>
Status: complete | partial | blocked
Changed: <files/areas>
Verified (commands actually run and results):
- <command>: <result>
Not verified / could not run: <list or "none">
Acceptance criteria: <each criterion, pass/fail, how checked>
Assumptions and decisions logged: <list>
Known issues: <list or "none">
Suggested next step: <one line>
```

## Commit conventions

Short imperative subject lines, for example `Add CSV row validation`. One logical change per commit.
