# M01: Repo, tooling, API skeleton, CI, Angular shell

## Goal
A clean, buildable skeleton for the whole project that runs locally and passes CI. No business features yet.

## Context
Read `CLAUDE.md` for the layout and rules. Nothing exists yet except the docs.

## Scope
1. Verify tool versions and report them: .NET SDK (must be 10.x), Node, npm, Docker, Git. If .NET 10 is missing, stop and tell me how to install it.
2. Create the solution and projects exactly as in `CLAUDE.md`: `Numera.Api`, `Numera.Application`, `Numera.Infrastructure`, `Numera.UnitTests`, `Numera.IntegrationTests`. Enable nullable reference types and warnings-as-errors.
3. API: a `GET /health` endpoint (anonymous), structured logging, problem-details error handling, CORS configured for the local Angular dev server.
4. Angular app in `web/` with the latest stable Angular CLI: standalone components, strict TypeScript, routing with placeholder pages (Login, Dashboard, Orders, Import, Settings), and an app shell with sidebar navigation and a top bar. Define design tokens (colors, spacing, typography) as CSS variables. Pick a distinct, modern, business-appropriate visual direction; avoid a default framework look.
5. `docker-compose.yml` that starts PostgreSQL for local dev, plus `.env.example` with placeholders only.
6. `.gitignore`, `.editorconfig`.
7. GitHub Actions workflow: restore, build and test the API; install, build and test the web app.
8. Fill in the Commands section of `CLAUDE.md` with the exact commands you verified.

## Out of scope
Database schema, authentication, any real feature, deployment.

## Tests required
- One trivial unit test and one integration test (health endpoint via `WebApplicationFactory`) so the test projects and CI are proven.
- One Angular component test for the app shell.

## Acceptance criteria
- `dotnet build` and `dotnet test` succeed from the repo root.
- `GET /health` returns 200 when the API runs.
- The Angular app builds, its tests pass, and navigating between placeholder pages works in the browser.
- `docker compose up -d` starts PostgreSQL.
- The CI workflow file is valid and runs the same build and test commands (note: confirm the actual CI run once the repo is pushed to GitHub).
- `CLAUDE.md` commands section is filled in with commands you actually ran.

## Verification required
Run every command above and include the real results in your milestone report.
