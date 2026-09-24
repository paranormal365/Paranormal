using System.Globalization;
using System.Text;

namespace Ben.Service.Models.Store;

/// <summary>
/// A listing's filters, in the address bar and back (storefront S2.1).
/// </summary>
/// <remarks>
/// <para>The address is the state: a filtered listing can be bookmarked, shared, refreshed and
/// stepped back through, and the page and the API read the same keys through this one class —
/// <c>q</c>, <c>price</c>, <c>min</c>, <c>max</c>, <c>opt</c> (repeatable, <c>Colour:Black</c>),
/// <c>rating</c>, <c>instock</c>, <c>sort</c>, <c>page</c>.</para>
///
/// <para>Parsing forgives: an unknown band, a negative price, a rating of 9 or a page of zero is
/// dropped rather than refused. A hand-edited address should show a listing, not an error.
/// Defaults are never written (no <c>sort=popular</c>, no <c>page=1</c>), so one listing has one
/// address.</para>
/// </remarks>
public static class StoreListingQueryString
{
    public const int MaxQueryLength = 100;
    public const int MaxOptions = 12;

    /// <summary>"?q=…&amp;price=…", or empty when nothing is set.</summary>
    public static string From(StoreListingQuery query, bool includePage = true)
    {
        var parts = new List<string>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) parts.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        var q = Normalize(query);
        Add("q", q.Q);
        Add("price", q.PriceBand);
        Add("min", q.PriceMin?.ToString(CultureInfo.InvariantCulture));
        Add("max", q.PriceMax?.ToString(CultureInfo.InvariantCulture));
        foreach (var option in q.Options ?? []) Add("opt", option);
        Add("rating", q.MinRating?.ToString(CultureInfo.InvariantCulture));
        if (q.InStock) Add("instock", "1");
        if (q.Sort != StoreCatalogConstants.Sorts.Popular) Add("sort", q.Sort);
        if (includePage && q.Page > 1) Add("page", q.Page.ToString(CultureInfo.InvariantCulture));

        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    /// <summary>Reads a query string ("?a=b&amp;c=d" or without the "?").</summary>
    public static StoreListingQuery Parse(string? queryString)
    {
        var pairs = (queryString ?? string.Empty).TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Select(p => (Key: Decode(p[0]).ToLowerInvariant(), Value: p.Length > 1 ? Decode(p[1]) : string.Empty))
            .ToList();

        string? One(string key) => pairs.LastOrDefault(p => p.Key == key).Value;

        return Normalize(new StoreListingQuery(
            Q: One("q"),
            PriceBand: One("price"),
            PriceMin: Money(One("min")),
            PriceMax: Money(One("max")),
            Options: pairs.Where(p => p.Key == "opt").Select(p => p.Value).ToList(),
            MinRating: int.TryParse(One("rating"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null,
            InStock: One("instock") is "1" or "true",
            Sort: One("sort"),
            Page: int.TryParse(One("page"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) ? page : 1));
    }

    /// <summary>The query with anything meaningless dropped and the defaults filled in.</summary>
    public static StoreListingQuery Normalize(StoreListingQuery q)
    {
        var text = q.Q?.Trim();
        if (text?.Length > MaxQueryLength) text = text[..MaxQueryLength];

        var options = (q.Options ?? [])
            .Select(o => o.Trim())
            .Where(o => o.IndexOf(':') is > 0 and var i && i < o.Length - 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxOptions)
            .ToList();

        decimal? min = q.PriceMin is >= 0m ? q.PriceMin : null;
        decimal? max = q.PriceMax is >= 0m ? q.PriceMax : null;
        if (min is { } lo && max is { } hi && hi < lo) (min, max) = (max, min);

        return new StoreListingQuery(
            Q: string.IsNullOrEmpty(text) ? null : text,
            PriceBand: StoreCatalogConstants.BandFor(q.PriceBand)?.Key,
            PriceMin: min,
            PriceMax: max,
            Options: options,
            MinRating: q.MinRating is >= 1 and <= 4 ? q.MinRating : null,
            InStock: q.InStock,
            Sort: StoreCatalogConstants.Sorts.Normalize(q.Sort),
            Page: Math.Max(1, q.Page));
    }

    /// <summary>The same query without one option value — the ✕ on a chosen filter.</summary>
    public static StoreListingQuery Without(StoreListingQuery q, string option)
        => q with { Options = (q.Options ?? []).Where(o => !string.Equals(o, option, StringComparison.OrdinalIgnoreCase)).ToList(), Page = 1 };

    private static decimal? Money(string? raw)
        => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));
}
