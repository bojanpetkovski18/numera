using Numera.Application.Entities;
using Numera.Application.Imports;

namespace Numera.UnitTests;

public class OrderRowValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    private static RawOrderRow Valid() => new(
        "HP-000001", "2026-01-15", "C-00001", "Europe", "Online Store", "HP-SO-ETH", "Single Origin",
        "2", "18.50", "0.05", "completed");

    private static RowValidationResult Validate(RawOrderRow row) => OrderRowValidator.Validate(row, Today);

    private static void AssertSingleError(RowValidationResult result, string field)
    {
        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void A_fully_valid_row_passes_and_is_converted()
    {
        var result = Validate(Valid());

        Assert.True(result.IsValid);
        var row = result.Row!;
        Assert.Equal("HP-000001", row.OrderId);
        Assert.Equal(new DateOnly(2026, 1, 15), row.OrderDate);
        Assert.Equal(2, row.Quantity);
        Assert.Equal(18.50m, row.UnitPrice);
        Assert.Equal(0.05m, row.Discount);
        Assert.Equal(OrderStatus.Completed, row.Status);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        var result = Validate(Valid() with { OrderId = "  HP-1  ", Quantity = " 3 " });

        Assert.True(result.IsValid);
        Assert.Equal("HP-1", result.Row!.OrderId);
        Assert.Equal(3, result.Row.Quantity);
    }

    // order_date
    [Theory]
    [InlineData("2026-10-11")] // today is allowed
    [InlineData("2024-02-29")] // leap day
    public void Dates_up_to_today_are_valid(string date) =>
        Assert.True(Validate(Valid() with { OrderDate = date }).IsValid);

    [Fact]
    public void A_future_date_is_rejected() =>
        AssertSingleError(Validate(Valid() with { OrderDate = "2026-10-12" }), "order_date");

    [Theory]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("2026-02-30")]
    [InlineData("15/01/2026")]
    [InlineData("2026-1-5")]
    public void Invalid_dates_are_rejected(string date) =>
        AssertSingleError(Validate(Valid() with { OrderDate = date }), "order_date");

    // quantity
    [Theory]
    [InlineData("1")]
    [InlineData("1000")]
    public void Positive_whole_quantities_are_valid(string quantity) =>
        Assert.True(Validate(Valid() with { Quantity = quantity }).IsValid);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Zero_and_negative_quantities_are_rejected(string quantity) =>
        AssertSingleError(Validate(Valid() with { Quantity = quantity }), "quantity");

    [Theory]
    [InlineData("1.5")]
    [InlineData("2.0")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("99999999999")]
    public void Non_integer_quantities_are_rejected(string quantity) =>
        AssertSingleError(Validate(Valid() with { Quantity = quantity }), "quantity");

    // unit_price
    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("19.99")]
    [InlineData("10.500")] // trailing zero is not a third decimal place
    [InlineData("9999999999.99")]
    public void Prices_of_at_least_zero_are_valid(string price) =>
        Assert.True(Validate(Valid() with { UnitPrice = price }).IsValid);

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-5")]
    [InlineData("12.345")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("10000000000")]
    public void Invalid_prices_are_rejected(string price) =>
        AssertSingleError(Validate(Valid() with { UnitPrice = price }), "unit_price");

    // discount
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("0.999")]
    public void Discounts_between_zero_and_one_are_valid(string discount) =>
        Assert.True(Validate(Valid() with { Discount = discount }).IsValid);

    [Theory]
    [InlineData("1.01")]
    [InlineData("-0.01")]
    [InlineData("2")]
    [InlineData("0.1234")]
    [InlineData("ten percent")]
    [InlineData("")]
    public void Invalid_discounts_are_rejected(string discount) =>
        AssertSingleError(Validate(Valid() with { Discount = discount }), "discount");

    // status
    [Theory]
    [InlineData("completed", OrderStatus.Completed)]
    [InlineData("refunded", OrderStatus.Refunded)]
    [InlineData("cancelled", OrderStatus.Cancelled)]
    [InlineData("COMPLETED", OrderStatus.Completed)]
    [InlineData(" Refunded ", OrderStatus.Refunded)]
    public void Known_statuses_are_valid_case_insensitively(string status, OrderStatus expected)
    {
        var result = Validate(Valid() with { Status = status });

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Row!.Status);
    }

    [Theory]
    [InlineData("shipped")]
    [InlineData("canceled")]
    [InlineData("")]
    public void Unknown_statuses_are_rejected(string status) =>
        AssertSingleError(Validate(Valid() with { Status = status }), "status");

    // text fields
    [Theory]
    [InlineData("order_id")]
    [InlineData("customer_id")]
    [InlineData("region")]
    [InlineData("channel")]
    [InlineData("product_sku")]
    [InlineData("product_category")]
    public void Required_text_fields_must_not_be_blank(string field)
    {
        var row = field switch
        {
            "order_id" => Valid() with { OrderId = "  " },
            "customer_id" => Valid() with { CustomerId = null },
            "region" => Valid() with { Region = "" },
            "channel" => Valid() with { Channel = "" },
            "product_sku" => Valid() with { ProductSku = "" },
            _ => Valid() with { ProductCategory = "" },
        };

        AssertSingleError(Validate(row), field);
    }

    [Fact]
    public void Text_longer_than_the_column_is_rejected()
    {
        AssertSingleError(Validate(Valid() with { OrderId = new string('x', 41) }), "order_id");
        Assert.True(Validate(Valid() with { OrderId = new string('x', 40) }).IsValid);
    }

    [Fact]
    public void Every_invalid_field_in_a_row_is_reported()
    {
        var result = Validate(Valid() with { Quantity = "0", UnitPrice = "-1", Status = "nope" });

        Assert.Equal(["quantity", "unit_price", "status"], result.Errors.Select(e => e.Field));
    }
}