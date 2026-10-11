using Microsoft.EntityFrameworkCore;
using Numera.Application.Csv;

namespace Numera.Application.Imports;

public sealed record ImportErrorLine(int RowNumber, string? Field, string Message, string RawRow);

/// <summary>Builds the downloadable error report. One line per <c>import_errors</c> row, in row order.</summary>
public static class ImportErrorReport
{
    public const string Header = "row_number,field,message,raw_row";

    public static async Task WriteAsync(IAsyncEnumerable<ImportErrorLine> lines, TextWriter writer, CancellationToken ct = default)
    {
        await writer.WriteAsync(Header + "\r\n");
        await foreach (var line in lines.WithCancellation(ct))
        {
            var row = string.Join(
                ',',
                line.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CsvCell.Escape(line.Field),
                CsvCell.Escape(line.Message),
                CsvCell.Escape(line.RawRow));
            await writer.WriteAsync(row + "\r\n");
        }
    }
}

public sealed class GetImportErrorsHandler(INumeraDbContext db)
{
    public Task<bool> JobExistsAsync(Guid importId, CancellationToken ct = default) =>
        db.ImportJobs.AnyAsync(j => j.Id == importId, ct);

    public IAsyncEnumerable<ImportErrorLine> StreamAsync(Guid importId) =>
        db.ImportErrors
            .AsNoTracking()
            .Where(e => e.ImportJobId == importId)
            .OrderBy(e => e.RowNumber)
            .ThenBy(e => e.Id)
            .Select(e => new ImportErrorLine(e.RowNumber, e.Field, e.Message, e.RawRow))
            .AsAsyncEnumerable();
}

public sealed class ListImportJobsHandler(INumeraDbContext db)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public async Task<IReadOnlyList<ImportJobSummary>> HandleAsync(int limit = DefaultLimit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit, 1, MaxLimit);
        var jobs = await db.ImportJobs
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
        return jobs.Select(ImportJobSummary.From).ToList();
    }
}