using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The public store's view of the catalogue: what a shopper may see, and the cards they see it as
/// (storefront S2.2).
/// </summary>
/// <remarks>
/// <para><b>One root.</b> A product is on the store when it is active AND its category is active
/// AND that category's parent (if it has one) is active AND it has at least one active variant —
/// <see cref="LiveProducts"/> says so once, and every public answer starts from it, so no endpoint
/// can forget one. The copies the cart, checkout and stock need in their own queries are held to the
/// same rule by StoreSellableRuleNamesTheParentTests.</para>
///
/// <para><b>Shelves show only what sells</b> (Ben, 09/24): a category or subcategory reaches a shopper
/// only when something is on sale under it; an empty one is there for sellers to file products in,
/// and its address answers 404.</para>
///
/// <para><b>Filtered in memory.</b> A store of tens or hundreds of products is loaded whole and
/// filtered, counted, sorted and paged here. That keeps the answers identical on SQL Server and on
/// the SQLite the tests run on (which cannot order by a decimal column), and lets the filter counts
/// come from the same pass as the list. A catalogue of many thousands would want this pushed into
/// SQL; nothing about the shapes would change.</para>
/// </remarks>
public static class StoreCatalogue
{
    /// <summary>The one definition of "on the store".</summary>
    /// <remarks>A subcategory's product also needs the parent shown: hiding a parent hides everything under it.</remarks>
    public static IQueryable<StoreProduct> LiveProducts(BenDataContext db)
        => db.StoreProducts.Where(p => p.IsActive && p.Category.IsActive
            && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive)
            && p.Variants.Any(v => v.IsActive));

    /// <summary>A product as the listing needs it, loaded once per request.</summary>
    public sealed record Entry(
        StoreProduct Product, StoreCategory Category, IReadOnlyList<StoreProductVariant> Variants,
        IReadOnlyList<(string Option, string Value, string? Hex, StoreOptionKind Kind, int OptionOrder, int ValueOrder, Guid VariantId)> Values,
        StoreProductImage? Picture);

    public static async Task<List<Entry>> LoadAsync(BenDataContext db, IQueryable<StoreProduct> products, CancellationToken ct)
    {
        var rows = await products.AsNoTracking()
            .Include(p => p.Category).ThenInclude(c => c!.ParentCategory)
            .Include(p => p.Variants.Where(v => v.IsActive))
            .AsSplitQuery()
            .ToListAsync(ct);
        var ids = rows.Select(p => p.Id).ToList();

        var values = await db.StoreProductVariantOptionValues.AsNoTracking()
            .Where(x => ids.Contains(x.Variant.ProductId) && x.Variant.IsActive && x.OptionValue.IsActive)
            .Select(x => new
            {
                x.Variant.ProductId, x.VariantId, Option = x.OptionValue.Option.Name, x.OptionValue.Value,
                x.OptionValue.SwatchHex, x.OptionValue.Option.Kind, OptionOrder = x.OptionValue.Option.SortOrder,
                ValueOrder = x.OptionValue.SortOrder,
            })
            .ToListAsync(ct);
        var pictures = await db.StoreProductImages.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId))
            .OrderBy(i => i.VariantId != null).ThenBy(i => i.SortOrder)
            .ToListAsync(ct);

        return rows.Select(p => new Entry(
            p, p.Category, p.Variants.OrderBy(v => v.SortOrder).ToList(),
            values.Where(v => v.ProductId == p.Id)
                .Select(v => (v.Option, v.Value, v.SwatchHex, v.Kind, v.OptionOrder, v.ValueOrder, v.VariantId)).ToList(),
            pictures.FirstOrDefault(i => i.ProductId == p.Id))).ToList();
    }

    public static int Available(StoreProductVariant v) => Math.Max(0, v.StockOnHand - v.StockReserved);

    public static bool IsNew(StoreProduct p, DateTime now) => p.NewUntilUtc is { } until && until >= now.Date;

    public static decimal MinPrice(Entry e) => e.Variants.Min(v => v.Price);

    public static StoreProductCard Card(Entry e, int lowStockThreshold, DateTime now)
    {
        var cheapest = e.Variants.OrderBy(v => v.Price).ThenBy(v => v.SortOrder).First();
        var available = e.Variants.Sum(Available);
        int? discount = cheapest.CompareAtPrice is { } was && was > cheapest.Price
            ? (int)Math.Floor((was - cheapest.Price) / was * 100m)
            : null;
        return new StoreProductCard(
            e.Product.Id, e.Product.Name, e.Product.Slug, e.Category.Name, e.Category.Slug,
            e.Picture?.UploadFileId, e.Picture?.AltText ?? e.Product.Name,
            cheapest.Price, e.Variants.Max(v => v.Price), discount is null ? null : cheapest.CompareAtPrice, discount,
            e.Product.AverageRating, e.Product.ReviewCount,
            InStock: available > 0,
            StockLeft: available > 0 && available <= lowStockThreshold ? available : null,
            IsNew(e.Product, now), e.Product.IsFeatured, e.Variants.Count,
            e.Variants.Count == 1 ? e.Variants[0].Id : null);
    }

    /// <summary>Most sold, then most viewed, then newest.</summary>
    public static IEnumerable<Entry> Popular(IEnumerable<Entry> entries)
        => entries.OrderByDescending(e => e.Product.UnitsSold).ThenByDescending(e => e.Product.ViewCount)
                  .ThenByDescending(e => e.Product.DateCreated).ThenBy(e => e.Product.Name);

    public static IEnumerable<Entry> Sort(IEnumerable<Entry> entries, string sort) => sort switch
    {
        StoreCatalogConstants.Sorts.Newest => entries.OrderByDescending(e => e.Product.DateCreated).ThenBy(e => e.Product.Name),
        StoreCatalogConstants.Sorts.Rating => entries.OrderByDescending(e => e.Product.AverageRating)
            .ThenByDescending(e => e.Product.ReviewCount).ThenBy(e => e.Product.Name),
        StoreCatalogConstants.Sorts.PriceAscending => entries.OrderBy(MinPrice).ThenBy(e => e.Product.Name),
        StoreCatalogConstants.Sorts.PriceDescending => entries.OrderByDescending(MinPrice).ThenBy(e => e.Product.Name),
        _ => Popular(entries),
    };

    /// <summary>Whether a product passes every filter in the query (search and shelf are the caller's).</summary>
    public static bool Matches(Entry e, StoreListingQuery q)
    {
        var price = MinPrice(e);
        if (StoreCatalogConstants.BandFor(q.PriceBand) is { } band && !band.Contains(price)) return false;
        if (q.PriceMin is { } min && price < min) return false;
        if (q.PriceMax is { } max && price > max) return false;
        if (q.MinRating is { } stars && (e.Product.ReviewCount == 0 || e.Product.AverageRating < stars)) return false;
        if (q.InStock && !e.Variants.Any(v => Available(v) > 0)) return false;

        // Values of one option are alternatives; different options must all be met.
        foreach (var group in (q.Options ?? []).Select(Split).GroupBy(o => o.Option, StringComparer.OrdinalIgnoreCase))
            if (!e.Values.Any(v => string.Equals(v.Option, group.Key, StringComparison.OrdinalIgnoreCase)
                                && group.Any(g => string.Equals(g.Value, v.Value, StringComparison.OrdinalIgnoreCase))))
                return false;
        return true;
    }

    public static bool MatchesSearch(Entry e, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return true;
        var term = q.Trim();
        return e.Product.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (e.Product.ShortDescription?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || e.Category.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || e.Variants.Any(v => v.Sku.StartsWith(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The filter counts, over the shelf or search before any filter is applied.</summary>
    public static StoreFilterFacets Facets(IReadOnlyList<Entry> entries)
    {
        var bands = StoreCatalogConstants.PriceBands
            .Select(b => new StorePriceBandFacet(b.Key, b.Label, entries.Count(e => b.Contains(MinPrice(e)))))
            .Where(b => b.Count > 0).ToList();

        var options = entries.SelectMany(e => e.Values.Select(v => (e.Product.Id, v)))
            .GroupBy(x => x.v.Option, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(x => x.v.OptionOrder)).ThenBy(g => g.Key)
            .Select(g => new StoreOptionFacet(g.First().v.Option, g.First().v.Kind,
                g.GroupBy(x => x.v.Value, StringComparer.OrdinalIgnoreCase)
                 .OrderBy(vg => vg.Min(x => x.v.ValueOrder)).ThenBy(vg => vg.Key)
                 .Select(vg => new StoreFacetValue($"{g.First().v.Option}:{vg.First().v.Value}", vg.First().v.Value,
                     vg.Select(x => x.Id).Distinct().Count(), vg.First().v.Hex))
                 .ToList()))
            .ToList();

        var ratings = Enumerable.Range(1, 4).Reverse()
            .Select(s => new StoreRatingFacet(s, entries.Count(e => e.Product.ReviewCount > 0 && e.Product.AverageRating >= s)))
            .ToList();

        return new StoreFilterFacets(bands, options, ratings, entries.Count(e => e.Variants.Any(v => Available(v) > 0)));
    }

    private static (string Option, string Value) Split(string pair)
    {
        var i = pair.IndexOf(':');
        return (pair[..i], pair[(i + 1)..]);
    }
}
