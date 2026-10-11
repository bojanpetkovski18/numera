# M08: Orders grid (API and UI)

## Goal
A fast, server-driven data grid over the order lines.

## Context
M06 and M07 are complete; reuse the shared filter model and state.

## Scope
1. `GET /api/orders`: server-side paging, sorting (whitelist of sortable columns only) and filtering with the shared filter model plus a text search on `order_id` (prefix or contains: decide, justify, index accordingly). Return items, total count, page and page size. Enforce a maximum page size.
2. Efficient counting: avoid a slow `COUNT(*)` on every request if it hurts at 200,000 rows; measure first, then decide, and log the decision.
3. Angular Orders screen:
   - Table with columns for all key fields, formatted dates and money, status badges.
   - Server paging controls and page-size choice, click-to-sort headers, keyboard accessible.
   - Filter bar shared with the dashboard filters, plus the order-id search with debounce.
   - Loading, empty and error states; sticky header; horizontal scroll or a card layout on mobile.
4. Filters and sorting reflected in the URL query string.

## Out of scope
Drill-down wiring (M09) and export (M10).

## Tests required
- Integration: paging returns correct slices and totals; sorting works and rejects non-whitelisted columns; each filter narrows results correctly; combined filters; page size cap enforced.
- Angular component tests: header click toggles sort and calls the API; search is debounced; empty and error states.

## Acceptance criteria
- Changing filters updates the grid and stays consistent with the dashboard's filtered totals (for example, the grid's total count for the same filter equals the number of order lines the metrics are computed from; define the check in the test).
- The grid stays responsive with the 200,000-row seed. Measure typical request time and record it; do not guess.
- Usable at 375 px width.

## Verification required
Run tests, then use the grid in a real browser with several filter, sort and paging combinations, and report what you saw.
