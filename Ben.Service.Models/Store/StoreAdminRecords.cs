using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// What the store's admin screens send and receive (storefront S1.2). SuperAdmin only; none of
// these ever reach a buyer.

// ── Categories ───────────────────────────────────────────────────────────────

/// <param name="ProductCount">Every product filed here, live or not — what blocks a delete.</param>
/// <param name="LiveProductCount">Products a buyer can see today, its subcategories' included. Hiding the
/// category takes exactly these off the store, so the confirm dialog reads it BEFORE anything is saved.</param>
/// <param name="ParentCategoryId">The top-level category this is a subcategory of; null for a top-level one.</param>
/// <param name="SubcategoryCount">How many subcategories sit under it — a category with any cannot become one.</param>
public sealed record StoreCategoryAdminRecord(
    Guid Id, string Name, string Slug, string? Description, Guid? ImageUploadFileId, int SortOrder,
    bool IsActive, bool IsNew, int ProductCount, int LiveProductCount, DateTime DateCreated,
    Guid? ParentCategoryId = null, string? ParentName = null, int SubcategoryCount = 0);

/// <param name="Slug">Empty = made from the name.</param>
/// <param name="ParentCategoryId">Null files it at the top level; otherwise the top-level category it sits under.</param>
public sealed record SaveStoreCategoryRequest(
    string Name, string? Slug, string? Description, bool IsActive, bool IsNew, Guid? ParentCategoryId = null);

/// <summary>What saving a category did, beyond saving it.</summary>
/// <param name="HiddenProducts">Live products this save took off the store (0 unless it hid the category).</param>
public sealed record StoreCategorySaveResult(StoreCategoryAdminRecord Category, int HiddenProducts);

/// <summary>A complete new order for a list — every id, once.</summary>
public sealed record ReorderRequest(IReadOnlyList<Guid> OrderedIds);

// ── Products ─────────────────────────────────────────────────────────────────

/// <param name="HiddenByCategory">Active, but its category is hidden — so not on the store.</param>
public sealed record StoreProductListAdminRecord(
    Guid Id, string Name, string Slug, Guid CategoryId, string CategoryName, bool IsActive, bool HiddenByCategory,
    bool IsFeatured, decimal MinPrice, decimal MaxPrice, int TotalStock, int LowStockVariants, int VariantCount,
    int UnitsSold, Guid? PrimaryImageUploadFileId, DateTime? DateUpdated, string? SellerName = null);

/// <summary>Just a name: a new product starts hidden, with one $0.00 variant to price.</summary>
public sealed record CreateStoreProductRequest(string Name, Guid? CategoryId = null);

/// <summary>A product's details. No <c>IsActive</c> — putting a product on sale is its own verb,
/// because it has its own checks.</summary>
public sealed record SaveStoreProductRequest(
    Guid CategoryId, Guid? EquipmentModelId, string Name, string? Slug, string? ShortDescription,
    string? LongDescriptionHtml, bool IsFeatured, DateTime? NewUntilUtc, string? StripeTaxCode, int SortOrder,
    IReadOnlyList<StoreSpecGroup> Specs, Guid? SellerAppUserId);

public sealed record StoreProductAdminRecord(
    Guid Id, Guid CategoryId, string CategoryName, bool CategoryIsActive, Guid? EquipmentModelId, string Name,
    string Slug, string? ShortDescription, string? LongDescriptionHtml, bool IsActive, bool IsFeatured,
    DateTime? NewUntilUtc, string? StripeTaxCode, int SortOrder, int ViewCount, int UnitsSold,
    decimal AverageRating, int ReviewCount, int PendingReviewCount, bool HasBeenSold,
    IReadOnlyList<StoreImageRecord> Images, IReadOnlyList<StoreOptionRecord> Options,
    IReadOnlyList<StoreVariantAdminRecord> Variants, IReadOnlyList<StoreSpecGroup> Specs,
    string PublicUrl, DateTime DateCreated, DateTime? DateUpdated,
    Guid? SellerAppUserId = null, string? SellerName = null);

/// <summary>Somebody who can be named as an item's seller: a holder of the Seller role.</summary>
public sealed record StoreSellerRecord(Guid Id, string Name, string? Email);

/// <param name="Label">The variant as a person reads it — "Black / Large", or "Default".</param>
/// <param name="HasBeenOrdered">True once any order line names it; it can then only be deactivated.</param>
public sealed record StoreVariantAdminRecord(
    Guid Id, string Sku, string Label, decimal Price, decimal? CompareAtPrice, int StockOnHand, int StockReserved,
    int UnitsSold, bool IsActive, bool IsDefault, int SortOrder, IReadOnlyList<Guid> OptionValueIds,
    bool HasBeenOrdered);

/// <param name="InitialStock">Create only — the first "Received" movement. Ignored on update;
/// stock changes go through the stock verb so every change leaves a movement.</param>
public sealed record SaveStoreVariantRequest(
    string Sku, decimal Price, decimal? CompareAtPrice, bool IsActive, bool IsDefault, int SortOrder,
    IReadOnlyList<Guid> OptionValueIds, int? InitialStock = null);

/// <summary>A product's options, whole: options and values missing from the list are removed.</summary>
public sealed record SaveStoreOptionsRequest(IReadOnlyList<SaveStoreOptionRequest> Options);

public sealed record SaveStoreOptionRequest(
    Guid? Id, string Name, StoreOptionKind Kind, IReadOnlyList<SaveStoreOptionValueRequest> Values);

/// <param name="SwatchHex">Swatch options only — "#1a2b3c".</param>
public sealed record SaveStoreOptionValueRequest(Guid? Id, string Value, string? SwatchHex, bool IsActive);

/// <summary>What generating variants from the options did.</summary>
public sealed record StoreVariantsGenerated(int Added, IReadOnlyList<StoreVariantAdminRecord> Variants);

/// <param name="VariantId">Show this picture when that variant is chosen; null = the product's.</param>
public sealed record UpdateStoreImageRequest(string? AltText, Guid? VariantId);

// ── Stock ────────────────────────────────────────────────────────────────────

/// <summary>A change (<paramref name="Delta"/>) or a count (<paramref name="SetTo"/>) — one, never both.</summary>
public sealed record AdjustStockRequest(int? Delta, int? SetTo, StoreStockReason Reason, string? Note);

/// <summary>Several variants at once, all or nothing — the stock page's one Save.</summary>
public sealed record BulkAdjustStockRequest(IReadOnlyList<BulkStockLine> Lines, StoreStockReason Reason, string? Note);

public sealed record BulkStockLine(Guid VariantId, int? Delta, int? SetTo);

/// <param name="Available">On hand less what open checkouts hold.</param>
public sealed record StoreStockRow(
    Guid VariantId, Guid ProductId, string ProductName, string Sku, string VariantName,
    int OnHand, int Reserved, int Available, bool IsLow);

public sealed record StoreStockMovementRecord(
    Guid Id, int Delta, int QuantityAfter, StoreStockReason Reason, string? Note, Guid? OrderId,
    int? OrderNumber, DateTime OccurredUtc, string? ActorDisplayName);

/// <summary>What a bulk receive or a CSV import changed.</summary>
public sealed record StoreStockAdjusted(int VariantsChanged, IReadOnlyList<StoreStockRow> Rows);

// ── Coupons ──────────────────────────────────────────────────────────────────

/// <param name="Problem">Why the code cannot be used right now — "Used up." — or null when it can.</param>
/// <param name="OrderCount">Orders placed with it, paid or not; what stops a rename or a delete.</param>
public sealed record StoreCouponAdminRecord(
    Guid Id, string Code, string Name, StoreCouponKind Kind, int? PercentOff, decimal? AmountOff,
    decimal? MinimumOrderAmount, DateTime? StartsUtc, DateTime? EndsUtc, int? MaxRedemptions,
    int RedemptionCount, int? MaxRedemptionsPerBuyer, bool IsActive, string? Problem, int OrderCount,
    DateTime DateCreated);

public sealed record SaveStoreCouponRequest(
    string Code, string Name, StoreCouponKind Kind, int? PercentOff, decimal? AmountOff,
    decimal? MinimumOrderAmount, DateTime? StartsUtc, DateTime? EndsUtc, int? MaxRedemptions,
    int? MaxRedemptionsPerBuyer, bool IsActive);

// ── Reviews ──────────────────────────────────────────────────────────────────

public sealed record StoreReviewAdminRecord(
    Guid Id, Guid ProductId, string ProductName, string ProductSlug, Guid AuthorAppUserId, string AuthorDisplayName,
    int OrderNumber, int Rating, string Title, string Body, StoreReviewStatus Status, string? RejectionReason,
    int HelpfulCount, DateTime CreatedUtc, DateTime? ModeratedUtc, string? AdminReply, DateTime? AdminRepliedUtc);

public sealed record RejectStoreReviewRequest(string Reason);

/// <param name="Body">Null or empty clears the reply.</param>
public sealed record ReplyToStoreReviewRequest(string? Body);

// ── Settings and dashboard ───────────────────────────────────────────────────

/// <summary>
/// The store settings page: what is stored (null = unset, the default applies) and whether the
/// store could take an order right now.
/// </summary>
/// <param name="NotReadyBecause">Every reason it could not, in words; empty when ready.</param>
/// <param name="TaxRegisteredStates">States Stripe Tax collects in. Anywhere else is taxed at $0.</param>
public sealed record StoreSettingsAdminRecord(
    bool StoreIsOn, bool CheckoutEnabled, decimal? ShippingFlatRate, decimal? FreeShippingThreshold,
    int? LowStockThreshold, string? ShipFromStreet, string? ShipFromCity, string? ShipFromState,
    string? ShipFromZip, string? SupportEmail, int? ReturnsWindowDays, int? ReservationMinutes, bool LinkEnabled,
    bool ReadyToSell, IReadOnlyList<string> NotReadyBecause, IReadOnlyList<string> TaxRegisteredStates);

/// <summary>Every store setting at once. Null = unset, so the default applies.</summary>
public sealed record SaveStoreSettingsRequest(
    bool CheckoutEnabled, decimal? ShippingFlatRate, decimal? FreeShippingThreshold, int? LowStockThreshold,
    string? ShipFromStreet, string? ShipFromCity, string? ShipFromState, string? ShipFromZip,
    string? SupportEmail, int? ReturnsWindowDays, int? ReservationMinutes, bool LinkEnabled);

public sealed record StoreSales(decimal GrossUsd, decimal RefundedUsd, decimal NetUsd);

/// <param name="UnitsHeldByOpenCheckouts">Units reserved by checkouts still waiting for payment.</param>
public sealed record StoreDashboardRecord(
    bool StoreIsOn, bool CheckoutEnabled, int Days, int OrdersToPack, int OrdersToShip, int OrdersNeedingAttention,
    int ReviewsPending, int UnitsHeldByOpenCheckouts, int OrdersInRange, StoreSales Sales,
    IReadOnlyList<StoreLowStockRow> LowStock);

public sealed record StoreLowStockRow(
    Guid ProductId, string ProductName, Guid VariantId, string Sku, string VariantName, int Available, int Threshold);
