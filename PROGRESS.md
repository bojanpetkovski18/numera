# Numera Progress

Update this file at the end of every milestone. Only record results that were actually verified.

Session estimates are planning guesses (2-3 hours per session), not measurements. Replace them with real numbers after the first few milestones.

| # | Milestone | Est. sessions | Status | Verified on | Notes |
|---|---|---|---|---|---|
| M01 | Repo, tooling, API skeleton, CI, Angular shell | 2 | complete locally | 2026-10-11 | API/web build and tests and `docker compose up -d` (PostgreSQL healthy) verified. CI run not yet verified: no git remote configured. |
| M02 | Database schema and seed generator | 2 | complete locally | 2026-10-11 | Migration, 200,000-row seed, unit and Testcontainers tests verified locally. CI run pending. |
| M03 | Authentication (JWT, demo user) | 1 | complete locally | 2026-10-11 | Backend and web tests plus a headless-Edge run of the login flow verified locally. CI run pending. |
| M04 | CSV import: upload and validation | 3 | complete locally | 2026-10-11 | Validation, import pipeline, endpoints, unit and Testcontainers tests verified; 100,000-row run measured. CI run pending. |
| M05 | CSV import: error report and import UI | 2 | complete locally | 2026-10-11 | Error report, history endpoint, Import screen, unit/integration/component tests and a headless-Edge run verified. CI run pending. |
| P01 | Polish pass after M05: known issues, bugs, import performance | - | complete locally | 2026-10-11 | Not a spec milestone (requested by the project owner). Unit, integration and web tests, real-Kestrel limit checks, headless-Edge checks and a before/after import benchmark verified. CI run pending. |
| M06 | Metrics endpoints (summary, timeseries, breakdown) | 2 | not started | | |
| M07 | Dashboard UI (KPIs, charts, date filter) | 3 | not started | | |
| M08 | Orders grid (API and UI) | 3 | not started | | |
| M09 | Drill-down from charts to grid | 1 | not started | | |
| M10 | CSV export of filtered view | 1 | not started | | |
| M11 | Demo reset, states, responsive polish | 3 | not started | | |
| M12 | E2E test, Docker, deploy, README, screenshots | 4 | not started | | |

Total estimate: about 27 sessions, which is roughly 5-6 weeks at 5 sessions a week. Keep a buffer week.

## Measured results (fill in only when measured)

- API build / test status: `dotnet build` succeeded with 0 warnings; `dotnet test` passed on 2026-10-11 (113 unit tests, 57 integration tests against PostgreSQL 17 via Testcontainers)
- Web build / test status: `npm run build` succeeded; `npm test -- --watch=false` passed on 2026-10-11 (34 tests in 7 files)
- Query time for dashboard on seeded data: _not measured_
- Seed time, 200,000 rows (local Docker PostgreSQL 17, dev machine, single runs on 2026-10-11): generation 522 ms, binary COPY insert 3,274 ms in the service; about 5.6 s wall-clock for `dotnet run -- seed` including process start and migrate check
- Import of a 100,000-row, 9.08 MB file via `POST /api/imports` (local dev machine, Docker PostgreSQL 17 with 200,000 existing orders, single runs on 2026-10-11): 6.4 s wall-clock for 100,000 rows imported (job duration 6.26 s); 4.6 s for the same file again with all 100,000 rows rejected as existing and 100,000 error rows written. API process working set went from 103 MB after startup to a peak of 180 MB (first upload) and 197 MB (second). Measured with `curl.exe` and the process peak working set, so rough.
- Import of the same 100,000-row file after the P01 pipeline change (Release build, fresh API process per run, database re-seeded to 200,000 orders before each run, 3 runs each on 2026-10-11): before 5.81 / 5.99 / 6.29 s, after 4.39 to 4.51 s wall-clock; API peak working set before 171 to 181 MB, after 167 to 170 MB. Server-side log for one run: 4,190 ms total, of which scan 289 ms and final database steps 2,152 ms.

## Known issues

- GitHub Actions: the owner confirmed green runs for the M01 to M05 pull requests; each new branch still needs its own run.
- Browser checks so far used throwaway Playwright scripts outside the repo (headless Edge). There is no committed browser test yet; the Playwright flow is milestone M12.
- No refresh token: the session ends when the 60-minute access token expires. The client guard checks token expiry only; a forged token is rejected by the API and the interceptor returns the user to login.
- A 100,000-row file fits the 10 MB limit only if rows average about 104 bytes or less; the Import page now says so.
- Forwarded headers are off by default; enable `ForwardedHeaders:Enabled` only when deploying behind a trusted proxy (M12).
- Global `ng` on this machine is v11; use the local CLI via `npm start` / `npm run build` in `web/`.

## Next step

Confirm CI is green on `#0005-polish-and-known-issues`, merge to `dev`, then start M06 (metrics endpoints) on a new `#0006-` branch.
