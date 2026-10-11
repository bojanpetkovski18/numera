using Numera.Application.Entities;

namespace Numera.Infrastructure.Seeding;

/// <summary>
/// Generates fictional order lines for "Harbor &amp; Pine Coffee". Pure and deterministic: no clock, no I/O.
/// </summary>
public static class DemoDataGenerator
{
    private sealed record Product(string Sku, string Category, decimal UnitPrice);

    private static readonly Product[] Catalog =
    [
        new("HP-SO-ETH", "Single Origin", 18.50m),
        new("HP-SO-COL", "Single Origin", 16.00m),
        new("HP-SO-KEN", "Single Origin", 19.50m),
        new("HP-SO-GUA", "Single Origin", 17.00m),
        new("HP-SO-SUM", "Single Origin", 17.50m),
        new("HP-BL-HAR", "Blends", 14.00m),
        new("HP-BL-PIN", "Blends", 15.00m),
        new("HP-BL-DAW", "Blends", 13.50m),
        new("HP-BL-NIT", "Blends", 14.50m),
        new("HP-EQ-GRN", "Equipment", 89.00m),
        new("HP-EQ-KET", "Equipment", 64.00m),
        new("HP-EQ-POV", "Equipment", 34.00m),
        new("HP-EQ-SCL", "Equipment", 42.00m),
        new("HP-EQ-AER", "Equipment", 59.00m),
        new("HP-SB-M01", "Subscriptions", 16.00m),
        new("HP-SB-M03", "Subscriptions", 45.00m),
        new("HP-SB-M12", "Subscriptions", 168.00m),
        new("HP-AC-MUG", "Accessories", 18.00m),
        new("HP-AC-FLT", "Accessories", 8.50m),
        new("HP-AC-TOT", "Accessories", 22.00m),
    ];

    private static readonly (string Name, double Weight)[] Regions =
        [("North America", 42), ("Europe", 30), ("Asia-Pacific", 18), ("Latin America", 10)];

    private static readonly (string Name, double Weight)[] Channels =
        [("Online Store", 50), ("Mobile App", 20), ("Marketplace", 18), ("Wholesale", 12)];

    private static readonly (string Name, double Weight)[] Categories =
        [("Single Origin", 30), ("Blends", 30), ("Equipment", 10), ("Subscriptions", 18), ("Accessories", 12)];

    // Index 0 = January. Holiday peak in November and December.
    private static readonly double[] MonthSeasonality = [1.05, 0.90, 0.95, 0.90, 0.90, 0.85, 0.85, 0.90, 1.00, 1.10, 1.45, 1.70];

    private const int CustomerPoolSize = 12_000;

    private sealed record PendingOrder(DateOnly Date, int Sequence, int CustomerIndex, string Channel, double Discount, OrderStatus Status, Product[] Lines, int[] Quantities);

    public static IReadOnlyList<Order> Generate(SeedOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.OrderLineCount);
        var rng = new Random(options.Seed);

        var days = BuildDayCalendar(options, out var cumulativeWeights);
        var customerRegions = Enumerable.Range(0, CustomerPoolSize)
            .Select(_ => PickWeighted(rng, Regions))
            .ToArray();

        var pending = new List<PendingOrder>(options.OrderLineCount / 2);
        var linesRemaining = options.OrderLineCount;
        var sequence = 0;
        while (linesRemaining > 0)
        {
            var date = days[PickDayIndex(rng, cumulativeWeights)];
            var customerIndex = (int)(CustomerPoolSize * Math.Pow(rng.NextDouble(), 2));
            var channel = PickWeighted(rng, Channels);
            var lineCount = Math.Min(PickLineCount(rng), linesRemaining);
            var lines = PickProducts(rng, lineCount, date.Month);
            var quantities = lines.Select(p => PickQuantity(rng, channel, p.Category)).ToArray();
            pending.Add(new PendingOrder(date, sequence++, customerIndex, channel, PickDiscount(rng, date.Month), PickStatus(rng), lines, quantities));
            linesRemaining -= lineCount;
        }

        // Order ids ascend with time; the generation sequence breaks ties deterministically.
        var ordered = pending.OrderBy(o => o.Date).ThenBy(o => o.Sequence).ToList();
        var result = new List<Order>(options.OrderLineCount);
        for (var i = 0; i < ordered.Count; i++)
        {
            var o = ordered[i];
            var orderId = $"HP-{i + 1:D6}";
            var customerId = $"C-{o.CustomerIndex + 1:D5}";
            var region = customerRegions[o.CustomerIndex];
            for (var l = 0; l < o.Lines.Length; l++)
            {
                result.Add(new Order
                {
                    OrderId = orderId,
                    OrderDate = o.Date,
                    CustomerId = customerId,
                    Region = region,
                    Channel = o.Channel,
                    ProductSku = o.Lines[l].Sku,
                    ProductCategory = o.Lines[l].Category,
                    Quantity = o.Quantities[l],
                    UnitPrice = o.Lines[l].UnitPrice,
                    Discount = (decimal)o.Discount,
                    Status = o.Status,
                });
            }
        }

        return result;
    }

    private static DateOnly[] BuildDayCalendar(SeedOptions options, out double[] cumulativeWeights)
    {
        var start = options.StartDate;
        var totalDays = options.EndDate.DayNumber - start.DayNumber + 1;
        var days = new DateOnly[totalDays];
        cumulativeWeights = new double[totalDays];
        var running = 0.0;
        for (var i = 0; i < totalDays; i++)
        {
            var date = start.AddDays(i);
            days[i] = date;
            var growth = 1.0 + 0.35 * i / totalDays;
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.1 : 1.0;
            running += MonthSeasonality[date.Month - 1] * growth * weekend;
            cumulativeWeights[i] = running;
        }

        return days;
    }

    private static int PickDayIndex(Random rng, double[] cumulative)
    {
        var target = rng.NextDouble() * cumulative[^1];
        var index = Array.BinarySearch(cumulative, target);
        return index >= 0 ? index : ~index;
    }

    private static string PickWeighted(Random rng, (string Name, double Weight)[] options)
    {
        var target = rng.NextDouble() * options.Sum(o => o.Weight);
        foreach (var (name, weight) in options)
        {
            target -= weight;
            if (target < 0) return name;
        }

        return options[^1].Name;
    }

    private static int PickLineCount(Random rng) => rng.NextDouble() switch
    {
        < 0.55 => 1,
        < 0.83 => 2,
        < 0.95 => 3,
        _ => 4,
    };

    private static Product[] PickProducts(Random rng, int count, int month)
    {
        var holiday = month is 11 or 12;
        var picked = new List<Product>(count);
        while (picked.Count < count)
        {
            var weights = Categories
                .Select(c => (c.Name, holiday && c.Name is "Equipment" or "Accessories" ? c.Weight * 2 : c.Weight))
                .ToArray();
            var category = PickWeighted(rng, weights);
            var candidates = Catalog.Where(p => p.Category == category && !picked.Contains(p)).ToArray();
            if (candidates.Length == 0) continue;
            picked.Add(candidates[rng.Next(candidates.Length)]);
        }

        return picked.ToArray();
    }

    private static int PickQuantity(Random rng, string channel, string category)
    {
        if (channel == "Wholesale" && category is "Single Origin" or "Blends")
        {
            return rng.Next(5, 25);
        }

        return rng.NextDouble() switch
        {
            < 0.70 => 1,
            < 0.92 => 2,
            _ => 3,
        };
    }

    private static double PickDiscount(Random rng, int month)
    {
        var roll = rng.NextDouble();
        if (month is 11 or 12)
        {
            return roll switch { < 0.50 => 0.0, < 0.70 => 0.10, < 0.90 => 0.15, _ => 0.20 };
        }

        return roll switch { < 0.80 => 0.0, < 0.92 => 0.05, < 0.98 => 0.10, _ => 0.15 };
    }

    private static OrderStatus PickStatus(Random rng) => rng.NextDouble() switch
    {
        < 0.925 => OrderStatus.Completed,
        < 0.97 => OrderStatus.Refunded,
        _ => OrderStatus.Cancelled,
    };
}
