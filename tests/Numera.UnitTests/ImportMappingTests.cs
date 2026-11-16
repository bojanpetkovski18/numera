using Numera.Application.Entities;
using Numera.Application.Imports;

namespace Numera.UnitTests;

public class ImportMappingTests
{
    /// <summary>The column names of the second sample file (web/public/sample-orders-mapped.csv).</summary>
    private static readonly string[] SampleHeaders =
        ["Order Number", "Date", "Customer", "Sales Region", "Sales Channel", "SKU", "Category", "Qty", "Price", "Discount %", "Order Status", "Notes"];

    private static Dictionary<string, string?> SampleColumns() => new()
    {
        ["order_id"] = "Order Number",
        ["order_date"] = "Date",
        ["customer_id"] = "Customer",
        ["region"] = "Sales Region",
        ["channel"] = "Sales Channel",
        ["product_sku"] = "SKU",
        ["product_category"] = "Category",
        ["quantity"] = "Qty",
        ["unit_price"] = "Price",
        ["discount"] = "Discount %",
        ["status"] = "Order Status",
    };

    private static ImportMapping Create(ImportMappingInput input, out Dictionary<string, string[]> errors)
    {
        errors = [];
        return ImportMapping.TryCreate(input, errors)!;
    }

    // building a mapping -------------------------------------------------------------------------------

    [Fact]
    public void A_complete_mapping_is_accepted_with_its_formats()
    {
        var mapping = Create(new ImportMappingInput(SampleColumns(), "dd/MM/yyyy", "percent"), out var errors);

        Assert.Empty(errors);
        Assert.Equal("Qty", mapping.SourceFor("quantity"));
        Assert.Equal(new ValueFormats("dd/MM/yyyy", DiscountFormat.Percent), mapping.Formats);
    }

    [Fact]
    public void Formats_default_to_numeras_own_and_field_names_ignore_case()
    {
        var columns = SampleColumns().ToDictionary(c => c.Key.ToUpperInvariant(), c => c.Value);

        var mapping = Create(new ImportMappingInput(columns, null, null), out var errors);

        Assert.Empty(errors);
        Assert.Equal(ValueFormats.Standard, mapping.Formats);
    }

    [Fact]
    public void Every_problem_is_reported()
    {
        var columns = SampleColumns();
        columns.Remove("status");
        columns["region"] = "  ";
        columns["channel"] = "qty"; // already used for quantity, in another letter case
        columns["colour"] = "Colour";
        var errors = new Dictionary<string, string[]>();

        var mapping = ImportMapping.TryCreate(new ImportMappingInput(columns, "yyyy/MM/dd", "basis points"), errors);

        Assert.Null(mapping);
        Assert.Equal(
        [
            "Unknown field 'colour'.",
            "Choose a column for: region, status.",
            "Each column can feed only one field; used more than once: 'qty'.",
            "dateFormat must be one of: yyyy-MM-dd, dd/MM/yyyy, MM/dd/yyyy, dd.MM.yyyy.",
            "discount must be one of: fraction, percent.",
        ], errors["mapping"]);
    }

    [Fact]
    public void A_missing_or_overlong_mapping_is_refused()
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(ImportMapping.TryCreate(null, errors));
        Assert.Equal(["The mapping must name a source column for every field."], errors["mapping"]);

        var columns = SampleColumns();
        columns["notes"] = null;
        columns["order_id"] = new string('x', 101);
        errors.Clear();
        Assert.Null(ImportMapping.TryCreate(new ImportMappingInput(columns, null, null), errors));
        Assert.Contains("The column name for order_id must be at most 100 characters.", errors["mapping"]);
    }

    // header matching ----------------------------------------------------------------------------------

    [Fact]
    public void Mapped_headers_are_found_whatever_their_case_and_spacing_and_the_rest_is_ignored_with_a_warning()
    {
        var mapping = Create(new ImportMappingInput(SampleColumns(), null, null), out _);
        var headers = SampleHeaders.Select((h, i) => i == 0 ? "﻿ order number " : h).ToArray();

        var header = HeaderEvaluation.FromMapping(headers, mapping);

        Assert.True(header.IsValid);
        Assert.Equal(0, header.ColumnIndex["order_id"]);
        Assert.Equal(7, header.ColumnIndex["quantity"]);
        Assert.Equal(["Column 'Notes' was not mapped and was ignored."], header.Warnings);
    }

    [Fact]
    public void A_mapped_column_that_is_absent_or_repeated_makes_the_header_invalid()
    {
        var mapping = Create(new ImportMappingInput(SampleColumns(), null, null), out _);
        var headers = SampleHeaders.Where(h => h != "Qty").Append("SKU").ToArray();

        var header = HeaderEvaluation.FromMapping(headers, mapping);

        Assert.False(header.IsValid);
        Assert.Equal(["Qty (for quantity)"], header.Missing);
        Assert.Equal(["SKU"], header.Duplicated);
    }

    // value formats --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("yyyy-MM-dd", "2025-03-04", "2025-03-04")]
    [InlineData("dd/MM/yyyy", "04/03/2025", "2025-03-04")]
    [InlineData("dd/MM/yyyy", "4/3/2025", "2025-03-04")]
    [InlineData("MM/dd/yyyy", "03/04/2025", "2025-03-04")]
    [InlineData("MM/dd/yyyy", "3/4/2025", "2025-03-04")]
    [InlineData("dd.MM.yyyy", "04.03.2025", "2025-03-04")]
    public void Each_date_format_reads_its_dates(string format, string text, string expected)
    {
        Assert.True(DateFormats.TryParse(text, format, out var date));
        Assert.Equal(DateOnly.Parse(expected), date);
    }

    [Theory]
    [InlineData("yyyy-MM-dd", "2025-3-4")] // the ISO format stays strict, as in version 1
    [InlineData("dd/MM/yyyy", "2025-03-04")]
    [InlineData("dd/MM/yyyy", "31/02/2025")]
    [InlineData("MM/dd/yyyy", "13/01/2025")]
    [InlineData("dd.MM.yyyy", "04/03/2025")]
    public void A_date_in_another_format_is_not_read(string format, string text) =>
        Assert.False(DateFormats.TryParse(text, format, out _));

    private static RawOrderRow Row(string date = "04/03/2025", string discount = "10") =>
        new("A-1", date, "C-1", "Europe", "Online Store", "S1", "Blends", "2", "10.00", discount, "completed");

    private static readonly DateOnly Today = new(2026, 11, 16);

    [Theory]
    [InlineData("10", "0.10")]
    [InlineData("10%", "0.10")]
    [InlineData(" 12.5 % ", "0.125")]
    [InlineData("0", "0")]
    [InlineData("100", "1")]
    public void A_percentage_discount_is_stored_as_a_fraction(string discount, string expected)
    {
        var result = OrderRowValidator.Validate(Row(discount: discount), Today, new ValueFormats("dd/MM/yyyy", DiscountFormat.Percent));

        Assert.True(result.IsValid);
        Assert.Equal(decimal.Parse(expected), result.Row!.Discount);
        Assert.Equal(new DateOnly(2025, 3, 4), result.Row.OrderDate);
        Assert.Equal(OrderStatus.Completed, result.Row.Status);
    }

    [Theory]
    [InlineData("101", "discount must be a percentage between 0 and 100.")]
    [InlineData("-5", "discount must be a percentage between 0 and 100.")]
    [InlineData("ten", "discount must be a percentage between 0 and 100.")]
    [InlineData("12.25", "discount must have at most 1 decimal place as a percentage.")]
    public void Bad_percentages_are_rejected(string discount, string message)
    {
        var result = OrderRowValidator.Validate(Row(discount: discount), Today, new ValueFormats("dd/MM/yyyy", DiscountFormat.Percent));

        Assert.Equal([new RowError("discount", message)], result.Errors);
    }

    [Fact]
    public void A_date_in_the_wrong_format_names_the_expected_one()
    {
        var result = OrderRowValidator.Validate(Row(date: "2025-03-04"), Today, new ValueFormats("dd/MM/yyyy", DiscountFormat.Percent));

        Assert.Equal([new RowError("order_date", "order_date must be a valid date in dd/MM/yyyy format.")], result.Errors);
    }

    [Fact]
    public void Without_formats_the_version_1_rules_apply()
    {
        Assert.False(OrderRowValidator.Validate(Row(date: "04/03/2025", discount: "0.1"), Today).IsValid);
        Assert.True(OrderRowValidator.Validate(Row(date: "2025-03-04", discount: "0.1"), Today).IsValid);
        Assert.Equal("discount", OrderRowValidator.Validate(Row(date: "2025-03-04", discount: "10"), Today).Errors.Single().Field);
    }

    // suggestions ----------------------------------------------------------------------------------------

    [Fact]
    public void Columns_are_suggested_from_common_names_and_each_column_is_used_once()
    {
        var columns = MappingSuggester.SuggestColumns(SampleHeaders);

        Assert.Equal(SampleColumns(), columns);
    }

    [Fact]
    public void Exact_names_win_over_synonyms_and_unknown_columns_are_left_unmapped()
    {
        var columns = MappingSuggester.SuggestColumns(["DATE", "order_date", "Product", "Weight", "QTY"]);

        Assert.Equal("order_date", columns["order_date"]); // the exact name, not "DATE"
        Assert.Equal("Product", columns["product_sku"]);
        Assert.Equal("QTY", columns["quantity"]);
        Assert.Null(columns["status"]);
        Assert.DoesNotContain("Weight", columns.Values);
    }

    [Theory]
    [InlineData(new[] { "2025-03-04", "2025-12-31" }, "yyyy-MM-dd", false)]
    [InlineData(new[] { "04/03/2025", "25/12/2025" }, "dd/MM/yyyy", false)]
    [InlineData(new[] { "03/04/2025", "12/25/2025" }, "MM/dd/yyyy", false)]
    [InlineData(new[] { "03/04/2025", "05/06/2025" }, "dd/MM/yyyy", true)] // could be either
    [InlineData(new[] { "04.03.2025" }, "dd.MM.yyyy", false)]
    [InlineData(new[] { "March 4" }, "yyyy-MM-dd", false)] // nothing fits: Numera's own, the import will say why
    public void The_date_format_is_suggested_from_the_samples(string[] samples, string format, bool ambiguous) =>
        Assert.Equal((format, ambiguous), MappingSuggester.SuggestDateFormat(samples));

    [Theory]
    [InlineData(new[] { "0", "0.05", "0.1" }, DiscountFormat.Fraction)]
    [InlineData(new[] { "0", "5", "10" }, DiscountFormat.Percent)]
    [InlineData(new[] { "0", "5%" }, DiscountFormat.Percent)]
    [InlineData(new string[0], DiscountFormat.Fraction)]
    public void The_discount_scale_is_suggested_from_the_samples(string[] samples, DiscountFormat expected) =>
        Assert.Equal(expected, MappingSuggester.SuggestDiscount(samples));

    [Fact]
    public void A_preview_prefers_a_saved_mapping_and_reports_what_is_left_unmapped()
    {
        string[][] rows = [["SAMPLE-0001", "01/03/2025", "C-1", "Europe", "Online Store", "S1", "Blends", "2", "18.50", "5", "completed", ""]];

        var auto = ImportPreview.Create(SampleHeaders, rows, null);
        Assert.False(auto.MatchesNumeraFormat);
        Assert.Equal(("dd/MM/yyyy", "percent", true), (auto.Suggestion.DateFormat, auto.Suggestion.Discount, auto.DateFormatAmbiguous));
        Assert.Empty(auto.Unmapped);

        var savedColumns = SampleColumns();
        savedColumns["customer_id"] = "Client"; // not in this file
        var saved = new SavedMappingView(Guid.NewGuid(), "Shop export", new ImportMappingInput(savedColumns, "MM/dd/yyyy", "percent"), [], DateTimeOffset.UnixEpoch);
        var fromSaved = ImportPreview.Create(SampleHeaders, rows, saved);
        Assert.Equal(("MM/dd/yyyy", false), (fromSaved.Suggestion.DateFormat, fromSaved.DateFormatAmbiguous));
        Assert.Equal(["customer_id"], fromSaved.Unmapped);
        Assert.Same(saved, fromSaved.SavedMapping);

        Assert.True(ImportPreview.Create(ImportColumns.Required.ToArray(), [], null).MatchesNumeraFormat);
    }

    [Fact]
    public void Headers_are_compared_as_a_set()
    {
        Assert.Equal(["a", "b c"], SavedMappingsHandler.HeaderKey([" B C", "a", "", "A"]));
    }
}
