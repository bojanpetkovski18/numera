using System.Globalization;
using Numera.Application.Entities;

namespace Numera.Application.Metrics;

/// <summary>
/// The filter shared by every metrics and orders query. Dates are inclusive. Each list means "any of these";
/// an empty list means "no restriction".
/// </summary>
public sealed record OrderFilter(
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Categories,
    IReadOnlyList<OrderStatus> Statuses)
{
    public static readonly OrderFilter None = new(null, null, [], [], [], []);
}

/// <summary>The filter exactly as it arrives in the query string.</summary>
public sealed record OrderFilterInput(
    string? From,
    string? To,
    string[]? Region,
    string[]? Channel,
    string[]? Category,
    string[]? Status);

public static class OrderFilterParser
{
    public const int MaxValuesPerField = 20;
    private const int MaxValueLength = 60;

    /// <summary>
    /// Parses and validates the filter. Returns field errors (empty when valid). When <paramref name="allowStatus"/>
    /// is false, a status filter is an error: revenue and orders are defined on completed lines only.
    /// </summary>
    public static (OrderFilter Filter, Dictionary<string, string[]> Errors) Parse(OrderFilterInput input, bool allowStatus)
    {
        var errors = new Dictionary<string, string[]>();
        var from = ParseDate(input.From, "from", errors);
        var to = ParseDate(input.To, "to", errors);
        if (from is not null && to is not null && from > to)
        {
            errors["to"] = ["'to' must be on or after 'from'."];
        }

        var regions = ParseValues(input.Region, "region", errors);
        var channels = ParseValues(input.Channel, "channel", errors);
        var categories = ParseValues(input.Category, "category", errors);

        var statuses = new List<OrderStatus>();
        var statusValues = Clean(input.Status);
        if (statusValues.Count > 0 && !allowStatus)
        {
            errors["status"] = ["Metrics count completed lines only, so they cannot be filtered by status."];
        }
        else
        {
            foreach (var value in statusValues)
            {
                switch (value.ToLowerInvariant())
                {
                    case "completed": statuses.Add(OrderStatus.Completed); break;
                    case "refunded": statuses.Add(OrderStatus.Refunded); break;
                    case "cancelled": statuses.Add(OrderStatus.Cancelled); break;
                    default:
                        errors["status"] = ["status must be one of: completed, refunded, cancelled."];
                        break;
                }
            }
        }

        return (new OrderFilter(from, to, regions, channels, categories, statuses.Distinct().ToList()), errors);
    }

    private static DateOnly? ParseDate(string? value, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[field] = [$"'{field}' must be a date in yyyy-MM-dd format."];
        return null;
    }

    private static List<string> ParseValues(string[]? values, string field, Dictionary<string, string[]> errors)
    {
        var cleaned = Clean(values);
        if (cleaned.Count > MaxValuesPerField)
        {
            errors[field] = [$"At most {MaxValuesPerField} {field} values can be combined."];
        }
        else if (cleaned.Any(v => v.Length > MaxValueLength))
        {
            errors[field] = [$"Each {field} value must be at most {MaxValueLength} characters."];
        }

        return cleaned;
    }

    private static List<string> Clean(string[]? values) =>
        (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
}