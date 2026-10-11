namespace Numera.Application.Entities;

public class ImportError
{
    public long Id { get; set; }
    public required Guid ImportJobId { get; init; }
    public required int RowNumber { get; init; }

    /// <summary>The offending column, or null for row-level errors such as a wrong column count.</summary>
    public string? Field { get; init; }
    public required string Message { get; init; }
    public required string RawRow { get; init; }
}
