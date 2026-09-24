using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The figures a product carries so the listing never has to work them out per card: its lowest
/// and highest price, and each variant's readable name (storefront S1.4).
/// </summary>
/// <remarks>
/// <para>Recomputed after every change to a product's variants or options, on the one path that
/// makes the change — a cache nothing refreshes is a price the listing gets wrong.</para>
///
/// <para>The range covers ACTIVE variants: a switched-off $499 kit must not make a $59 meter read
/// "$59 – $499". A product with none active falls back to all of them, so an admin's list still
/// shows what it would cost.</para>
/// </remarks>
public static class StorePriceCaches
{
    /// <summary>What a variant is called when the product has no options.</summary>
    public const string DefaultLabel = "Default";

    /// <summary>A variant as a person reads it: its name, or "Default".</summary>
    public static string Label(string? name) => string.IsNullOrWhiteSpace(name) ? DefaultLabel : name;

    /// <summary>The key two variants with the same choices share: value ids, sorted, joined by <c>+</c>.</summary>
    public static string Signature(IEnumerable<Guid> optionValueIds)
        => string.Join('+', optionValueIds.Distinct().Select(id => id.ToString("N")).Order(StringComparer.Ordinal));

    public static async Task RecomputeAsync(BenDataContext db, Guid productId, CancellationToken ct = default)
    {
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return;

        var variants = await db.StoreProductVariants.Where(v => v.ProductId == productId).ToListAsync(ct);
        var chosen = await db.StoreProductVariantOptionValues
            .Where(x => x.Variant.ProductId == productId)
            .Select(x => new
            {
                x.VariantId,
                x.OptionValue.Value,
                OptionOrder = x.OptionValue.Option.SortOrder,
                ValueOrder = x.OptionValue.SortOrder,
            })
            .ToListAsync(ct);

        foreach (var v in variants)
        {
            var parts = chosen.Where(c => c.VariantId == v.Id)
                .OrderBy(c => c.OptionOrder).ThenBy(c => c.ValueOrder)
                .Select(c => c.Value).ToList();
            v.Name = parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        var priced = variants.Where(v => v.IsActive).Select(v => v.Price).DefaultIfEmpty().ToList();
        if (!variants.Any(v => v.IsActive)) priced = variants.Select(v => v.Price).DefaultIfEmpty().ToList();
        product.MinPrice = priced.Min();
        product.MaxPrice = priced.Max();

        await db.SaveChangesAsync(ct);
    }
}
