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

/// <summary>The outcome of checking a CSV header row against the required columns.</summary>
public sealed record HeaderEvaluation(
    IReadOnlyDictionary<string, int> ColumnIndex,
    int ColumnCount,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Duplicated,
    IReadOnlyList<string> Unknown)
{
    public bool IsValid => Missing.Count == 0 && Duplicated.Count == 0;

    public IReadOnlyList<string> Warnings =>
        Unknown.Select(name => $"Unknown column '{name}' was ignored.").ToList();

    /// <summary>Matches names case-insensitively after trimming. Unknown columns are ignored with a warning.</summary>
    public static HeaderEvaluation Evaluate(IReadOnlyList<string?> headers)
    {
        var index = new Dictionary<string, int>();
        var duplicated = new List<string>();
        var unknown = new List<string>();

        for (var i = 0; i < headers.Count; i++)
        {
            var name = (headers[i] ?? string.Empty).Trim().TrimStart('\uFEFF').ToLowerInvariant();
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
}