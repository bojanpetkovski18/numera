using System.Security.Cryptography;
using System.Text;
using Numera.Application.Metrics;

namespace Numera.Application.Views;

/// <summary>
/// What a saved view remembers, in the dashboard's own terms. A preset ("last90") stays relative: the view shows the
/// last 90 days whenever it is opened. Lists mean "any of"; empty means all.
/// </summary>
public sealed record ViewSettings(
    string? Preset,
    string? From,
    string? To,
    string[]? Regions,
    string[]? Channels,
    string[]? Categories,
    string? Granularity,
    string? Compare);

/// <summary>A view's settings applied to a day: the filter, the granularity and the comparison period (if any).</summary>
public sealed record ResolvedView(OrderFilter Filter, Granularity Granularity, ComparisonPeriod? Comparison);

public static class ViewSettingsRules
{
    public static readonly IReadOnlyList<string> Presets = ["all", "last30", "last90", "ytd", "custom"];

    /// <summary>
    /// Validates untrusted settings with the same rules as the dashboard's query parameters and returns them
    /// cleaned (lower-case keywords, dates only for a custom range), or null with the reasons in <paramref name="errors"/>.
    /// </summary>
    public static ViewSettings? Validate(ViewSettings? settings, Dictionary<string, string[]> errors)
    {
        if (settings is null)
        {
            errors["settings"] = ["The view settings are missing."];
            return null;
        }

        var preset = string.IsNullOrWhiteSpace(settings.Preset) ? "all" : settings.Preset.Trim().ToLowerInvariant();
        if (!Presets.Contains(preset))
        {
            errors["settings.preset"] = [$"preset must be one of: {string.Join(", ", Presets)}."];
        }

        var custom = preset == "custom";
        var (filter, filterErrors) = OrderFilterParser.Parse(
            new OrderFilterInput(custom ? settings.From : null, custom ? settings.To : null, settings.Regions, settings.Channels, settings.Categories, null),
            allowStatus: false);
        foreach (var (key, value) in filterErrors)
        {
            errors[$"settings.{key}"] = value;
        }

        if (!Periods.TryParseGranularity(settings.Granularity, out var granularity))
        {
            errors["settings.granularity"] = ["granularity must be one of: day, week, month."];
        }

        var compare = string.IsNullOrWhiteSpace(settings.Compare) ? "none" : settings.Compare.Trim().ToLowerInvariant();
        if (compare is not ("none" or "previous" or "year"))
        {
            errors["settings.compare"] = ["compare must be one of: none, previous, year."];
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new ViewSettings(
            preset,
            filter.From?.ToString("yyyy-MM-dd"),
            filter.To?.ToString("yyyy-MM-dd"),
            [.. filter.Regions],
            [.. filter.Channels],
            [.. filter.Categories],
            granularity.ToString().ToLowerInvariant(),
            compare);
    }

    /// <summary>
    /// Applies validated settings to <paramref name="today"/> the way the dashboard does: presets end today and
    /// include it. The comparison is dropped when the range has no start or no end, as on the dashboard.
    /// </summary>
    public static ResolvedView Resolve(ViewSettings settings, DateOnly today)
    {
        DateOnly? Parse(string? value) => value is null ? null : DateOnly.ParseExact(value, "yyyy-MM-dd");
        var (from, to) = settings.Preset switch
        {
            "last30" => (today.AddDays(-29), today),
            "last90" => (today.AddDays(-89), today),
            "ytd" => (new DateOnly(today.Year, 1, 1), today),
            "custom" => (Parse(settings.From), Parse(settings.To)),
            _ => ((DateOnly?)null, (DateOnly?)null),
        };

        var filter = new OrderFilter(from, to, settings.Regions ?? [], settings.Channels ?? [], settings.Categories ?? [], []);
        Periods.TryParseGranularity(settings.Granularity, out var granularity);
        var errors = new Dictionary<string, string[]>();
        var comparison = settings.Compare is "previous" or "year" && from is not null && to is not null
            ? PeriodComparison.Parse(settings.Compare, filter, errors)
            : null;
        return new ResolvedView(filter, granularity, comparison);
    }
}

/// <summary>Share link tokens: 32 random bytes, URL-safe base64 (43 characters). Only their SHA-256 hash is stored.</summary>
public static class ShareTokens
{
    public const int Length = 43;

    public static (string Token, string Hash) Create()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    /// <summary>A cheap check before any database work: the right length and alphabet.</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
