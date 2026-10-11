namespace Numera.Application.Entities;

public enum ImportJobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

public class ImportJob
{
    public Guid Id { get; set; }
    public required string FileName { get; init; }
    public ImportJobStatus Status { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int RejectedRows { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}
