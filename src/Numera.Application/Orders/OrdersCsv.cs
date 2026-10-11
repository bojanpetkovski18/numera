using System.Globalization;
using System.Text;
using Numera.Application.Csv;
using Numera.Application.Imports;

namespace Numera.Application.Orders;

/// <summary>
/// The export file format. Columns are the import columns in the same order, so an export can be imported again
/// (the extra <c>line_total</c> column is ignored by the importer with a warning), followed by the line total.
/// Dates are ISO (yyyy-MM-dd); numbers are plain invariant decimals with no thousands separators or currency.
/// </summary>
public static class OrdersCsv
{
    public const string LineTotal = "line_total";

    public static readonly string Header = string.Join(',', ImportColumns.Required.Append(LineTotal));

    /// <summary>The row ends with CRLF (RFC 4180).</summary>
    public static string Row(OrderRow row)
    {
        var line = new StringBuilder(128);
        line.Append(CsvCell.Escape(row.OrderId)).Append(',')
            .Append(row.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
            .Append(CsvCell.Escape(row.CustomerId)).Append(',')
            .Append(CsvCell.Escape(row.Region)).Append(',')
            .Append(CsvCell.Escape(row.Channel)).Append(',')
            .Append(CsvCell.Escape(row.ProductSku)).Append(',')
            .Append(CsvCell.Escape(row.ProductCategory)).Append(',')
            .Append(row.Quantity.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(row.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
            .Append(Plain(row.Discount)).Append(',')
            .Append(CsvCell.Escape(row.Status)).Append(',')
            .Append(row.LineTotal.ToString("0.00", CultureInfo.InvariantCulture))
            .Append("\r\n");
        return line.ToString();
    }

    /// <summary>0.05 stays 0.05, 0.10 becomes 0.1, 0 becomes 0: the value as stored, without padding zeros.</summary>
    private static string Plain(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);
}

/// <summary>
/// Download name: <c>numera-orders_{UTC timestamp}_{filters}.csv</c>, for example
/// <c>numera-orders_20261012-153045Z_2025-03-31-to-2025-04-06_europe_completed.csv</c>.
/// Only lowercase letters, digits and dashes, so it is safe on every file system and in a header.
/// </summary>
public static class ExportFileName
{
    private const int MaxDescriptionLength = 80;

    public static string For(OrdersQuery query, DateTimeOffset now)
    {
        var parts = new List<string>();
        var filter = query.Filter;
        if (filter.From is { } from && filter.To is { } to)
        {
            parts.Add(from == to ? Iso(from) : $"{Iso(from)}-to-{Iso(to)}");
        }
        else if (filter.From is { } onlyFrom)
        {
            parts.Add($"from-{Iso(onlyFrom)}");
        }
        else if (filter.To is { } onlyTo)
        {
            parts.Add($"to-{Iso(onlyTo)}");
        }

        parts.AddRange(filter.Regions.Concat(filter.Channels).Concat(filter.Categories).Select(Slug));
        parts.AddRange(filter.Statuses.Select(s => s.ToString().ToLowerInvariant()));
        if (query.Search is { } search)
        {
            parts.Add("id-" + Slug(search));
        }

        var description = string.Join('_', parts.Where(p => p.Length > 0));
        if (description.Length == 0)
        {
            description = "all";
        }
        else if (description.Length > MaxDescriptionLength)
        {
            description = description[..MaxDescriptionLength].TrimEnd('-', '_');
        }

        var stamp = now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z";
        return $"numera-orders_{stamp}_{description}.csv";
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Slug(string value)
    {
        var slug = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }
        return slug.ToString().TrimEnd('-');
    }
}
