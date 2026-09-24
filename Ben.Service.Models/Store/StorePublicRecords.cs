using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// What the public store sends to anybody, signed in or not (storefront S2.1). Nothing here carries
// stock counts beyond what a buyer needs, a cost, an admin note or a hidden product.

/// <param name="ProductCount">What is on sale here, its subcategories' products included.</param>
/// <param name="ParentId">The top-level category this is a subcategory of; null at the top.</param>
public sealed record StoreCategoryCard(
    Guid Id, string Name, string Slug, string? Description, Guid? ImageUploadFileId, bool IsNew, int ProductCount,
    Guid? ParentId = null);

/// <param name="CompareAtPrice">The old price, when the cheapest variant has one — drawn struck through.</param>
/// <param name="DiscountPercent">How much the old price is cut, rounded down; null when there is no old price.</param>
/// <param name="StockLeft">Set only at or under the low-stock number — "Only 3 left".</param>
/// <param name="SingleVariantId">Set when the product has exactly one live variant, so a card can add it straight to the cart.</param>
public sealed record StoreProductCard(
    Guid Id, string Name, string Slug, string CategoryName, string CategorySlug,
    Guid? PrimaryImageUploadFileId, string? PrimaryImageAlt,
    decimal MinPrice, decimal MaxPrice, decimal? CompareAtPrice, int? DiscountPercent,
    decimal AverageRating, int ReviewCount, bool InStock, int? StockLeft, bool IsNew, bool IsFeatured,
    int VariantCount, Guid? SingleVariantId);

public sealed record StoreHeroSlide(string Title, string? Caption, Guid ImageUploadFileId, string Href);

public sealed record StoreHomeResponse(
    IReadOnlyList<StoreHeroSlide> Slides, IReadOnlyList<StoreCategoryCard> Categories,
    IReadOnlyList<StoreProductCard> Featured, IReadOnlyList<StoreProductCard> NewArrivals,
    IReadOnlyList<StoreProductCard> Popular);

/// <summary>What every store page needs to know about the shop itself.</summary>
/// <param name="FreeShippingOver">Null when orders never ship free.</param>
/// <param name="PaymentsEnabled">A card can be taken — keys present (or pretend Stripe in Development).</param>
public sealed record StorePublicInfo(
    bool CheckoutEnabled, decimal ShippingFlatRate, decimal? FreeShippingOver, int ReturnsWindowDays,
    string? SupportEmail, bool PaymentsEnabled, bool FakeCheckout, string? PublishableKey);

// ── The listing ──────────────────────────────────────────────────────────────

/// <param name="Value">What the query string carries — "Colour:Black".</param>
public sealed record StoreFacetValue(string Value, string Label, int Count, string? SwatchHex);

public sealed record StoreOptionFacet(string Name, StoreOptionKind Kind, IReadOnlyList<StoreFacetValue> Values);

public sealed record StorePriceBandFacet(string Key, string Label, int Count);

/// <param name="Stars">"4 stars and up".</param>
public sealed record StoreRatingFacet(int Stars, int Count);

public sealed record StoreFilterFacets(
    IReadOnlyList<StorePriceBandFacet> PriceBands, IReadOnlyList<StoreOptionFacet> Options,
    IReadOnlyList<StoreRatingFacet> Ratings, int InStockCount);

/// <summary>Everything a listing can be narrowed by. Every filter is AND-ed with the others.</summary>
/// <param name="Options">"Name:Value" pairs — two values of one option are OR-ed, different options AND-ed.</param>
public sealed record StoreListingQuery(
    string? Q = null, string? PriceBand = null, decimal? PriceMin = null, decimal? PriceMax = null,
    IReadOnlyList<string>? Options = null, int? MinRating = null, bool InStock = false,
    string? Sort = null, int Page = 1);

/// <param name="Category">The shelf being shown; null for all products or a search.</param>
public sealed record StoreListingResponse(
    StoreCategoryCard? Category, IReadOnlyList<StoreCategoryCard> Categories, IReadOnlyList<StoreProductCard> Products,
    int Total, int Page, int PageSize, StoreFilterFacets Facets);

public sealed record StoreSearchSuggestion(string Name, string Slug, Guid? ImageUploadFileId, decimal MinPrice);

// ── One product ──────────────────────────────────────────────────────────────

/// <param name="Available">Free to sell — on hand less what checkouts hold. 0 is sold out.</param>
public sealed record StoreVariantPublicRecord(
    Guid Id, string Sku, string Label, decimal Price, decimal? CompareAtPrice, int Available,
    IReadOnlyList<Guid> OptionValueIds, bool IsDefault);

/// <param name="CountsByStars">Five counts, one star first.</param>
public sealed record StoreReviewSummary(decimal Average, int Count, IReadOnlyList<int> CountsByStars);

/// <summary>The product's page in the equipment catalogue.</summary>
public sealed record StoreEquipmentLink(string BrandName, string BrandSlug, string ModelName, string ModelSlug)
{
    public string Href => $"/equipment/{BrandSlug}/{ModelSlug}";
}

/// <param name="IsPreview">A SuperAdmin looking at something shoppers can't see yet.</param>
public sealed record StoreProductDetail(
    Guid Id, string Name, string Slug, string CategoryName, string CategorySlug, string? ShortDescription,
    string? LongDescriptionHtml, bool IsNew, bool IsFeatured, IReadOnlyList<StoreImageRecord> Images,
    IReadOnlyList<StoreOptionRecord> Options, IReadOnlyList<StoreVariantPublicRecord> Variants,
    IReadOnlyList<StoreSpecGroup> Specs, StoreReviewSummary Reviews, StoreEquipmentLink? Equipment,
    IReadOnlyList<StoreProductCard> Related, int LowStockThreshold, int ReturnsWindowDays,
    DateTime LastUpdatedUtc, bool IsPreview, string? ParentCategoryName = null, string? ParentCategorySlug = null);
