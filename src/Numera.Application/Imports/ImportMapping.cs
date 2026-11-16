using System.Globalization;

namespace Numera.Application.Imports;

public enum DiscountFormat
{
    /// <summary>0.10 means 10% off (Numera's own format).</summary>
    Fraction,

    /// <summary>10 or 10% means 10% off.</summary>
    Percent,
}

/// <summary>How the values of a file are written: the date format and the discount scale.</summary>
public sealed record ValueFormats(string DateFormat, DiscountFormat Discount)
{
    /// <summary>Numera's own format, used when a file is imported without a mapping.</summary>
    public static readonly ValueFormats Standard = new(DateFormats.Iso, DiscountFormat.Fraction);
}

/// <summary>The date formats a mapping can choose. Day and month may have one or two digits, except in the ISO format.</summary>
public static class DateFormats
{
    public const string Iso = "yyyy-MM-dd";

    public static readonly IReadOnlyList<string> Allowed = [Iso, "dd/MM/yyyy", "MM/dd/yyyy", "dd.MM.yyyy"];

    public static bool TryParse(string text, string format, out DateOnly date) =>
        DateOnly.TryParseExact(text, Patterns(format), CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string[] Patterns(string format) => format switch
    {
        "dd/MM/yyyy" => ["d/M/yyyy"],
        "MM/dd/yyyy" => ["M/d/yyyy"],
        "dd.MM.yyyy" => ["d.M.yyyy"],
        _ => [Iso],
    };
}

/// <summary>
/// Which source column feeds each Numera field, and how values are written. Every Numera field is mapped, and each
/// source column feeds at most one field. Column names are matched case-insensitively after trimming.
/// </summary>
public sealed record ImportMapping(IReadOnlyDictionary<string, string> Columns, ValueFormats Formats)
{
    public const int MaxSourceNameLength = 100;

    /// <summary>The source column mapped to a Numera field.</summary>
    public string SourceFor(string field) => Columns[field];

    /// <summary>
    /// Builds a mapping from untrusted input (a form field or a saved mapping request). Returns null and fills
    /// <paramref name="errors"/> under the key "mapping" when it is not usable.
    /// </summary>
    public static ImportMapping? TryCreate(ImportMappingInput? input, Dictionary<string, string[]> errors)
    {
        var problems = new List<string>();
        if (input?.Columns is null)
        {
            errors["mapping"] = ["The mapping must name a source column for every field."];
            return null;
        }

        var columns = new Dictionary<string, string>();
        foreach (var (field, source) in input.Columns)
        {
            var name = field.Trim().ToLowerInvariant();
            if (!ImportColumns.Required.Contains(name))
            {
                problems.Add($"Unknown field '{Shorten(field)}'.");
                continue;
            }

            var column = source?.Trim() ?? string.Empty;
            if (column.Length > MaxSourceNameLength)
            {
                problems.Add($"The column name for {name} must be at most {MaxSourceNameLength} characters.");
            }
            else if (column.Length > 0)
            {
                columns[name] = column;
            }
        }

        var unmapped = ImportColumns.Required.Where(f => !columns.ContainsKey(f)).ToList();
        if (unmapped.Count > 0)
        {
            problems.Add($"Choose a column for: {string.Join(", ", unmapped)}.");
        }

        var reused = columns.Values
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}'")
            .ToList();
        if (reused.Count > 0)
        {
            problems.Add($"Each column can feed only one field; used more than once: {string.Join(", ", reused)}.");
        }

        var dateFormat = string.IsNullOrWhiteSpace(input.DateFormat) ? DateFormats.Iso : input.DateFormat.Trim();
        if (!DateFormats.Allowed.Contains(dateFormat))
        {
            problems.Add($"dateFormat must be one of: {string.Join(", ", DateFormats.Allowed)}.");
        }

        DiscountFormat discount = DiscountFormat.Fraction;
        if (!string.IsNullOrWhiteSpace(input.Discount) && !TryParseDiscount(input.Discount, out discount))
        {
            problems.Add("discount must be one of: fraction, percent.");
        }

        if (problems.Count > 0)
        {
            errors["mapping"] = [.. problems];
            return null;
        }

        return new ImportMapping(columns, new ValueFormats(dateFormat, discount));
    }

    public ImportMappingInput ToInput() =>
        new(new Dictionary<string, string?>(Columns.Select(c => KeyValuePair.Create(c.Key, (string?)c.Value))),
            Formats.DateFormat, Formats.Discount == DiscountFormat.Percent ? "percent" : "fraction");

    private static bool TryParseDiscount(string value, out DiscountFormat format)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "fraction": format = DiscountFormat.Fraction; return true;
            case "percent": format = DiscountFormat.Percent; return true;
            default: format = DiscountFormat.Fraction; return false;
        }
    }

    private static string Shorten(string value) => value.Length <= 40 ? value : value[..40] + "…";
}

/// <summary>A mapping exactly as it arrives in JSON: <c>{ "columns": { "order_id": "Order No", … }, "dateFormat": "dd/MM/yyyy", "discount": "percent" }</c>.</summary>
public sealed record ImportMappingInput(Dictionary<string, string?>? Columns, string? DateFormat, string? Discount);
