using System.Globalization;
using Numera.Application.Metrics;

namespace Numera.Application.Orders;

/// <summary>Columns the grid may sort by. Anything else is rejected (no dynamic SQL from input).</summary>
public enum OrderSortField
{
    OrderDate,
    OrderId,
    CustomerId,
    Region,
    Channel,
    Category,
    Sku,
    Quantity,
    UnitPrice,
    Discount,
    LineTotal,
    Status,
}

public sealed record OrdersQuery(
    OrderFilter Filter,
    string? Search,
    OrderSortField Sort,
    bool Descending,
    int Page,
    int PageSize);

public sealed record OrderRow(
    long Id,
    string OrderId,
    DateOnly OrderDate,
    string CustomerId,
    string Region,
    string Channel,
    string ProductSku,
    string ProductCategory,
    int Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal LineTotal,
    string Status);

public sealed record OrdersPage(IReadOnlyList<OrderRow> Items, int TotalCount, int Page, int PageSize, int TotalPages);

public static class OrdersQueryParser
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 40;

    /// <summary>Sort names as they appear in the query string (camelCase, matching the JSON fields).</summary>
    public static readonly IReadOnlyDictionary<string, OrderSortField> SortNames = new Dictionary<string, OrderSortField>(StringComparer.OrdinalIgnoreCase)
    {
        ["orderDate"] = OrderSortField.OrderDate,
        ["orderId"] = OrderSortField.OrderId,
        ["customerId"] = OrderSortField.CustomerId,
        ["region"] = OrderSortField.Region,
        ["channel"] = OrderSortField.Channel,
        ["productCategory"] = OrderSortField.Category,
        ["productSku"] = OrderSortField.Sku,
        ["quantity"] = OrderSortField.Quantity,
        ["unitPrice"] = OrderSortField.UnitPrice,
        ["discount"] = OrderSortField.Discount,
        ["lineTotal"] = OrderSortField.LineTotal,
        ["status"] = OrderSortField.Status,
    };

    /// <summary>
    /// Parses grid parameters on top of the shared filter. Defaults: newest first, page 1, 25 rows.
    /// The page size is capped at <see cref="MaxPageSize"/>; larger values are rejected, not silently clamped.
    /// </summary>
    public static (OrdersQuery Query, Dictionary<string, string[]> Errors) Parse(
        OrderFilterInput filterInput, string? search, string? sort, string? dir, string? page, string? pageSize)
    {
        var (filter, errors) = OrderFilterParser.Parse(filterInput, allowStatus: true);

        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term is { Length: > MaxSearchLength })
        {
            errors["q"] = [$"Search text must be at most {MaxSearchLength} characters."];
        }

        var sortField = OrderSortField.OrderDate;
        if (!string.IsNullOrWhiteSpace(sort) && !SortNames.TryGetValue(sort.Trim(), out sortField))
        {
            errors["sort"] = [$"sort must be one of: {string.Join(", ", SortNames.Keys)}."];
        }

        var descending = sort is null || string.IsNullOrWhiteSpace(sort); // newest first by default
        if (!string.IsNullOrWhiteSpace(dir))
        {
            switch (dir.Trim().ToLowerInvariant())
            {
                case "asc": descending = false; break;
                case "desc": descending = true; break;
                default: errors["dir"] = ["dir must be asc or desc."]; break;
            }
        }

        var pageNumber = ParseInt(page, "page", 1, 1, int.MaxValue, errors);
        var size = ParseInt(pageSize, "pageSize", DefaultPageSize, 1, MaxPageSize, errors);

        return (new OrdersQuery(filter, term, sortField, descending, pageNumber, size), errors);
    }

    /// <summary>Escapes LIKE wildcards so the user's text is matched literally.</summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";

    private static int ParseInt(string? value, string field, int fallback, int min, int max, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < min || number > max)
        {
            errors[field] = [max == int.MaxValue ? $"{field} must be a whole number of at least {min}." : $"{field} must be between {min} and {max}."];
            return fallback;
        }

        return number;
    }
}