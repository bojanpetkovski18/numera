namespace Numera.Application.Csv;

/// <summary>
/// Writes CSV cells safely: RFC 4180 quoting plus protection against CSV/formula injection.
/// Spreadsheet programs evaluate cells that start with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> (and tab or
/// carriage return), so such text is prefixed with an apostrophe, which they display as plain text.
/// </summary>
public static class CsvCell
{
    public static string Neutralize(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    /// <summary>Neutralizes and, when needed, quotes a text cell.</summary>
    public static string Escape(string? value)
    {
        var text = Neutralize(value ?? string.Empty);
        return NeedsQuoting(text) ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static string Row(params string?[] cells) => string.Join(',', cells.Select(Escape));

    private static bool NeedsQuoting(string text) =>
        text.Length > 0 &&
        (text.AsSpan().IndexOfAny(",\"\r\n") >= 0 || text[0] == ' ' || text[^1] == ' ');
}