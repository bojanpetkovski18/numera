using Numera.Application.Csv;
using Numera.Application.Imports;

namespace Numera.UnitTests;

public class CsvCellTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    public void Cells_starting_with_a_formula_character_are_neutralized(string input, string expected) =>
        Assert.Equal(expected, CsvCell.Neutralize(input));

    [Theory]
    [InlineData("plain")]
    [InlineData("a=b")]
    [InlineData("1-2")]
    [InlineData("")]
    public void Other_cells_are_left_alone(string input) => Assert.Equal(input, CsvCell.Neutralize(input));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData(" padded", "\" padded\"")]
    [InlineData("", "")]
    public void Cells_are_quoted_only_when_needed(string input, string expected) =>
        Assert.Equal(expected, CsvCell.Escape(input));

    [Fact]
    public void Null_becomes_an_empty_cell() => Assert.Equal(string.Empty, CsvCell.Escape(null));

    [Fact]
    public void A_dangerous_cell_that_also_needs_quoting_is_both_prefixed_and_quoted() =>
        Assert.Equal("\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\"", CsvCell.Escape("=HYPERLINK(\"x\",\"y\")"));

    [Fact]
    public async Task The_error_report_has_a_header_one_line_per_error_and_neutralized_text()
    {
        async IAsyncEnumerable<ImportErrorLine> Lines()
        {
            yield return new ImportErrorLine(4, "quantity", "quantity must be greater than 0.", "A-1,2025-01-01,0");
            yield return new ImportErrorLine(9, null, "Expected 11 columns but found 3.", "=cmd|' /C calc'!A0,x");
            await Task.CompletedTask;
        }

        var writer = new StringWriter();
        await ImportErrorReport.WriteAsync(Lines(), writer);

        Assert.Equal(
            "row_number,field,message,raw_row\r\n" +
            "4,quantity,quantity must be greater than 0.,\"A-1,2025-01-01,0\"\r\n" +
            "9,,Expected 11 columns but found 3.,\"'=cmd|' /C calc'!A0,x\"\r\n",
            writer.ToString());
    }
}