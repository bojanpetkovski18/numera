# M13: Period comparison

## Goal
Every KPI and the revenue chart can be compared with the previous period or the same period last year, so a user sees at once whether things went up or down.

## Context
Read `docs/SPEC.md` section 12 and the KPI definitions, order filter and metrics endpoints in `docs/ARCHITECTURE.md`. The metrics endpoints (M06), the dashboard (M07) and drill-down (M09) exist.

## Definitions (write these into `docs/ARCHITECTURE.md` and follow them exactly)
- A comparison needs both `from` and `to`. For a range of n days (inclusive):
  - **Previous period** = the n days ending the day before `from`.
  - **Same period last year** = `from` and `to` one year earlier; 29 February becomes 28 February.
- **Relative change** (revenue, orders, average order value) = (current - comparison) / comparison, from the reported (rounded) values, rounded to 4 decimals. None when the comparison value is 0.
- **Refund rate change** = current rate - comparison rate, in percentage points (a fraction, rounded to 4 decimals). None when either period has no completed or refunded lines.
- **Comparison series:** each comparison day is moved forward by the gap between the two periods (n days, or one year) and counted in the current period it lands in. So comparison point i always sits next to current point i, and both cover the same days of the calendar.

## Scope
1. `compare=previous|year` on `GET /api/metrics/summary` and `GET /api/metrics/timeseries`. Absent means no comparison; responses only gain fields.
2. Summary: a `comparison` object (its dates and the same KPIs) and a `changes` object, or null for both.
3. Timeseries: `comparisonRevenue` and `comparisonOrders` per point (null without a comparison), and the comparison dates.
4. Validation: `compare` with a missing `from` or `to`, or an unknown value, is a 400 that names `compare`.
5. All aggregation in SQL, reusing the existing revenue and orders expressions and the order filter.
6. Dashboard: a "Compare to" choice in the filter bar (None, Previous period, Same period last year), kept in the URL with the other dashboard settings. It is disabled, with a short reason, when the range is "All time" or open-ended.
7. KPI cards: the change as text and an arrow ("▲ 12.4% vs previous period"), green or red by whether the change is good (a rising refund rate is bad), the comparison value below, and a clear message when there is no comparison value. Colour is never the only signal.
8. Revenue chart: the comparison series as a dashed, muted line with a legend; the tooltip and the data table show both values. Clicking still opens the current period's rows.
9. The filter bar shows the comparison dates ("Compared with Jul 3, 2026 to Sep 30, 2026").

## Out of scope
Comparison on the breakdown charts and explanations of the change (M14). Comparison on the orders grid or in exports.

## Tests required
- Unit tests: comparison ranges (ordinary, leap day, one-day range, year boundary), relative change and refund rate change, including the "none" cases.
- Integration tests against hand-computed data: summary and timeseries with each mode, a comparison period without data, the comparison series lining up with the current periods for day, week and month, validation errors.
- Component tests: the compare choice (enabled, disabled with reason, URL round trip), KPI change text and direction, the chart's second dataset and table column.

## Acceptance criteria
- Comparison KPIs equal the KPIs requested directly for the comparison dates (automated test).
- The comparison series adds up to the comparison revenue for the whole range (automated test).
- Without `compare`, every v1 response and test is unchanged.
- The dashboard shows the comparison at 375, 768 and 1280 px without overflow, with no console errors and no axe violations.

## Verification required
Run all tests. Call both endpoints with each mode against the seeded database and report real responses and timings. Exercise the dashboard in a browser at three widths.
