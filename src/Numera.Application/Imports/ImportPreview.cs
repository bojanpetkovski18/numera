namespace Numera.Application.Imports;

public static class ImportPreviewLimits
{
    /// <summary>Data rows returned by a preview; enough to recognise the columns, never the whole file.</summary>
    public const int MaxRows = 20;

    /// <summary>Files with more columns are refused by the preview (an order export has a dozen or so).</summary>
    public const int MaxColumns = 100;

    /// <summary>Longer cells are cut in the preview only; the import reads them in full.</summary>
    public const int MaxCellLength = 200;
}

/// <summary>A suggested or saved mapping, in the same shape the upload accepts.</summary>
public sealed record SuggestedMapping(IReadOnlyDictionary<string, string?> Columns, string DateFormat, string Discount);

/// <summary>
/// What a file looks like and how it could be mapped. <see cref="MatchesNumeraFormat"/> is true when the file
/// already uses Numera's column names, so it can be imported as it is.
/// </summary>
public sealed record ImportPreview(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    bool MatchesNumeraFormat,
    SuggestedMapping Suggestion,
    bool DateFormatAmbiguous,
    IReadOnlyList<string> Unmapped,
    SavedMappingView? SavedMapping)
{
    /// <summary>
    /// Builds the preview: a saved mapping for the same headers wins over the automatic suggestion. Fields whose
    /// suggested column is not in the file are left unmapped.
    /// </summary>
    public static ImportPreview Create(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, SavedMappingView? saved)
    {
        var auto = MappingSuggester.Suggest(headers, rows);
        var present = headers.Select(HeaderEvaluation.Clean).ToHashSet(StringComparer.OrdinalIgnoreCase);

        SuggestedMapping suggestion;
        var ambiguous = auto.DateFormatAmbiguous;
        if (saved is not null)
        {
            var columns = ImportColumns.Required.ToDictionary(
                f => f,
                f => saved.Mapping.Columns is { } c && c.TryGetValue(f, out var s) && s is not null && present.Contains(s) ? s : null);
            suggestion = new SuggestedMapping(columns, saved.Mapping.DateFormat ?? DateFormats.Iso, saved.Mapping.Discount ?? "fraction");
            ambiguous = false; // the user chose it before
        }
        else
        {
            suggestion = new SuggestedMapping(auto.Columns, auto.DateFormat, auto.Discount == DiscountFormat.Percent ? "percent" : "fraction");
        }

        var unmapped = ImportColumns.Required.Where(f => suggestion.Columns[f] is null).ToList();
        var matches = HeaderEvaluation.Evaluate(headers.ToList<string?>()).IsValid;
        return new ImportPreview(headers, rows, matches, suggestion, ambiguous, unmapped, saved);
    }
}
