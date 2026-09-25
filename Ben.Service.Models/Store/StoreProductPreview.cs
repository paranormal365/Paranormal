namespace Ben.Service.Models.Store;

/// <summary>What the editor holds that has not been saved yet — drawn in the live preview.</summary>
/// <param name="Variants">Unsaved prices and on/off, by variant id; a variant not here is drawn as saved.</param>
public sealed record StoreProductPreviewDraft(
    string Name, string? ShortDescription, string? LongDescriptionHtml, bool IsFeatured, DateTime? NewUntilUtc,
    IReadOnlyList<StoreSpecGroup> Specs, IReadOnlyDictionary<Guid, StoreVariantPreviewDraft>? Variants = null);

public sealed record StoreVariantPreviewDraft(decimal Price, decimal? CompareAtPrice, bool IsActive);

/// <summary>What the product page needs that is not the product: its shelf, its model, the store's numbers.</summary>
public sealed record StoreProductPreviewContext(
    string CategoryName, string CategorySlug, StoreEquipmentLink? Equipment, int LowStockThreshold, int ReturnsWindowDays,
    DateTime Now, string? ParentCategoryName = null, string? ParentCategorySlug = null);

/// <summary>
/// The product page as a shopper would see it, built from the editor's unsaved work (Ben,
/// 09/24/2026: "a preview button so you can see … the store page as it is being configured").
/// </summary>
/// <remarks>
/// <para><b>Built in the editor, not fetched.</b> The shop's own product address answers only when
/// the store is switched on and only with what is saved; this answers while the store is dark
/// and with what is typed. The rules are the public page's (PublicStoreController.Product):
/// only live variants, only option values a live variant is made of, an old price only when it is
/// higher, "New" until the end of the chosen day.</para>
///
/// <para>Nothing else the shop shows can be known here — other products on the shelf, the
/// stars by count — so those parts are empty rather than invented.</para>
/// </remarks>
public static class StoreProductPreview
{
    public static StoreProductDetail Build(
        StoreProductAdminRecord saved, StoreProductPreviewDraft draft, StoreProductPreviewContext context)
    {
        var variants = saved.Variants
            .Select(v => (Saved: v, Draft: draft.Variants?.GetValueOrDefault(v.Id)))
            .Where(x => x.Draft?.IsActive ?? x.Saved.IsActive)
            .Select(x =>
            {
                var price = x.Draft?.Price ?? x.Saved.Price;
                var was = x.Draft is { } d ? d.CompareAtPrice : x.Saved.CompareAtPrice;
                return new StoreVariantPublicRecord(
                    x.Saved.Id, x.Saved.Sku, x.Saved.Label, price, was is { } w && w > price ? w : null,
                    Math.Max(0, x.Saved.StockOnHand - x.Saved.StockReserved), x.Saved.OptionValueIds, x.Saved.IsDefault);
            })
            .ToList();

        var usable = variants.SelectMany(v => v.OptionValueIds).ToHashSet();
        var options = saved.Options
            .Select(o => o with { Values = o.Values.Where(v => v.IsActive && usable.Contains(v.Id)).ToList() })
            .Where(o => o.Values.Count > 0)
            .ToList();

        var name = string.IsNullOrWhiteSpace(draft.Name) ? saved.Name : draft.Name.Trim();

        return new StoreProductDetail(
            saved.Id, name, saved.Slug, context.CategoryName, context.CategorySlug,
            string.IsNullOrWhiteSpace(draft.ShortDescription) ? null : draft.ShortDescription.Trim(),
            string.IsNullOrWhiteSpace(draft.LongDescriptionHtml) ? null : draft.LongDescriptionHtml,
            IsNew: draft.NewUntilUtc is { } until && until.Date >= context.Now.Date,
            draft.IsFeatured, saved.Images, options, variants,
            draft.Specs.Where(g => g.Items.Count > 0).ToList(),
            new StoreReviewSummary(saved.AverageRating, saved.ReviewCount, [0, 0, 0, 0, 0]),
            context.Equipment, Related: [], context.LowStockThreshold, context.ReturnsWindowDays,
            LastUpdatedUtc: context.Now, IsPreview: true, context.ParentCategoryName, context.ParentCategorySlug);
    }
}
