using System.Text;
using Numera.Application.Imports;
using Numera.Infrastructure.Imports;

namespace Numera.UnitTests;

public class ImportCsvTests
{
    private const string Header =
        "order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status";

    private const string Row = "A-1,2025-01-01,C-1,Europe,Online Store,SKU-A,Blends,1,14.00,0,completed";

    private static MemoryStream Stream(string text, bool bom = false)
    {
        var bytes = new UTF8Encoding(false).GetBytes(text);
        if (bom)
        {
            bytes = [0xEF, 0xBB, 0xBF, .. bytes];
        }

        return new MemoryStream(bytes);
    }

    private static async Task<List<CsvRecord>> ReadAllAsync(string text, bool bom = false)
    {
        await using var reader = await OrderCsvReader.OpenAsync(Stream(text, bom));
        var records = new List<CsvRecord>();
        await foreach (var record in reader.ReadRecordsAsync())
        {
            records.Add(record);
        }

        return records;
    }

    // header evaluation
    [Fact]
    public void Header_matching_is_case_insensitive_and_trims_whitespace()
    {
        var headers = ImportColumns.Required.Select(c => $"  {c.ToUpperInvariant()} ").ToList();

        var result = HeaderEvaluation.Evaluate(headers);

        Assert.True(result.IsValid);
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Missing_required_columns_are_reported_by_name()
    {
        var headers = ImportColumns.Required.Where(c => c is not ("unit_price" or "status")).ToList();

        var result = HeaderEvaluation.Evaluate(headers);

        Assert.False(result.IsValid);
        Assert.Equal(["unit_price", "status"], result.Missing);
    }

    [Fact]
    public void Unknown_columns_are_ignored_with_a_warning()
    {
        var headers = ImportColumns.Required.Append("loyalty_tier").ToList();

        var result = HeaderEvaluation.Evaluate(headers);

        Assert.True(result.IsValid);
        Assert.Equal(["Unknown column 'loyalty_tier' was ignored."], result.Warnings);
    }

    [Fact]
    public void A_repeated_required_column_makes_the_header_invalid()
    {
        var headers = ImportColumns.Required.Append("region").ToList();

        var result = HeaderEvaluation.Evaluate(headers);

        Assert.False(result.IsValid);
        Assert.Equal(["region"], result.Duplicated);
    }

    // scanner
    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        var scan = await ImportFileScanner.ScanAsync(Stream(string.Empty));

        Assert.Equal(ImportRejectionKind.EmptyFile, scan.Rejection?.Kind);
    }

    [Fact]
    public async Task A_header_only_file_is_rejected_as_having_no_data()
    {
        var scan = await ImportFileScanner.ScanAsync(Stream(Header + "\n"));

        Assert.Equal(ImportRejectionKind.NoDataRows, scan.Rejection?.Kind);
    }

    [Fact]
    public async Task A_missing_column_rejects_the_whole_file_with_a_clear_message()
    {
        var text = "order_id,order_date\nA-1,2025-01-01\n";

        var scan = await ImportFileScanner.ScanAsync(Stream(text));

        Assert.Equal(ImportRejectionKind.MissingColumns, scan.Rejection?.Kind);
        Assert.Contains("customer_id", scan.Rejection!.Message);
        Assert.Contains("status", scan.Rejection.Message);
    }

    [Fact]
    public async Task More_rows_than_the_limit_are_rejected()
    {
        var text = Header + "\n" + string.Join("\n", Enumerable.Repeat(Row, 4)) + "\n";

        var scan = await ImportFileScanner.ScanAsync(Stream(text), maxRows: 3);

        Assert.Equal(ImportRejectionKind.TooManyRows, scan.Rejection?.Kind);
    }

    [Fact]
    public async Task Exactly_the_limit_is_accepted()
    {
        var text = Header + "\n" + string.Join("\n", Enumerable.Repeat(Row, 3)) + "\n";

        var scan = await ImportFileScanner.ScanAsync(Stream(text), maxRows: 3);

        Assert.Null(scan.Rejection);
        Assert.Equal(3, scan.DataRows);
    }

    // reader
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task All_line_endings_are_handled(string newline)
    {
        var text = Header + newline + Row + newline + Row.Replace("A-1", "A-2") + newline;

        var records = await ReadAllAsync(text);

        Assert.Equal(2, records.Count);
        Assert.Equal("A-1", records[0].Row!.OrderId);
        Assert.Equal("A-2", records[1].Row!.OrderId);
    }

    [Fact]
    public async Task A_byte_order_mark_is_skipped()
    {
        var records = await ReadAllAsync(Header + "\n" + Row + "\n", bom: true);

        var record = Assert.Single(records);
        Assert.Equal("A-1", record.Row!.OrderId);
    }

    [Fact]
    public async Task Quoted_fields_may_contain_commas_quotes_and_newlines()
    {
        var text = Header + "\n" +
                   "A-1,2025-01-01,C-1,\"Europe, West\",\"Online \"\"Store\"\"\",SKU-A,\"Blends\nHouse\",1,14.00,0,completed\n";

        var record = Assert.Single(await ReadAllAsync(text));

        Assert.Equal("Europe, West", record.Row!.Region);
        Assert.Equal("Online \"Store\"", record.Row.Channel);
        Assert.Equal("Blends\nHouse", record.Row.ProductCategory);
    }

    [Fact]
    public async Task Row_numbers_count_the_header_as_row_one_and_blank_lines_are_skipped()
    {
        var text = Header + "\n" + Row + "\n\n" + Row.Replace("A-1", "A-2") + "\n";

        var records = await ReadAllAsync(text);

        Assert.Equal([2, 4], records.Select(r => r.RowNumber));
    }

    [Fact]
    public async Task A_row_with_the_wrong_number_of_columns_is_flagged_not_fatal()
    {
        var text = Header + "\n" + "A-1,2025-01-01,C-1\n" + Row + "\n";

        var records = await ReadAllAsync(text);

        Assert.Equal(2, records.Count);
        Assert.Null(records[0].Row);
        Assert.Contains("Expected 11 columns but found 3", records[0].StructuralError);
        Assert.NotNull(records[1].Row);
    }

    [Fact]
    public async Task The_raw_record_is_kept_for_the_error_report()
    {
        var records = await ReadAllAsync(Header + "\n" + Row + "\n");

        Assert.Equal(Row, Assert.Single(records).RawRecord);
    }
}