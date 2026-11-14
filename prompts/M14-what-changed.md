# M14: "What changed?"

## Goal
When revenue moves between two periods, Numera shows which regions, channels and categories drove the change, largest first, and lets the user open the rows behind each one.

## Context
Read `docs/SPEC.md` section 12 and the comparison definitions in `docs/ARCHITECTURE.md`. Period comparison (M13) exists.

## Definitions (write these into `docs/ARCHITECTURE.md`)
- For a dimension (region, channel or category), each group's **contribution** = its revenue in the current period - its revenue in the comparison period. Groups present in only one period count as 0 in the other.
- The contributions of one dimension add up to the total revenue change (to within rounding).
- **Share of change** = contribution / total change, shown only when the total change is not 0.
- Drivers are sorted by the absolute contribution, largest first; ties by name.

## Scope
1. `GET /api/metrics/drivers?compare=previous|year&by=region|channel|category` with the usual filters. It returns the total change and, per group, current revenue, comparison revenue and contribution. The same validation as M13 applies.
2. One query per request, in SQL, over both periods at once.
3. Dashboard: a "What changed?" panel, shown when a comparison is active. It has a one-sentence summary built from the data, for example "Revenue rose $41,200 (+8.1%) vs the previous period. The biggest increase was Europe (+$28,400)." Below it are tabs for region, channel and category, each listing the top drivers with a diverging bar, and the rest grouped as "N others".
4. Each driver opens the orders grid on the current period's completed rows for that group. A second link opens the comparison period's rows.
5. Loading, empty ("no change"), error and success states; keyboard access to tabs and links.

## Out of scope
Statistical anomaly detection, forecasting, AI-written explanations.

## Tests required
- Unit tests for contribution, share and ordering, including groups that appear or disappear.
- Integration tests with hand-computed data: contributions add up to the total change; filters are respected; each mode works.
- Component tests: the summary sentence for rise, fall and no change; the tabs; the drill-down links.

## Acceptance criteria
- For every dimension, contributions add up to the summary's revenue change (automated test).
- A driver's drill-down rows add up to its current revenue (automated test).
- The panel works at 375, 768 and 1280 px with no console errors and no axe violations.

## Verification required
Run all tests; call the endpoint on the seeded database (report timings); exercise the panel in a browser.
