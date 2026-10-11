# M06: Metrics endpoints (summary, timeseries, breakdown)

## Goal
Fast, correct aggregate queries behind the dashboard.

## Context
Read `docs/SPEC.md` sections 3, 6 and 7. Data exists from the seed and imports.

## Definitions (write these into `docs/ARCHITECTURE.md` and follow them exactly)
- **Line revenue** = quantity x unit_price x (1 - discount).
- **Revenue** counts lines with status `completed` only.
- **Orders** = number of distinct order_id among completed lines.
- **Average order value** = revenue / orders (0 when there are no orders).
- **Refund rate** = refunded lines / (completed + refunded lines), as a fraction. Cancelled lines are excluded from both sides.
If you believe a definition should change, propose it and log it before implementing.

## Scope
1. A shared filter model used by every metrics and orders query: date range (from, to), region, channel, product category, status (where meaningful).
2. `GET /api/metrics/summary`: revenue, orders, average order value, refund rate for the filter.
3. `GET /api/metrics/timeseries`: revenue (and orders) by day, week or month for the filter, with empty periods filled with zero.
4. `GET /api/metrics/breakdown?by=category|region|channel`: revenue and order count per group for the filter, sorted by revenue.
5. All aggregation in SQL. Inspect the generated SQL and confirm indexes are used (`EXPLAIN` on the seeded data), and note findings in `DECISIONS.md`.
6. Validate inputs (date range order, enum values) and return problem details on bad input.

## Out of scope
Any UI. The orders grid (M08).

## Tests required
- Unit tests for the definitions using small hand-computed datasets.
- Integration tests: for a small known dataset, every endpoint returns hand-computed values (compute independently in the test, not with the same query). Include edge cases: empty result set, only refunds, one-day range, a filter that matches nothing.

## Acceptance criteria
- KPIs match values calculated independently from the same data (automated test).
- Changing filters changes results consistently across all three endpoints.
- Query time on the 200,000-row seed is measured and recorded in `PROGRESS.md` (record the machine context; do not claim a number you did not measure).

## Verification required
Run tests, call each endpoint against the seeded database, and report real responses and timings.
