using Numera.Application.Imports;

namespace Numera.Infrastructure.Imports;

public enum ImportRejectionKind
{
    EmptyFile,
    MissingColumns,
    DuplicateColumns,
    NoDataRows,
    TooManyRows,
}

public sealed record ImportRejection(ImportRejectionKind Kind, string Message);

public sealed record ScanResult(HeaderEvaluation? Header, int DataRows, ImportRejection? Rejection);

/// <summary>First pass over an upload: checks the header and counts rows without touching the database.</summary>
public static class ImportFileScanner
{
    public static async Task<ScanResult> ScanAsync(Stream stream, int maxRows = ImportLimits.MaxRows, CancellationToken ct = default)
    {
        await using var reader = await OrderCsvReader.OpenAsync(stream);

        if (reader.Header is null)
        {
            return new ScanResult(null, 0, new ImportRejection(ImportRejectionKind.EmptyFile, "The file is empty."));
        }

        var header = reader.Header;
        if (header.Missing.Count > 0)
        {
            var message = $"The file is missing required columns: {string.Join(", ", header.Missing)}.";
            return new ScanResult(header, 0, new ImportRejection(ImportRejectionKind.MissingColumns, message));
        }

        if (header.Duplicated.Count > 0)
        {
            var message = $"The file repeats columns: {string.Join(", ", header.Duplicated)}.";
            return new ScanResult(header, 0, new ImportRejection(ImportRejectionKind.DuplicateColumns, message));
        }

        var rows = 0;
        await foreach (var _ in reader.ReadRecordsAsync(ct))
        {
            rows++;
            if (rows > maxRows)
            {
                var message = $"The file has more than {maxRows:N0} data rows, which is the import limit.";
                return new ScanResult(header, rows, new ImportRejection(ImportRejectionKind.TooManyRows, message));
            }
        }

        return rows == 0
            ? new ScanResult(header, 0, new ImportRejection(ImportRejectionKind.NoDataRows, "The file has a header but no data rows."))
            : new ScanResult(header, rows, null);
    }
}