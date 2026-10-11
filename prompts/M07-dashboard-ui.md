# M07: Dashboard UI (KPIs, charts, date filter)

## Goal
A dashboard a business would actually want to look at.

## Context
M06 is complete. Read `docs/SPEC.md` sections 3 and 5.

## Scope
1. Dashboard page with:
   - KPI cards: revenue, orders, average order value, refund rate, each with a clear label and formatted value.
   - Revenue over time chart with a day/week/month toggle.
   - Breakdown charts by product category and by region.
   - A global filter bar: date range with presets (last 30 days, last 90 days, year to date, all time), region, channel, category.
2. Filters live in shared state and are reflected in the URL query string so views are shareable and survive refresh.
3. Use a mature charting library (choose one, justify it in `DECISIONS.md`). Charts must be readable, accessible (text alternatives or an accessible data table fallback, sufficient color contrast, not color-only encoding) and responsive.
4. Every widget has loading (skeleton), empty ("no data for these filters"), and error (with retry) states.
5. Visual quality: consistent spacing and typography from the design tokens, number formatting, tooltips, no clutter. It should look like a product, not a template.
6. Dark mode is optional; do it only if it is cheap with the existing tokens.

## Out of scope
Drill-down interaction (M09), the grid (M08), export.

## Tests required
- Component tests: filter changes trigger the right API calls; each widget's loading, empty and error states render; URL query params round-trip into filter state.
- Unit tests for formatting helpers.

## Acceptance criteria
- Changing a filter updates all KPIs and charts consistently.
- Reloading the page with a filtered URL restores the same view.
- Usable at 375 px, 768 px and desktop widths.
- No console errors during normal use.

## Verification required
Run the app with seeded data, exercise every filter and the time toggle in a real browser at three widths, check the browser console, and report what you saw. Run all tests.
