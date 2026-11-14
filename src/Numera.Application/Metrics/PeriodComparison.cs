namespace Numera.Application.Metrics;

public enum ComparisonMode
{
    /// <summary>The same number of days, ending the day before <c>from</c>.</summary>
    Previous,

    /// <summary>The same dates one year earlier (29 February becomes 28 February).</summary>
    Year,
}

/// <summary>The comparison period for a bounded range, and how far its days move to line up with the current one.</summary>
public sealed record ComparisonPeriod(ComparisonMode Mode, DateOnly From, DateOnly To, int ShiftYears, int ShiftDays)
{
    public string Name => PeriodComparison.Name(Mode);
}

/// <summary>Comparison rules from docs/ARCHITECTURE.md ("Period comparison").</summary>
public static class PeriodComparison
{
    public static string Name(ComparisonMode mode) => mode == ComparisonMode.Previous ? "previous" : "year";

    /// <summary>The comparison period for <paramref name="from"/>..<paramref name="to"/> (inclusive), or null when it would start before the calendar does.</summary>
    public static ComparisonPeriod? For(DateOnly from, DateOnly to, ComparisonMode mode)
    {
        if (mode == ComparisonMode.Previous)
        {
            var days = to.DayNumber - from.DayNumber + 1;
            return from.DayNumber - days < DateOnly.MinValue.DayNumber
                ? null
                : new ComparisonPeriod(mode, from.AddDays(-days), from.AddDays(-1), 0, days);
        }

        // DateOnly.AddYears maps 29 February to 28 February, as the definition requires.
        return from.Year == DateOnly.MinValue.Year
            ? null
            : new ComparisonPeriod(mode, from.AddYears(-1), to.AddYears(-1), 1, 0);
    }

    /// <summary>
    /// Parses the <c>compare</c> query parameter against an already parsed filter. Returns null when no comparison
    /// was asked for or when the input is invalid (the reason is added to <paramref name="errors"/>).
    /// </summary>
    public static ComparisonPeriod? Parse(string? value, OrderFilter filter, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        ComparisonMode mode;
        switch (value.Trim().ToLowerInvariant())
        {
            case "previous": mode = ComparisonMode.Previous; break;
            case "year": mode = ComparisonMode.Year; break;
            default:
                errors["compare"] = ["compare must be one of: previous, year."];
                return null;
        }

        if (filter.From is not { } from || filter.To is not { } to)
        {
            errors["compare"] = ["A comparison needs both 'from' and 'to'."];
            return null;
        }

        if (from > to)
        {
            return null; // already reported on 'to' by the filter parser
        }

        var period = For(from, to, mode);
        if (period is null)
        {
            errors["compare"] = ["The comparison period would start before the year 1."];
        }

        return period;
    }
}
