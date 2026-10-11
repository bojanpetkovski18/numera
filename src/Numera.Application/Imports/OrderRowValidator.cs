using System.Globalization;
using Numera.Application.Entities;

namespace Numera.Application.Imports;

/// <summary>The text of one CSV data row, before any validation.</summary>
public sealed record RawOrderRow(
    string? OrderId,
    string? OrderDate,
    string? CustomerId,
    string? Region,
    string? Channel,
    string? ProductSku,
    string? ProductCategory,
    string? Quantity,
    string? UnitPrice,
    string? Discount,
    string? Status);

public sealed record ValidatedOrderRow(
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
    OrderStatus Status);

public sealed record RowError(string? Field, string Message);

public sealed record RowValidationResult(ValidatedOrderRow? Row, IReadOnlyList<RowError> Errors)
{
    public bool IsValid => Row is not null && Errors.Count == 0;
}

/// <summary>
/// The import validation rules from the spec. Pure: no I/O and no clock (the caller passes today's date).
/// Every failing field is reported, so a row can produce several errors.
/// </summary>
public static class OrderRowValidator
{
    public const string DateFormat = "yyyy-MM-dd";

    // Column limits: orders.order_id etc. (see NumeraDbContext) and numeric(12,2) / numeric(4,3).
    private const int IdMaxLength = 40;
    private const int NameMaxLength = 60;
    private const decimal MaxUnitPrice = 9_999_999_999.99m;

    /// <summary>
    /// The latest order date accepted as "not in the future": today's date in the earliest time zone (UTC+14),
    /// so an order placed today anywhere in the world is accepted whatever the server's clock zone.
    /// </summary>
    public static DateOnly LatestAllowedDate(DateTimeOffset now) =>
        DateOnly.FromDateTime(now.UtcDateTime.AddHours(14));

    /// <param name="raw">The row as read from the file.</param>
    /// <param name="today">The latest allowed order date; normally <see cref="LatestAllowedDate"/>.</param>
    public static RowValidationResult Validate(RawOrderRow raw, DateOnly today)
    {
        var errors = new List<RowError>();

        var orderId = RequiredText(raw.OrderId, ImportColumns.OrderId, IdMaxLength, errors);
        var customerId = RequiredText(raw.CustomerId, ImportColumns.CustomerId, IdMaxLength, errors);
        var region = RequiredText(raw.Region, ImportColumns.Region, NameMaxLength, errors);
        var channel = RequiredText(raw.Channel, ImportColumns.Channel, NameMaxLength, errors);
        var sku = RequiredText(raw.ProductSku, ImportColumns.ProductSku, IdMaxLength, errors);
        var category = RequiredText(raw.ProductCategory, ImportColumns.ProductCategory, NameMaxLength, errors);

        var date = ParseDate(raw.OrderDate, today, errors);
        var quantity = ParseQuantity(raw.Quantity, errors);
        var unitPrice = ParseUnitPrice(raw.UnitPrice, errors);
        var discount = ParseDiscount(raw.Discount, errors);
        var status = ParseStatus(raw.Status, errors);

        if (errors.Count > 0)
        {
            return new RowValidationResult(null, errors);
        }

        var row = new ValidatedOrderRow(
            orderId!, date!.Value, customerId!, region!, channel!, sku!, category!,
            quantity!.Value, unitPrice!.Value, discount!.Value, status!.Value);
        return new RowValidationResult(row, errors);
    }

    private static string? RequiredText(string? value, string field, int maxLength, List<RowError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(field, $"{field} is required."));
            return null;
        }

        if (text.Length > maxLength)
        {
            errors.Add(new RowError(field, $"{field} must be at most {maxLength} characters."));
            return null;
        }

        return text;
    }

    private static DateOnly? ParseDate(string? value, DateOnly today, List<RowError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(ImportColumns.OrderDate, "order_date is required."));
            return null;
        }

        if (!DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add(new RowError(ImportColumns.OrderDate, $"order_date must be a valid date in {DateFormat} format."));
            return null;
        }

        if (date > today)
        {
            errors.Add(new RowError(ImportColumns.OrderDate, "order_date must not be in the future."));
            return null;
        }

        return date;
    }

    private static int? ParseQuantity(string? value, List<RowError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(ImportColumns.Quantity, "quantity is required."));
            return null;
        }

        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var quantity))
        {
            errors.Add(new RowError(ImportColumns.Quantity, "quantity must be a whole number."));
            return null;
        }

        if (quantity <= 0)
        {
            errors.Add(new RowError(ImportColumns.Quantity, "quantity must be greater than 0."));
            return null;
        }

        return quantity;
    }

    private static decimal? ParseUnitPrice(string? value, List<RowError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(ImportColumns.UnitPrice, "unit_price is required."));
            return null;
        }

        if (!TryParseDecimal(text, out var price))
        {
            errors.Add(new RowError(ImportColumns.UnitPrice, "unit_price must be a number."));
            return null;
        }

        if (price < 0)
        {
            errors.Add(new RowError(ImportColumns.UnitPrice, "unit_price must be at least 0."));
            return null;
        }

        if (price > MaxUnitPrice)
        {
            errors.Add(new RowError(ImportColumns.UnitPrice, "unit_price is too large."));
            return null;
        }

        if (decimal.Round(price, 2) != price)
        {
            errors.Add(new RowError(ImportColumns.UnitPrice, "unit_price must have at most 2 decimal places."));
            return null;
        }

        return price;
    }

    private static decimal? ParseDiscount(string? value, List<RowError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(ImportColumns.Discount, "discount is required."));
            return null;
        }

        if (!TryParseDecimal(text, out var discount))
        {
            errors.Add(new RowError(ImportColumns.Discount, "discount must be a number between 0 and 1."));
            return null;
        }

        if (discount < 0 || discount > 1)
        {
            errors.Add(new RowError(ImportColumns.Discount, "discount must be between 0 and 1."));
            return null;
        }

        if (decimal.Round(discount, 3) != discount)
        {
            errors.Add(new RowError(ImportColumns.Discount, "discount must have at most 3 decimal places."));
            return null;
        }

        return discount;
    }

    private static OrderStatus? ParseStatus(string? value, List<RowError> errors)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(ImportColumns.Status, "status is required."));
            return null;
        }

        switch (text.ToLowerInvariant())
        {
            case "completed":
                return OrderStatus.Completed;
            case "refunded":
                return OrderStatus.Refunded;
            case "cancelled":
                return OrderStatus.Cancelled;
            default:
                errors.Add(new RowError(ImportColumns.Status, "status must be one of: completed, refunded, cancelled."));
                return null;
        }
    }

    private static bool TryParseDecimal(string text, out decimal value) =>
        decimal.TryParse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
}