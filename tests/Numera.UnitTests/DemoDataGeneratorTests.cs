using Numera.Application.Entities;
using Numera.Infrastructure.Seeding;

namespace Numera.UnitTests;

public class DemoDataGeneratorTests
{
    private static readonly SeedOptions Small = new() { OrderLineCount = 5_000 };

    private static string Fingerprint(IEnumerable<Order> orders) =>
        string.Join('\n', orders.Select(o =>
            $"{o.OrderId}|{o.OrderDate:O}|{o.CustomerId}|{o.Region}|{o.Channel}|{o.ProductSku}|{o.ProductCategory}|{o.Quantity}|{o.UnitPrice}|{o.Discount}|{o.Status}"));

    [Fact]
    public void Same_seed_produces_identical_output()
    {
        var first = DemoDataGenerator.Generate(Small);
        var second = DemoDataGenerator.Generate(Small);

        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void Different_seed_produces_different_output()
    {
        var first = DemoDataGenerator.Generate(Small);
        var second = DemoDataGenerator.Generate(Small with { Seed = Small.Seed + 1 });

        Assert.NotEqual(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void Default_options_produce_200000_lines()
    {
        Assert.Equal(200_000, DemoDataGenerator.Generate(new SeedOptions()).Count);
    }

    [Fact]
    public void Generates_exactly_the_requested_number_of_lines()
    {
        Assert.Equal(1_234, DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 1_234 }).Count);
    }

    [Fact]
    public void Every_row_is_valid()
    {
        var options = new SeedOptions { OrderLineCount = 20_000 };
        var orders = DemoDataGenerator.Generate(options);

        Assert.All(orders, o =>
        {
            Assert.True(Enum.IsDefined(o.Status));
            Assert.True(o.Quantity > 0);
            Assert.True(o.UnitPrice >= 0);
            Assert.InRange(o.Discount, 0m, 1m);
            Assert.InRange(o.OrderDate, options.StartDate, options.EndDate);
            Assert.False(string.IsNullOrWhiteSpace(o.OrderId));
            Assert.False(string.IsNullOrWhiteSpace(o.CustomerId));
            Assert.False(string.IsNullOrWhiteSpace(o.Region));
            Assert.False(string.IsNullOrWhiteSpace(o.Channel));
            Assert.False(string.IsNullOrWhiteSpace(o.ProductSku));
            Assert.False(string.IsNullOrWhiteSpace(o.ProductCategory));
        });
    }

    [Fact]
    public void Order_id_and_sku_combinations_are_unique()
    {
        var orders = DemoDataGenerator.Generate(new SeedOptions { OrderLineCount = 20_000 });

        var distinct = orders.Select(o => (o.OrderId, o.ProductSku)).Distinct().Count();

        Assert.Equal(orders.Count, distinct);
    }

    [Fact]
    public void Lines_of_one_order_share_date_customer_channel_and_status()
    {
        var orders = DemoDataGenerator.Generate(Small);

        foreach (var group in orders.GroupBy(o => o.OrderId))
        {
            Assert.Single(group.Select(o => (o.OrderDate, o.CustomerId, o.Region, o.Channel, o.Status, o.Discount)).Distinct());
        }
    }

    [Fact]
    public void Includes_refunds_cancellations_and_discounts()
    {
        var orders = DemoDataGenerator.Generate(Small);

        Assert.Contains(orders, o => o.Status == OrderStatus.Refunded);
        Assert.Contains(orders, o => o.Status == OrderStatus.Cancelled);
        Assert.Contains(orders, o => o.Discount > 0);
        Assert.True(orders.Count(o => o.Status == OrderStatus.Completed) > orders.Count * 0.8);
    }

    [Fact]
    public void Window_covers_24_months_ending_at_end_date()
    {
        var options = new SeedOptions();

        Assert.Equal(new DateOnly(2024, 10, 1), options.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), options.EndDate);
    }
}
