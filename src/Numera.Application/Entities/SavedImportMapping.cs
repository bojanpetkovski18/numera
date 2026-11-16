namespace Numera.Application.Entities;

/// <summary>A column mapping a user saved for files laid out a certain way.</summary>
public class SavedImportMapping
{
    public Guid Id { get; set; }
    public Guid UserId { get; init; }
    public required string Name { get; set; }

    /// <summary>The mapping as JSON (<c>ImportMappingInput</c>), validated before it is stored.</summary>
    public required string MappingJson { get; set; }

    /// <summary>The headers of the file it was made for, cleaned and lower-cased, sorted; used to recognise similar files.</summary>
    public string[] SourceHeaders { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
