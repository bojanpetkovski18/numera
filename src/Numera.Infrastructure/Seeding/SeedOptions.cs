namespace Numera.Infrastructure.Seeding;

public sealed record SeedOptions
{
    public const int DefaultSeed = 20260101;

    /// <summary>Fixed random seed; the same options always produce identical rows.</summary>
    public int Seed { get; init; } = DefaultSeed;

    /// <summary>Number of order lines (rows in <c>orders</c>) to generate.</summary>
    public int OrderLineCount { get; init; } = 200_000;

    /// <summary>Last day of data. Fixed (not the clock) so output is deterministic and never in the future.</summary>
    public DateOnly EndDate { get; init; } = new(2026, 9, 30);

    /// <summary>Number of calendar months of data ending at <see cref="EndDate"/>'s month.</summary>
    public int Months { get; init; } = 24;

    public DateOnly StartDate => new DateOnly(EndDate.Year, EndDate.Month, 1).AddMonths(-(Months - 1));
}
