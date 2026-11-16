# M17: Ask in plain English (optional)

**Start this milestone only after the owner confirms it.** It needs an AI API key, a spending limit, and a decision on the hosted demo's daily question limit.

## Goal
A user types a question about the sales data and gets an answer as a KPI or a chart, with the filters it used shown and editable.

## Context
Read `docs/SPEC.md` section 12 ("AI never writes SQL"). Metrics (M06), comparison (M13) and drivers (M14) exist.

## Design
- The model receives the question, today's date, the data's date range and the allowed values for region, channel and category (from `/api/filter-options`). It must answer with one tool call whose input matches a fixed schema:
  - metric: revenue, orders, average order value or refund rate;
  - view: total, over time (with granularity), or by dimension;
  - filters: dates, regions, channels and categories;
  - comparison mode;
  - or "cannot answer", with a reason.
- The API validates that input exactly like a request from the UI (`OrderFilterParser` and friends), runs the existing query handlers, and returns the structured request alongside the result. Nothing the model returns is executed as code or SQL.
- Use the current Claude model and SDK guidance at implementation time (load the `claude-api` skill). The key comes from configuration (`Ai__ApiKey`). Without a key, the feature is hidden in the UI and the endpoint returns 404.
- Limits: question length (300 characters), a per-user rate limit, a daily total on the hosted demo, a request timeout, and a clear message when a limit is reached. Questions are not logged in full.

## Scope
1. `POST /api/ask` with `{ question }`, returning the interpreted request (metric, view, filters, comparison) and the result, or a "cannot answer" reason.
2. Dashboard: a question box with three example questions. The answer is rendered with the existing KPI and chart components, with filter chips the user can change, and "Open in dashboard" applying the filters.
3. Loading, cannot-answer, limit-reached, error and success states.

## Out of scope
Chat history, follow-up questions, free-form text answers, answering anything outside the metrics.

## Tests required
- Unit tests: validation of model output (unknown metric, a region not in the data, dates out of order, extra fields), and the prompt built from the filter options.
- Integration tests with a fake model client: a valid interpretation runs the right handler; invalid output is rejected; limits; the feature is off without a key.
- Component tests: the question box, rendering each view, editing the chips.
- A small, manually run evaluation set (about 20 questions with expected interpretations), with results recorded in `PROGRESS.md`. It is not run in CI, because it needs the key.

## Acceptance criteria
- An answer's numbers equal the same request made through the dashboard (automated test with the fake client).
- With no key configured, the app works and the feature is invisible.
- Prompt injection in a question cannot change the allowed schema or reach other data, because only validated, whitelisted requests run.

## Verification required
Run all tests; run the evaluation set against the real API with the owner's key, if provided, and record the results.
