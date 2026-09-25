using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The store's readable addresses — <c>/store/c/emf-meters</c>, <c>/store/p/k-ii-emf-meter</c>
/// (storefront S1.3).
/// </summary>
/// <remarks>
/// Frozen once given, unlike the equipment catalogue's (<see cref="EquipmentCatalogSlugs"/>): a
/// product link is exactly what gets shared, bookmarked and printed on a flyer, so renaming a
/// product keeps its address. An admin who wants a new address types one; a typed address that is
/// taken is refused rather than quietly suffixed, because the admin chose it on purpose.
/// </remarks>
public static class StoreSlugs
{
    /// <summary>A free address for a new category, made from its name.</summary>
    public static Task<string> ForCategoryAsync(BenDataContext db, Guid id, string name, CancellationToken ct)
        => UrlSlug.MakeUniqueAsync(UrlSlug.From(name) ?? id.ToString("N")[..8],
            slug => db.StoreCategories.AnyAsync(c => c.Id != id && c.Slug == slug, ct));

    /// <summary>A free address for a new product, made from its name.</summary>
    public static Task<string> ForProductAsync(BenDataContext db, Guid id, string name, CancellationToken ct)
        => UrlSlug.MakeUniqueAsync(UrlSlug.From(name) ?? id.ToString("N")[..8],
            slug => db.StoreProducts.AnyAsync(p => p.Id != id && p.Slug == slug, ct));

    /// <summary>A typed address in its one spelling, or null when it has no letters or digits.</summary>
    public static string? Typed(string? typed) => UrlSlug.From(typed);
}
