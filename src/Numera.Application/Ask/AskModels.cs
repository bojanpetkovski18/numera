using Numera.Application.Metrics;

namespace Numera.Application.Ask;

/// <summary>
/// What the model returns for a question: exactly this shape, enforced by the JSON schema in <see cref="AskSchema"/>.
/// Every field is always present; empty strings and empty lists mean "not used". Nothing here is trusted: it goes
/// through <see cref="AskValidator"/> like any request from the UI.
/// </summary>
public sealed record AskInterpretation(
    bool Answerable,
    string Reason,
    string Metric,
    string View,
    string Granularity,
    string Dimension,
    string From,
    string To,
    string[] Regions,
    string[] Channels,
    string[] Categories,
    string Compare);

/// <summary>What the model is told about the data: today, the data's dates and the values the filters may use.</summary>
public sealed record AskContext(DateOnly Today, FilterOptions Options);

public enum AskMetric
{
    Revenue,
    Orders,
    AverageOrderValue,
    RefundRate,
}

public enum AskView
{
    Total,
    OverTime,
    ByDimension,
}

/// <summary>A question turned into a request Numera can run: only known metrics, values and dates.</summary>
public sealed record AskPlan(
    AskMetric Metric,
    AskView View,
    Granularity Granularity,
    BreakdownDimension Dimension,
    OrderFilter Filter,
    ComparisonPeriod? Comparison);

/// <summary>The plan in the API's words, so the page can show it as chips and run it again through the dashboard endpoints.</summary>
public sealed record AskRequestDto(
    string Metric,
    string View,
    string? Granularity,
    string? Dimension,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Categories,
    string Compare);

/// <summary>
/// The answer: either the request and its result (a summary, a timeseries or a breakdown, exactly as the dashboard
/// endpoints return them), or why it could not be answered.
/// </summary>
public sealed record AskAnswer(string Status, AskRequestDto? Request, object? Result, string? Reason)
{
    public static AskAnswer CannotAnswer(string reason) => new("cannot_answer", null, null, reason);
}

/// <summary>The outcome of asking the model.</summary>
public sealed record AskModelResult(AskInterpretation? Interpretation, AskModelFailure? Failure);

public enum AskModelFailure
{
    /// <summary>The model declined the question.</summary>
    Refused,

    /// <summary>The service was busy, unreachable or too slow; worth trying again later.</summary>
    Unavailable,

    /// <summary>The reply did not match the schema.</summary>
    Invalid,
}

/// <summary>Turns a question into an <see cref="AskInterpretation"/>. The real one calls Claude; tests use a fake.</summary>
public interface IAskModel
{
    Task<AskModelResult> InterpretAsync(string question, AskContext context, CancellationToken ct);
}
