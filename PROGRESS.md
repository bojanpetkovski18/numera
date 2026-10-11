# Numera Progress

Update this file at the end of every milestone. Only record results that were actually verified.

Session estimates are planning guesses (2-3 hours per session), not measurements. Replace them with real numbers after the first few milestones.

| # | Milestone | Est. sessions | Status | Verified on | Notes |
|---|---|---|---|---|---|
| M01 | Repo, tooling, API skeleton, CI, Angular shell | 2 | complete locally | 2026-10-11 | API/web build and tests and `docker compose up -d` (PostgreSQL healthy) verified. CI run not yet verified: no git remote configured. |
| M02 | Database schema and seed generator | 2 | complete locally | 2026-10-11 | Migration, 200,000-row seed, unit and Testcontainers tests verified locally. CI run pending. |
| M03 | Authentication (JWT, demo user) | 1 | complete locally | 2026-10-11 | Backend and web tests plus a headless-Edge run of the login flow verified locally. CI run pending. |
| M04 | CSV import: upload and validation | 3 | not started | | |
| M05 | CSV import: error report and import UI | 2 | not started | | |
| M06 | Metrics endpoints (summary, timeseries, breakdown) | 2 | not started | | |
| M07 | Dashboard UI (KPIs, charts, date filter) | 3 | not started | | |
| M08 | Orders grid (API and UI) | 3 | not started | | |
| M09 | Drill-down from charts to grid | 1 | not started | | |
| M10 | CSV export of filtered view | 1 | not started | | |
| M11 | Demo reset, states, responsive polish | 3 | not started | | |
| M12 | E2E test, Docker, deploy, README, screenshots | 4 | not started | | |

Total estimate: about 27 sessions, which is roughly 5-6 weeks at 5 sessions a week. Keep a buffer week.

## Measured results (fill in only when measured)

- API build / test status: `dotnet build` succeeded with 0 warnings; `dotnet test` passed on 2026-10-11 (17 unit tests, 23 integration tests against PostgreSQL 17 via Testcontainers)
- Web build / test status: `npm run build` succeeded; `npm test -- --watch=false` passed on 2026-10-11 (13 tests in 4 files)
- Query time for dashboard on seeded data: _not measured_
- Seed time, 200,000 rows (local Docker PostgreSQL 17, dev machine, single runs on 2026-10-11): generation 522 ms, binary COPY insert 3,274 ms in the service; about 5.6 s wall-clock for `dotnet run -- seed` including process start and migrate check
- Import time for a 100,000-row file: _not measured_

## Known issues

- The GitHub Actions workflow has not run; no git remote is configured yet. Confirm after the first push.
- The login flow was driven in headless Microsoft Edge through Playwright (installed outside the repo). There is no committed browser test yet; the Playwright flow is milestone M12.
- Global `ng` on this machine is v11; use the local CLI via `npm start` / `npm run build` in `web/`.
- `ng serve` blocks on an analytics prompt in non-interactive shells; set `NG_CLI_ANALYTICS=false`.

## Next step

Confirm CI is green on `#0002-authentication`, merge to `dev`, then start M04 (CSV import: upload and validation) on a new `#0003-` branch.
