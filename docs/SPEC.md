# Numera: Reporting Reimagined

Project 1 of 3. Independent portfolio project, built with AI assistance (disclosed in the README). All data is fictional.

## 1. Purpose

Small and mid-sized teams keep their sales and operations data in spreadsheets. Numera lets them upload a CSV, see clean KPIs and charts, drill into the underlying rows, and export what they find, without a BI tool or an analyst.

**Ideal client:** an operations or sales manager at a small business, or a founder who needs a custom reporting tool.
**What it proves:** Angular + .NET full-stack delivery, SQL aggregation performance, a robust import pipeline, tests, Docker, CI and deployment.

## 2. Demo dataset (fictional)

"Harbor & Pine Coffee": an invented online coffee roaster. Seed about 200,000 order lines over 24 months with columns: order_id, order_date, customer_id, region, channel, product_sku, product_category, quantity, unit_price, discount, status.

## 3. MVP scope

**In:**
1. Sign in with a seeded demo user (JWT).
2. CSV import: upload, column mapping check, row validation, a summary (imported / rejected), and a downloadable error report.
3. Dashboard: KPI cards (revenue, orders, average order value, refund rate), revenue over time, breakdown by category and by region, and a date-range filter.
4. Data grid: server-side paging, sorting and filtering (region, channel, category, status, text search on order_id).
5. Drill-down: clicking a chart segment opens the grid pre-filtered to those rows.
6. CSV export of the current filtered view.
7. Loading, empty, error and success states on every screen.
8. Responsive layout (desktop, tablet, mobile).
9. Demo mode: one-click reset of seeded data.

**Out of MVP:** saved filter views, comparison periods, scheduled email reports, PDF export, multi-user roles. Version 2 takes some of these on: see section 12.

## 4. Validation rules (import)

- Required columns present; unknown columns ignored with a warning.
- order_date is a valid date and not in the future.
- quantity is an integer greater than 0; unit_price is at least 0; discount is between 0 and 1.
- status is one of: completed, refunded, cancelled.
- Duplicate order_id and product_sku combinations within a file are rejected.
- A row error never aborts the import; valid rows are imported and invalid rows go to the error report.
- File limit: 10 MB and 100,000 rows (documented and enforced).

## 5. Screens

1. **Login** (demo credentials shown on the page).
2. **Dashboard** (KPIs, charts, date filter).
3. **Orders** (grid with filters, export).
4. **Import** (upload, progress, result summary, error report download).
5. **Settings/Demo** (reset data).

## 6. Architecture

- **Frontend:** Angular (latest stable), standalone components, Signals, NgRx where state is shared (filters, import status), a charting library, Tailwind or a custom theme.
- **Backend:** ASP.NET Core on .NET 10, Web API, EF Core with PostgreSQL.
- **Light CQRS:** commands for import and reset; queries for aggregates, using SQL-level aggregation (no loading rows into memory).
- **Database:** `orders` (indexed on order_date, region, channel, product_category), `import_jobs` (status, counts, timestamps), `import_errors` (job_id, row_number, field, message), `users`.
- **Infrastructure:** Docker Compose for local dev; GitHub Actions for build and test.

## 7. API (draft)

| Method | Path | Purpose |
|---|---|---|
| POST | /api/auth/login | Issue a token |
| POST | /api/imports | Upload a CSV |
| GET | /api/imports/{id} | Import status and counts |
| GET | /api/imports/{id}/errors.csv | Download rejected rows |
| GET | /api/metrics/summary | KPI values for filters |
| GET | /api/metrics/timeseries | Revenue by day/week/month |
| GET | /api/metrics/breakdown | By category or region |
| GET | /api/orders | Paged, sorted, filtered rows |
| GET | /api/orders/export | CSV of the filtered view |
| POST | /api/demo/reset | Reseed data |

## 8. Acceptance criteria (summary)

- Uploading a valid file imports every row and shows correct counts.
- Uploading a file with bad rows imports the good rows, rejects the bad ones, and the error report matches the rejects exactly.
- Dashboard KPIs match values calculated independently from the same data (checked by an automated test).
- Changing a filter updates the KPIs, charts and grid consistently.
- Drill-down from a chart segment shows only matching rows.
- Export contains exactly the rows the grid is showing.
- Unauthenticated requests to protected endpoints return 401.
- The app is usable at 375 px width.
- Performance claims (for example, query time on 200,000 rows) are only published after being measured and recorded.

## 9. Testing

- xUnit unit tests for validation rules and aggregation logic.
- Integration tests against a real PostgreSQL via Testcontainers.
- One Playwright end-to-end flow: log in, import, view dashboard, drill down, export.
- Frontend component tests for filters and the grid.

## 10. Six-week plan (about 5 sessions a week, 2-3 hours each)

| Week | Focus | Done when |
|---|---|---|
| 1 | Repo, tooling, schema, seed generator, API skeleton, CI, Angular shell | CI is green; seeded data queryable |
| 2 | Import pipeline and error report | Acceptance criteria for import pass |
| 3 | Metrics endpoints, KPI cards, charts | KPIs verified against independent calculation |
| 4 | Grid, filters, drill-down, export | Filters, drill-down and export behave consistently |
| 5 | States, responsive polish, e2e and integration tests | Test suite green; usable on mobile |
| 6 | Docker, deploy, README, screenshots, Upwork write-up | Live demo and public repo ready |

Add a seventh week as a buffer. This plan is tight; if you fall behind, cut export or the region breakdown, not the tests.

## 11. Definition of done

- Public repo with a professional README, architecture diagram and setup instructions.
- Live demo link, with demo credentials and a note about cold-start delay on the free tier.
- Passing CI and a recorded test summary.
- Screenshots of the dashboard, grid and import result.
- A DECISIONS.md covering key trade-offs.
- Fictional data only, with no employer code or rules.

## 12. Version 2: from dashboard to decisions

Version 1 (released as `v1.0.0`) shows what happened. Version 2 helps explain why, and makes Numera work with a client's own files. It is released as `v2.0.0` once M13 to M18 are complete. Every v1 rule (`CLAUDE.md` and sections 4 to 9 above) still applies.

### Scope, in delivery order

| # | Milestone | What the user gets |
|---|---|---|
| M13 | Period comparison | KPI cards show the change against the previous period or the same period last year; the revenue chart shows the comparison period as a second line. |
| M14 | "What changed?" | For a compared range, the regions, channels and categories that drove the revenue change, largest first, each opening its rows. |
| M15 | Import column mapping | Upload a CSV whose headers do not match Numera's: preview it, map the columns (with suggestions), and save the mapping for next time. |
| M16 | Saved views and share links | Named filter views per user, and a read-only dashboard link with an expiry date that works without signing in and can be revoked. |
| M17 | Ask in plain English (optional) | A question box that turns a question into Numera's own metrics and filters and shows the answer as a chart. Starts only after the owner confirms it, because it needs an AI API key and a cost limit. |
| M18 | Release 2.0 | End-to-end test extended, screenshots, README, demo video, release `v2.0.0`. |

### Rules for version 2

- **Comparison periods.** A comparison needs a bounded date range (both `from` and `to`). "Previous period" is the same number of days immediately before `from`. "Same period last year" is the same dates one year earlier (29 February maps to 28 February).
- **Changes are explained, not guessed.** Every change shown (a percentage, a driver) is computed in SQL from the same definitions as the KPIs and, where it is a revenue figure, opens the rows behind it, as in v1.
- **Shared links show aggregates only.** A share link shows the dashboard for its saved filter. It does not open order rows, imports, settings or other filters.
- **AI never writes SQL.** The question box may only choose from Numera's existing metrics, dimensions and filters. Numera validates that choice like any other request. The answer always shows the filters it used, so a person can check it.
- **No new paid services are required to run Numera.** The AI feature is off unless a key is configured, and the app works fully without it.

### Out of version 2

Workspaces, users and roles; scheduled emails; PDF and Excel exports; Excel upload; forecasting; dark mode. They remain candidates for a later version.
