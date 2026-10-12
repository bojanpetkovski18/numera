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
| M06 | Metrics endpoints (summary, timeseries, breakdown) | 2 | complete locally | 2026-10-11 | Definitions documented, endpoints, unit and Testcontainers tests against hand-computed data, EXPLAIN findings and HTTP timings on the seed. CI run pending. |
| M07 | Dashboard UI (KPIs, charts, date filter) | 3 | complete locally | 2026-10-11 | Dashboard, filter bar with URL sync, component and unit tests, and a headless-Edge run at 1280, 768 and 375 px with no console errors. CI run pending. |
| M08 | Orders grid (API and UI) | 3 | complete locally | 2026-10-11 | Orders API with paging, whitelisted sort, filters and trigram-indexed search; grid UI with URL state; unit, Testcontainers and component tests; query plans and HTTP timings on the seed; a headless-Edge run at 1280, 768 and 375 px with no console errors. CI run pending. |
| M09 | Drill-down from charts to grid | 1 | complete locally | 2026-10-11 | Click, keyboard and data-table drill-downs from all three charts; filter chips and a back link on the grid; back/forward and scroll fixes; unit, component and Testcontainers consistency tests; a headless-Edge run of every drill path, numbers compared with the dashboard, at 1280 and 375 px with no console errors. CI run pending. |
| M10 | CSV export of filtered view | 1 | complete locally | 2026-10-12 | Streamed export from one snapshot with a 100,000-row limit; Export CSV button with progress, result and error states; unit, Testcontainers (export = grid, cell by cell) and component tests; browser download compared with the grid; memory and time measured on the seed. CI run pending. |
| M11 | Demo reset, states, responsive polish | 3 | complete locally | 2026-10-12 | Demo reset API and Settings flow; error banner and not-found page; states and wording audit; axe check (no violations after fixes) and keyboard/scroll walk-through at 375, 768 and 1280 px; reset restores the original dashboard numbers in the browser. CI run pending. |
| M12 | E2E test, Docker, deploy, README, screenshots | 4 | not started | | |

Total estimate: about 27 sessions, which is roughly 5-6 weeks at 5 sessions a week. Keep a buffer week.

## Measured results (fill in only when measured)

- API build / test status: `dotnet build` succeeded with 0 warnings; `dotnet test` passed on 2026-10-11 (172 unit tests, 182 integration tests against PostgreSQL 17 via Testcontainers)
- Web build / test status: `npm run build` succeeded (initial bundle 358.43 kB raw / 98.12 kB transferred; settings chunk 4.66 kB / 1.71 kB, lazy); `npm test -- --watch=false` passed on 2026-10-12 (135 tests in 21 files)
- Metrics endpoint time on the 200,000-row seed (2026-10-11; Release build, API and Docker PostgreSQL 17 on the same Windows 11 dev machine; warm; 20 HTTP calls each with `curl.exe`, time to full response; median / p90):
  - summary, all data: 75.8 / 85.7 ms; one month: 10.0 / 11.4 ms; one region: 15.2 / 16.8 ms
  - timeseries, all data by month (24 points): 127.1 / 137.4 ms; by day (730 points): 126.3 / 132.4 ms; one year by week: 72.0 / 76.9 ms
  - breakdown by category, all data: 180.4 / 187.4 ms; by region, one year: 106.6 / 109.8 ms
- Orders endpoint time on the 200,000-row seed (2026-10-11; Debug build via `dotnet run`, API and Docker PostgreSQL 17 on the same Windows 11 dev machine, AMD Ryzen 7 8840HS, 14 GB RAM; warm; 15 calls each with PowerShell `Invoke-WebRequest`, time to full response; median / max):
  - page 1, default sort (25 rows): 24.4 / 27.6 ms; one region: 21.2 / 24.0 ms; last 90 days + refunded: 20.2 / 21.9 ms; 100 rows: 37.9 / 45.5 ms
  - sorted by line total: 58.5 / 62.7 ms; page 4,000: 57.9 / 63.1 ms; last page (8,000): 92.3 / 106.9 ms
  - order ID search "1195": 13.6 / 16.1 ms
- Orders query plans (`EXPLAIN (ANALYZE)`, same setup, 2026-10-11): count all 12.9 ms, one region 3.8 ms, one month 0.9 ms; page 1 by date 0.4 ms, page 4,000 42.4 ms, last page 92.6 ms; ID contains-search 20–24 ms without an index, 0.3–0.5 ms with the trigram index for terms of 3+ characters (see D037, D038)
- CSV export on the 200,000-row seed (2026-10-12; Release build, API and Docker PostgreSQL 17 on the same dev machine, AMD Ryzen 7 8840HS, 14 GB RAM; `curl.exe`; API process memory sampled every 20 ms, or 50 ms for the throttled run):
  - Europe + Asia-Pacific, 95,924 rows, 9.10 MB: 265 ms (first call), then 238 / 179 / 197 ms. API working set rose by 0 to 8 MB and private bytes by 0 to 10 MB during a download.
  - The same export with the client limited to 1 MB/s took 9.1 s; API working set and private bytes each rose by about 1 MB (136 samples), and the file was byte-identical to the fast run. The server streams rather than buffering the file.
  - Above the limit (104,076 rows): 422 in 53 ms, no file sent.
- Demo reset (2026-10-12; Debug build via `dotnet run`, same dev machine and Docker PostgreSQL 17): 4.6 s from clicking "Reset data" to the result message in headless Edge. During one reset through `curl.exe`, the API working set rose from 189 to a peak of 234 MB (+45 MB) and private bytes from 139 to 183 MB, sampled every 50 ms; the memory was not given back within 3 s (garbage collector). Relevant to the host's memory limit in M12.
- Accessibility check (2026-10-12): axe-core 4.10, WCAG 2.0/2.1/2.2 A and AA plus best practices, 8 screen states (signed in and out) at 375, 768 and 1280 px. Before the fixes: 1 moderate issue (duplicate landmark name on Orders). After: no violations. No sideways page scroll at any width. See D050.
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
- Not checked with a real screen reader (NVDA or VoiceOver); only automated checks and keyboard walk-throughs.
- Browser Back to the dashboard lands at the top of the page, not the previous scroll position: the charts are still loading when the router restores it (D043).

## Next step

Confirm CI is green on `#0011-demo-reset-polish`, merge to `dev`, then start M12 (end-to-end test, Docker, deployment, README, portfolio) on a new `#0012-` branch.
