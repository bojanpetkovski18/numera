# Numera Architecture

Working notes, kept in sync with the code. The final diagram and overview are added in M12.

## Layers

| Project | Holds |
|---|---|
| `Numera.Api` | Minimal API endpoints, auth, limits, problem details. No business logic. |
| `Numera.Application` | Entities, pure rules (import validation, KPI definitions, order filter), DTOs, EF-based handlers via `INumeraDbContext`. |
| `Numera.Infrastructure` | EF Core `NumeraDbContext` and migrations, PostgreSQL-specific handlers (CSV import with `COPY`, metrics SQL), seeding, JWT. |

PostgreSQL-specific work (bulk `COPY`, hand-written aggregate SQL) lives in Infrastructure (DECISIONS D024, D032); rules that can be tested without a database live in Application.

## KPI definitions

These are the definitions every metric follows (`MetricDefinitions` in `Numera.Application/Metrics`).

- **Line revenue** = quantity x unit_price x (1 - discount).
- **Revenue** counts lines with status `completed` only.
- **Orders** = number of distinct `order_id` among completed lines.
- **Average order value** = revenue / orders (0 when there are no orders).
- **Refund rate** = refunded lines / (completed + refunded lines), as a fraction. Cancelled lines are excluded from both sides. 0 when there are no completed or refunded lines.

Precision and rounding:

- Line revenue and all sums are exact `numeric` in PostgreSQL. Rounding happens once, on the total being reported.
- Money (revenue, average order value) is rounded to 2 decimals, half away from zero. The average order value divides the unrounded revenue.
- Refund rate is rounded to 4 decimals.
- Because each total is rounded separately, the sum of rounded group or period revenues can differ from the rounded overall revenue by a cent or two.
- An order whose lines fall into several groups (for example two categories) is counted once in each of those groups, so group order counts can add up to more than the overall order count.

## Order filter

One filter model (`OrderFilter`) is used by every metrics query and, from M08, by the orders grid.

| Query parameter | Meaning |
|---|---|
| `from`, `to` | Inclusive dates, `yyyy-MM-dd`; `from` must not be after `to`. |
| `region`, `channel`, `category` | Exact match. Repeat the parameter for "any of": `?region=Europe&region=Asia-Pacific`. At most 20 values, 60 characters each. |
| `status` | Grid only. Rejected by metrics endpoints, because revenue and orders are defined on completed lines. |

Invalid input returns a 400 validation problem that names each failing parameter.

## Metrics endpoints

| Endpoint | Returns |
|---|---|
| `GET /api/metrics/summary` | `revenue`, `orders`, `averageOrderValue`, `refundRate`, `completedLines`, `refundedLines` |
| `GET /api/metrics/timeseries?granularity=day\|week\|month` | `points[]` of `periodStart`, `revenue`, `orders` (default granularity: month) |
| `GET /api/metrics/breakdown?by=category\|region\|channel` | `groups[]` of `key`, `revenue`, `orders`, sorted by revenue (descending), then key |

Timeseries details:
- Weeks start on Monday (ISO 8601); months on the 1st. `periodStart` is the first day of the period.
- Every period between the bounds is present; periods without data have zero revenue and orders.
- The bounds are the filter's `from` and `to`. When either is missing, the first or last order date matching the rest of the filter is used; when nothing matches, `points` is empty.
- At most 1,000 points per request; a longer range returns a 400 asking for a shorter range or larger granularity.

Breakdown details: groups that have only refunded or cancelled lines are listed with zero revenue and zero orders.

All aggregation runs in PostgreSQL in a single statement per request (`SUM ... FILTER`, `COUNT(DISTINCT ...)`, `generate_series`); no rows are loaded into the application.