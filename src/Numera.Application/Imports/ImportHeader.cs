namespace Numera.Application.Imports;

public static class ImportColumns
{
    public const string OrderId = "order_id";
    public const string OrderDate = "order_date";
    public const string CustomerId = "customer_id";
    public const string Region = "region";
    public const string Channel = "channel";
    public const string ProductSku = "product_sku";
    public const string ProductCategory = "product_category";
    public const string Quantity = "quantity";
    public const string UnitPrice = "unit_price";
    public const string Discount = "discount";
    public const string Status = "status";

    public static readonly IReadOnlyList<string> Required =
    [
        OrderId, OrderDate, CustomerId, Region, Channel, ProductSku,
        ProductCategory, Quantity, UnitPrice, Discount, Status,
    ];
}

/// <summary>
/// The outcome of checking a CSV header row: where each Numera field is (<see cref="ColumnIndex"/>, keyed by field),
/// and what is missing, repeated or ignored. <see cref="Mapped"/> is true when it came from a column mapping.
/// </summary>
public sealed record HeaderEvaluation(
    IReadOnlyDictionary<string, int> ColumnIndex,
    int ColumnCount,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Duplicated,
    IReadOnlyList<string> Unknown,
    bool Mapped = false)
{
    public bool IsValid => Missing.Count == 0 && Duplicated.Count == 0;

    public IReadOnlyList<string> Warnings =>
        Unknown.Select(name => Mapped ? $"Column '{name}' was not mapped and was ignored." : $"Unknown column '{name}' was ignored.").ToList();

    /// <summary>Matches names case-insensitively after trimming. Unknown columns are ignored with a warning.</summary>
    public static HeaderEvaluation Evaluate(IReadOnlyList<string?> headers)
    {
        var index = new Dictionary<string, int>();
        var duplicated = new List<string>();
        var unknown = new List<string>();

        for (var i = 0; i < headers.Count; i++)
        {
            var name = Clean(headers[i]).ToLowerInvariant();
            if (ImportColumns.Required.Contains(name))
            {
                if (!index.TryAdd(name, i) && !duplicated.Contains(name))
                {
                    duplicated.Add(name);
                }
            }
            else
            {
                unknown.Add(name.Length == 0 ? $"(column {i + 1})" : name);
            }
        }

        var missing = ImportColumns.Required.Where(c => !index.ContainsKey(c)).ToList();
        return new HeaderEvaluation(index, headers.Count, missing, duplicated, unknown);
    }

    /// <summary>
    /// Matches the mapping's source columns against the headers (case-insensitively, after trimming). A source column
    /// that is absent is reported as missing, one that appears twice as duplicated; columns no field uses are ignored
    /// with a warning. The index is keyed by Numera field, so the rest of the import reads mapped files unchanged.
    /// </summary>
    public static HeaderEvaluation FromMapping(IReadOnlyList<string?> headers, ImportMapping mapping)
    {
        var names = headers.Select(Clean).ToList();
        var index = new Dictionary<string, int>();
        var missing = new List<string>();
        var duplicated = new List<string>();

        foreach (var field in ImportColumns.Required)
        {
            var source = mapping.SourceFor(field);
            var positions = Enumerable.Range(0, names.Count)
                .Where(i => string.Equals(names[i], source, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (positions.Count == 0)
            {
                missing.Add($"{source} (for {field})");
                continue;
            }

            index[field] = positions[0];
            if (positions.Count > 1)
            {
                duplicated.Add(source);
            }
        }

        var used = index.Values.ToHashSet();
        var unknown = Enumerable.Range(0, names.Count)
            .Where(i => !used.Contains(i) && !duplicated.Contains(names[i], StringComparer.OrdinalIgnoreCase))
            .Select(i => names[i].Length == 0 ? $"(column {i + 1})" : names[i])
            .ToList();
        return new HeaderEvaluation(index, headers.Count, missing, duplicated, unknown, Mapped: true);
    }

    /// <summary>A header as written, without surrounding spaces or a byte order mark.</summary>
    public static string Clean(string? header) => (header ?? string.Empty).Trim().TrimStart('﻿').Trim();
}
