using Numera.Application.Entities;

namespace Numera.Application.Metrics;

/// <summary>A line as the KPI definitions see it.</summary>
public sealed record MetricLine(string OrderId, int Quantity, decimal UnitPrice, decimal Discount, OrderStatus Status);

public sealed record KpiValues(decimal Revenue, int Orders, decimal AverageOrderValue, decimal RefundRate, int CompletedLines, int RefundedLines);

/// <summary>
/// The KPI definitions from docs/ARCHITECTURE.md. Production totals are summed in SQL; the ratios and rounding
/// below are applied to those totals, and <see cref="Summarize"/> is the in-memory reference used by tests.
/// </summary>
public static class MetricDefinitions
{
    /// <summary>quantity x unit_price x (1 - discount), exact (no rounding).</summary>
    public static decimal LineRevenue(int quantity, decimal unitPrice, decimal discount) =>
        quantity * unitPrice * (1 - discount);

    /// <summary>Revenue / orders, or 0 when there are no orders. Uses the unrounded revenue.</summary>
    public static decimal AverageOrderValue(decimal revenue, int orders) =>
        orders == 0 ? 0m : RoundMoney(revenue / orders);

    /// <summary>Refunded lines / (completed + refunded lines), as a fraction; 0 when both are zero.</summary>
    public static decimal RefundRate(int refundedLines, int completedLines)
    {
        var denominator = refundedLines + completedLines;
        return denominator == 0 ? 0m : decimal.Round((decimal)refundedLines / denominator, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>Money is reported to the cent, rounding half away from zero, after summing exact line values.</summary>
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static KpiValues FromTotals(decimal exactRevenue, int orders, int completedLines, int refundedLines) => new(
        RoundMoney(exactRevenue),
        orders,
        AverageOrderValue(exactRevenue, orders),
        RefundRate(refundedLines, completedLines),
        completedLines,
        refundedLines);

    /// <summary>Reference implementation over in-memory lines (tests and documentation).</summary>
    public static KpiValues Summarize(IEnumerable<MetricLine> lines)
    {
        var list = lines.ToList();
        var completed = list.Where(l => l.Status == OrderStatus.Completed).ToList();
        var revenue = completed.Sum(l => LineRevenue(l.Quantity, l.UnitPrice, l.Discount));
        var orders = completed.Select(l => l.OrderId).Distinct().Count();
        var refunded = list.Count(l => l.Status == OrderStatus.Refunded);
        return FromTotals(revenue, orders, completed.Count, refunded);
    }
}