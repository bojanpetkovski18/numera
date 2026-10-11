using Numera.Application.Entities;
using Numera.Application.Metrics;
using Numera.Application.Orders;

namespace Numera.UnitTests;

public class OrdersExportFormatTests
{
    private static readonly OrderRow Sample = new(
        1, "HP-000123", new DateOnly(2025, 3, 31), "C-00042", "Europe", "Online Store", "HP-SO-ETH", "Single Origin",
        3, 18.5m, 0.10m, 49.95m, "completed");

    [Fact]
    public void The_header_is_the_import_columns_plus_the_line_total()
    {
        Assert.Equal(
            "order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status,line_total",
            OrdersCsv.Header);
    }

    [Fact]
    public void Every_row_has_one_cell_per_header_column_in_the_same_order()
    {
        var header = OrdersCsv.Header.Split(',');
        var cells = OrdersCsv.Row(Sample).TrimEnd('\r', '\n').Split(',');

        Assert.Equal(header.Length, cells.Length);
        Assert.Equal("completed", cells[Array.IndexOf(header, "status")]);
        Assert.Equal("49.95", cells[Array.IndexOf(header, "line_total")]);
        Assert.Equal("2025-03-31", cells[Array.IndexOf(header, "order_date")]);
        Assert.Equal("0.1", cells[Array.IndexOf(header, "discount")]);
    }

    [Fact]
    public void A_row_uses_iso_dates_and_plain_invariant_numbers_and_ends_with_crlf()
    {
        Assert.Equal(
            "HP-000123,2025-03-31,C-00042,Europe,Online Store,HP-SO-ETH,Single Origin,3,18.50,0.1,completed,49.95\r\n",
            OrdersCsv.Row(Sample));
    }

    [Fact]
    public void Numbers_do_not_follow_the_server_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains(",18.50,0.1,completed,49.95\r\n", OrdersCsv.Row(Sample with { }));
            Assert.Contains(",1234.50,", OrdersCsv.Row(Sample with { UnitPrice = 1234.5m }));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Text_cells_are_quoted_and_formula_text_is_neutralized()
    {
        var row = OrdersCsv.Row(Sample with { OrderId = "=HYPERLINK(\"x\")", CustomerId = "+1", Region = "North, East", ProductSku = "@cmd", Channel = "-x" });

        Assert.StartsWith("\"'=HYPERLINK(\"\"x\"\")\",2025-03-31,'+1,\"North, East\",'-x,'@cmd,", row);
    }

    [Fact]
    public void A_zero_discount_is_written_as_0()
    {
        Assert.Contains(",0,", OrdersCsv.Row(Sample with { Discount = 0m }));
    }

    private static OrdersQuery Query(OrderFilter filter, string? search = null) =>
        new(filter, search, OrderSortField.OrderDate, true, 1, 25);

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 30, 45, TimeSpan.FromHours(2));

    [Fact]
    public void The_file_name_has_a_utc_timestamp_and_says_all_when_unfiltered()
    {
        Assert.Equal("numera-orders_20261012-133045Z_all.csv", ExportFileName.For(Query(OrderFilter.None), Now));
    }

    [Fact]
    public void The_file_name_describes_the_filters()
    {
        var filter = OrderFilter.None with
        {
            From = new DateOnly(2025, 3, 31),
            To = new DateOnly(2025, 4, 6),
            Regions = ["Europe", "Asia-Pacific"],
            Channels = ["Online Store"],
            Statuses = [OrderStatus.Completed],
        };

        Assert.Equal(
            "numera-orders_20261012-133045Z_2025-03-31-to-2025-04-06_europe_asia-pacific_online-store_completed_id-hp-01.csv",
            ExportFileName.For(Query(filter, "HP-01"), Now));
    }

    [Theory]
    [InlineData("2025-01-15", null, "from-2025-01-15")]
    [InlineData(null, "2025-01-15", "to-2025-01-15")]
    [InlineData("2025-01-15", "2025-01-15", "2025-01-15")]
    public void Open_and_single_day_ranges_read_naturally(string? from, string? to, string expected)
    {
        var filter = OrderFilter.None with
        {
            From = from is null ? null : DateOnly.Parse(from),
            To = to is null ? null : DateOnly.Parse(to),
        };

        Assert.Equal($"numera-orders_20261012-133045Z_{expected}.csv", ExportFileName.For(Query(filter), Now));
    }

    [Fact]
    public void Unsafe_characters_never_reach_the_file_name_and_long_descriptions_are_cut()
    {
        var filter = OrderFilter.None with { Categories = ["../../etc/passwd", "Ünïcode \"quoted\"; name", new string('x', 200)] };

        var name = ExportFileName.For(Query(filter), Now);

        Assert.Matches("^numera-orders_20261012-133045Z_[a-z0-9_-]+\\.csv$", name);
        Assert.StartsWith("numera-orders_20261012-133045Z_etc-passwd_n-code-quoted-name_xxx", name);
        Assert.True(name.Length <= "numera-orders_20261012-133045Z_".Length + 80 + ".csv".Length);
    }
}
