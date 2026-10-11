# M09: Drill-down from charts to grid

## Goal
Clicking a chart segment opens the orders grid showing exactly the matching rows.

## Context
M07 (dashboard) and M08 (grid) are complete.

## Scope
1. Make chart elements clickable and keyboard activatable:
   - A category or region bar/segment navigates to Orders with that category or region added to the current filters.
   - A point or bar on the time chart navigates to Orders with the date range narrowed to that period (day, week or month, matching the current granularity).
2. Navigation preserves all other active filters and uses the URL query string.
3. The Orders screen shows the active filters as removable chips so the user can see why the rows are filtered, and a "back to dashboard" link that keeps the filters.
4. Clear behavior when a drill-down yields zero rows (empty state).

## Out of scope
New metrics, export.

## Tests required
- Component tests: each click type produces the expected route and query params; other filters are preserved.
- Integration or end-to-end check: for a known dataset, the number of rows shown after drilling into a segment equals that segment's order-line count implied by the dashboard data (define precisely which count, given that revenue counts only completed lines, and state it in the test).

## Acceptance criteria
- Drill-down from a chart segment shows only matching rows.
- Period drill-down uses the correct date boundaries (test the week boundary and month end).
- Works by keyboard.

## Verification required
Exercise each drill-down path in a real browser and compare the numbers with the dashboard. Report what you saw.
