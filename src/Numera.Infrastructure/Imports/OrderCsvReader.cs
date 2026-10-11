using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Numera.Application.Imports;

namespace Numera.Infrastructure.Imports;

/// <summary>
/// One data row. <see cref="Row"/> is null when the row is structurally unusable
/// (wrong column count, malformed quoting); <see cref="StructuralError"/> then says why.
/// <see cref="RowNumber"/> is the 1-based record number with the header as row 1.
/// </summary>
public sealed record CsvRecord(int RowNumber, RawOrderRow? Row, string? StructuralError, string RawRecord);

/// <summary>
/// Streams an order CSV: handles a BOM, CRLF/LF/CR line endings, quoted fields and embedded commas or newlines.
/// Only the current record is held in memory.
/// </summary>
public sealed class OrderCsvReader : IAsyncDisposable
{
    private const int MaxFieldSize = 10_000;

    private readonly StreamReader _reader;
    private readonly CsvParser _parser;
    private bool _badData;

    /// <summary>The evaluated header, or null when the file has no content at all.</summary>
    public HeaderEvaluation? Header { get; private set; }

    private OrderCsvReader(Stream stream)
    {
        _reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            Delimiter = ",",
            MaxFieldSize = MaxFieldSize,
            BadDataFound = _ => _badData = true,
        };
        _parser = new CsvParser(_reader, configuration, leaveOpen: true);
    }

    /// <summary>Opens the reader and reads the header row. A malformed header leaves <see cref="Header"/> null.</summary>
    public static async Task<OrderCsvReader> OpenAsync(Stream stream)
    {
        var reader = new OrderCsvReader(stream);
        try
        {
            if (await reader._parser.ReadAsync() && reader._parser.Record is { } header)
            {
                reader.Header = HeaderEvaluation.Evaluate(header);
            }
        }
        catch (CsvHelperException)
        {
            // Unparseable header: treated like an empty file by the caller.
        }

        return reader;
    }

    public async IAsyncEnumerable<CsvRecord> ReadRecordsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (Header is null || !Header.IsValid)
        {
            yield break;
        }

        var header = Header;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            _badData = false;

            var (hasRecord, parseError) = await ReadNextAsync();
            if (parseError is not null)
            {
                yield return new CsvRecord(_parser.Row, null, parseError, string.Empty);
                yield break;
            }

            if (!hasRecord)
            {
                yield break;
            }

            var rowNumber = _parser.Row;
            var raw = _parser.RawRecord.TrimEnd('\r', '\n');
            var fields = _parser.Record!;

            if (_badData)
            {
                yield return new CsvRecord(rowNumber, null, "The row has malformed quoting.", raw);
            }
            else if (fields.Length != header.ColumnCount)
            {
                yield return new CsvRecord(
                    rowNumber, null, $"Expected {header.ColumnCount} columns but found {fields.Length}.", raw);
            }
            else
            {
                string? Get(string column) => fields[header.ColumnIndex[column]];
                var row = new RawOrderRow(
                    Get(ImportColumns.OrderId), Get(ImportColumns.OrderDate), Get(ImportColumns.CustomerId),
                    Get(ImportColumns.Region), Get(ImportColumns.Channel), Get(ImportColumns.ProductSku),
                    Get(ImportColumns.ProductCategory), Get(ImportColumns.Quantity), Get(ImportColumns.UnitPrice),
                    Get(ImportColumns.Discount), Get(ImportColumns.Status));
                yield return new CsvRecord(rowNumber, row, null, raw);
            }
        }
    }

    /// <summary>
    /// Counts the remaining data rows without building them, stopping once <paramref name="stopAfter"/> is exceeded.
    /// A row the parser cannot read counts as one row (it is rejected in the load pass).
    /// </summary>
    public async Task<int> CountRecordsAsync(int stopAfter, CancellationToken ct = default)
    {
        if (Header is null || !Header.IsValid)
        {
            return 0;
        }

        var count = 0;
        while (count <= stopAfter)
        {
            ct.ThrowIfCancellationRequested();
            var (hasRecord, parseError) = await ReadNextAsync();
            if (parseError is not null)
            {
                return count + 1;
            }

            if (!hasRecord)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private async Task<(bool HasRecord, string? Error)> ReadNextAsync()
    {
        try
        {
            return (await _parser.ReadAsync(), null);
        }
        catch (CsvHelperException)
        {
            return (false, "The row could not be parsed (for example, a field is too long or a quote is not closed).");
        }
    }

    public ValueTask DisposeAsync()
    {
        _parser.Dispose();
        _reader.Dispose();
        return ValueTask.CompletedTask;
    }
}