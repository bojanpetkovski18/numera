using System.Globalization;

namespace Numera.Application.Imports;

/// <summary>A suggested mapping: a source column per field (null when nothing fits) and the value formats.</summary>
public sealed record MappingSuggestion(
    IReadOnlyDictionary<string, string?> Columns,
    string DateFormat,
    bool DateFormatAmbiguous,
    DiscountFormat Discount);

/// <summary>
/// Suggests how to map a file from its header and a few sample rows. Only a suggestion: the user confirms it, and
/// the import validates every row whatever was suggested.
/// </summary>
public static class MappingSuggester
{
    /// <summary>
    /// Names that usually mean each field, compared after lower-casing and dropping everything but letters and digits
    /// ("Order Date", "order-date" and "ORDER_DATE" are all "orderdate"). Earlier names win.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Synonyms = new Dictionary<string, string[]>
    {
        [ImportColumns.OrderId] = ["orderid", "ordernumber", "orderno", "ordernr", "order", "invoiceid", "invoicenumber", "invoiceno", "invoice", "transactionid"],
        [ImportColumns.OrderDate] = ["orderdate", "date", "orderedon", "orderedat", "purchasedate", "saledate", "salesdate", "transactiondate", "invoicedate"],
        [ImportColumns.CustomerId] = ["customerid", "customernumber", "customerno", "customer", "clientid", "client", "buyerid", "buyer", "accountid", "account"],
        [ImportColumns.Region] = ["region", "salesregion", "territory", "area", "market"],
        [ImportColumns.Channel] = ["channel", "saleschannel", "source", "platform", "storetype"],
        [ImportColumns.ProductSku] = ["productsku", "sku", "productcode", "itemcode", "itemsku", "productid", "itemid", "item", "product"],
        [ImportColumns.ProductCategory] = ["productcategory", "category", "productgroup", "productline", "itemcategory", "producttype", "group"],
        [ImportColumns.Quantity] = ["quantity", "qty", "units", "unitssold", "quantityordered", "qtyordered", "count"],
        [ImportColumns.UnitPrice] = ["unitprice", "price", "priceperunit", "itemprice", "unitcost", "rate"],
        [ImportColumns.Discount] = ["discount", "discountpercent", "discountpct", "discountrate", "disc"],
        [ImportColumns.Status] = ["status", "orderstatus", "state", "paymentstatus"],
    };

    public static MappingSuggestion Suggest(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var columns = SuggestColumns(headers);
        IEnumerable<string> Samples(string field)
        {
            var source = columns[field];
            var index = source is null ? -1 : headers.ToList().FindIndex(h => string.Equals(HeaderEvaluation.Clean(h), source, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? [] : rows.Where(r => index < r.Count).Select(r => r[index]);
        }

        var (dateFormat, ambiguous) = SuggestDateFormat(Samples(ImportColumns.OrderDate));
        return new MappingSuggestion(columns, dateFormat, ambiguous, SuggestDiscount(Samples(ImportColumns.Discount)));
    }

    /// <summary>
    /// For each field, the first header (in file order) whose normalised name is the field's best-ranked synonym
    /// still available. A header is suggested for one field at most; fields are filled in order of how exact the
    /// match is, so "Date" goes to order_date before anything else can claim it.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> SuggestColumns(IReadOnlyList<string> headers)
    {
        var names = headers.Select(h => (Original: HeaderEvaluation.Clean(h), Key: Normalise(h))).ToList();
        var result = ImportColumns.Required.ToDictionary(f => f, _ => (string?)null);
        var taken = new HashSet<int>();

        var maxRank = Synonyms.Values.Max(s => s.Length);
        for (var rank = 0; rank < maxRank; rank++)
        {
            foreach (var field in ImportColumns.Required)
            {
                if (result[field] is not null || rank >= Synonyms[field].Length)
                {
                    continue;
                }

                var index = names.FindIndex(n => n.Key == Synonyms[field][rank]);
                if (index >= 0 && taken.Add(index))
                {
                    result[field] = names[index].Original;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The first allowed date format that reads every non-empty sample. Ambiguous when another format reads them all
    /// too (for example 03/04/2025 as dd/MM or MM/dd). Numera's own format when the samples fit none.
    /// </summary>
    public static (string Format, bool Ambiguous) SuggestDateFormat(IEnumerable<string?> samples)
    {
        var values = samples.Select(s => s?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
        if (values.Count == 0)
        {
            return (DateFormats.Iso, false);
        }

        var fitting = DateFormats.Allowed.Where(f => values.All(v => DateFormats.TryParse(v, f, out _))).ToList();
        return fitting.Count == 0 ? (DateFormats.Iso, false) : (fitting[0], fitting.Count > 1);
    }

    /// <summary>Percent when a sample has a % sign or is above 1 (no fraction can be); otherwise a fraction.</summary>
    public static DiscountFormat SuggestDiscount(IEnumerable<string?> samples)
    {
        foreach (var sample in samples)
        {
            var text = sample?.Trim() ?? string.Empty;
            if (text.EndsWith('%'))
            {
                return DiscountFormat.Percent;
            }

            if (decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 1)
            {
                return DiscountFormat.Percent;
            }
        }

        return DiscountFormat.Fraction;
    }

    private static string Normalise(string header) =>
        new(HeaderEvaluation.Clean(header).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
