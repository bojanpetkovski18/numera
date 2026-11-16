# Changelog

Versions follow semantic versioning (D055). Only shipped and verified features are listed; measurements are in [PROGRESS.md](PROGRESS.md).

## 2.0.0 (2026-11-16)

"From dashboard to decisions" ([docs/SPEC.md](docs/SPEC.md) section 12).

### Added

- **Period comparison.** Every KPI against the previous period or the same period last year, with the change shown as an arrow, a sign and words, and the comparison as a dashed line on the revenue chart (`compare=previous|year` on the summary and timeseries endpoints).
- **"What changed?"** The revenue change split by region, channel or category, largest first, with a summary sentence and links to the rows in both periods (`GET /api/metrics/drivers`).
- **Import column mapping.** Files with other column names, `dd/MM/yyyy`, `MM/dd/yyyy` or `dd.MM.yyyy` dates and percentage discounts, through a preview and mapping step with suggestions; saved mappings per user (`POST /api/imports/preview`, `/api/import-mappings`). A second sample file shows it.
- **Saved views and share links.** Named dashboard views per user, and read-only links that expire after 1, 7 or 30 days, can be revoked, and show figures only (`/api/views`, `/api/shares`, public `/api/shared/{token}` and the page `/shared/{token}`).
- **Ask in plain English (optional).** With an Anthropic API key configured, a question box turns a question into one of the dashboard's own requests (Claude, structured output), which Numera validates and answers with its own queries. Off without a key.
- **End-to-end tests** for the version 2 flow, and axe accessibility checks of every main screen at 375, 768 and 1280 px, in CI.

### Changed

- A demo reset also removes saved mappings, saved views and share links.
- The rate limiter runs after authentication, so the question limit can be per user.
- The Import page previews a file before importing it; files in Numera's own format are imported straight away, as before.

### Notes

- The question box's answers have not yet been evaluated against the real model (`tools/Numera.AskEval` needs a key).
- Two migrations: `AddImportMappings`, `AddSavedViewsAndShareLinks`. They run on start in the Docker image, as before.

## 1.0.0 (2026-10-25)

First release: CSV import with validation and an error report, KPI dashboard with charts, shared filters, drill-down to the rows behind any chart value, orders grid with search and sorting, CSV export, demo reset, responsive and accessible layout, Docker image, CI with an end-to-end test.
