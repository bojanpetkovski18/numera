# Numera: portfolio kit

Material for an Upwork portfolio entry and similar profiles. Everything here describes what is built and verified in this repository; numbers appear only where they were measured and recorded in [PROGRESS.md](../../PROGRESS.md).

## Upwork portfolio entry (copy and paste)

Upwork's limits differ between its help pages (title 40 or 70 characters, description 300 or 600). These texts fit the stricter limits; a longer description is included for when the editor allows it.

| Field | Text | Characters |
|---|---|---|
| Project title | Sales Reporting App: Angular + .NET | 35 |
| Alternative title | CSV to KPI Dashboard with Drill-Down | 36 |
| Your role | Solo full-stack developer: architecture, Angular, .NET API, PostgreSQL, tests, Docker, CI | 89 |
| Skills (pick 5) | Angular, ASP.NET Core, PostgreSQL, Dashboard, Data Visualization (alternatives: C#, .NET, TypeScript, Docker, CSV) | |
| Project link | The GitHub repository (and the live demo link once it is deployed) | |

**Project description (284 characters):**

> Web app that turns a sales CSV into a reporting dashboard: validated import with an error report, KPI cards and charts, shared filters, click-through from any chart to the exact rows behind it, and CSV export. Angular 22, ASP.NET Core (.NET 10), PostgreSQL, Docker; tested end to end.

**Longer description, if 600 characters are allowed:**

> Small teams often run their sales reporting from spreadsheets. This web app turns a sales CSV into a reporting dashboard: an import that validates every row and explains each rejected one, KPI cards and charts computed in the database from one set of definitions, filters shared by every screen, one click from any chart to the exact rows behind it, and CSV export of the current view. Built solo with Angular 22, ASP.NET Core on .NET 10 and PostgreSQL; tested from the database to the browser; runs with one Docker command. Fictional demo data; built with AI assistance.

**Images, in this order, with captions:**

1. `docs/screenshots/dashboard.png`: *Dashboard: revenue, orders, average order value and refund rate, revenue over time and by category and region. Every number is computed in the database from one set of definitions.*
2. `docs/screenshots/orders-drill-down.png`: *Clicking the Blends bar opens the orders grid on exactly those order lines. The chips show why the list is filtered; the view can be sorted, searched and exported to CSV.*
3. `docs/screenshots/import-result.png`: *Import result: valid rows imported, problem rows skipped with the reason for each, downloadable as an error report. Re-imported rows are recognized and not counted twice.*
4. `docs/screenshots/dashboard-mobile.png`: *The same dashboard on a phone: filters, KPI cards and charts stack into one column.*

## Portfolio title

**Sales Reporting Dashboard: CSV Import, KPIs, Drill-Down and Export (Angular + .NET)**

## Short description (for the portfolio card)

A reporting web app for small teams that live in spreadsheets. Upload a sales CSV and get validated data, clear KPIs and charts, a filterable orders grid, one-click drill-down from any chart to the exact rows behind it, and CSV export of what you see. Built solo with Angular 22, ASP.NET Core on .NET 10 and PostgreSQL, with automated tests from database to browser, and deployed with Docker.

## The problem and the solution

**Problem.** A small business exports sales data from its shop and sales channels into spreadsheets. Answering simple questions (revenue last quarter, which region is behind, which orders make up a refund spike) means manual filtering and formulas, numbers that disagree between sheets, and no easy way to share a view.

**Solution.** Numera imports the CSV with validation for every row and explains every rejected row. Revenue, orders, average order value and refund rate are defined once and computed in the database, so every screen agrees. Filters are shared by the dashboard and the grid and kept in the link. Clicking a chart opens exactly the order lines behind that number, and the grid's view exports to CSV.

## My role

Solo developer: requirements, architecture, backend, frontend, database, tests, Docker and CI, deployment and documentation. Built with AI assistance (Claude) under written rules for scope, testing and honesty about what was verified. I directed the work milestone by milestone and reviewed every change.

## Technologies

- **Backend:** C#, ASP.NET Core on .NET 10 (minimal APIs), EF Core 10, PostgreSQL 17, Npgsql (binary COPY, hand-written aggregate SQL), JWT authentication, rate limiting, RFC 7807 problem details
- **Frontend:** Angular 22 (standalone components, Signals, zoneless), NgRx SignalStore, Chart.js, responsive CSS, accessible components
- **Testing:** xUnit, Testcontainers (real PostgreSQL in tests), Vitest component tests, Playwright end-to-end test, axe accessibility checks
- **Delivery:** Docker (multi-stage, non-root), Docker Compose, GitHub Actions CI, Render and Neon

## Implemented features

- CSV import with validation for every row, duplicate detection, a downloadable error report and import history (10 MB and 100,000-row limits)
- Dashboard: four KPI cards, revenue over time (day, week or month), revenue by category and by region
- Shared filters (date presets or custom range, region, channel, category) kept in the URL
- Orders grid with server-side paging and sorting, status filter and order ID search; card layout on phones
- Drill-down from any chart bar or point, by mouse or keyboard, to exactly the matching order lines
- CSV export of the current view, streamed from the database, with spreadsheet formula-injection protection
- Demo data reset with confirmation; loading, empty, error and success states on every screen
- Accessibility: keyboard navigation, skip link, focus management, axe-checked at three screen widths

## Most valuable technical decisions

1. **Numbers that agree everywhere.** KPI definitions live in one place and run as SQL in PostgreSQL, never by loading rows into the app. A drill-down opens the grid on exactly the rows behind a chart value, and an automated test checks for every chart point and bar that the rows add up to the value shown.
2. **An import pipeline that reports instead of failing.** Rows stream into a staging table with PostgreSQL's binary COPY. Invalid and duplicate rows are recorded with a row number and reason, and the rest are imported in one transaction. Measured: a 100,000-row file in about 4.5 seconds on a development machine.
3. **An export that matches the screen.** The grid and the export share one query and the export reads from a single database snapshot, so the file holds exactly what the grid shows. It streams without holding the file in server memory (measured with a throttled download). Oversized exports are refused with a clear message rather than truncated.
4. **One production image, tested end to end.** The same Docker image runs locally, in CI and on the hosted demo, and a Playwright test drives the main flow against it on every push. It caught a production-only Content-Security-Policy problem before release.

## Suggested screenshot sequence

1. `docs/screenshots/dashboard.png`: the dashboard with KPIs and charts (lead image)
2. `docs/screenshots/orders-drill-down.png`: the grid after a drill-down, with filter chips
3. `docs/screenshots/import-result.png`: an import with rejected rows and the error report button
4. `docs/screenshots/dashboard-mobile.png`: the dashboard on a phone
5. `docs/screenshots/login.png`: the sign-in page with the demo account (optional)

## Search keywords

sales dashboard, reporting dashboard, KPI dashboard, CSV import, data import pipeline, data validation, drill-down reports, CSV export, Angular, Angular Signals, NgRx, ASP.NET Core, .NET, C#, Web API, PostgreSQL, EF Core, Chart.js, data visualization, full-stack developer, business intelligence, admin panel, Docker, GitHub Actions, Playwright, Testcontainers, accessibility

## Client services this project supports

This project shows the kind of work I take on for clients: custom dashboards and reporting tools that turn exported business data into trustworthy numbers; CSV and spreadsheet import pipelines with clear validation and error reports; data grids with filtering, search and export; and full-stack Angular and .NET applications backed by PostgreSQL, delivered with automated tests, Docker and CI so they can be maintained after hand-over.
