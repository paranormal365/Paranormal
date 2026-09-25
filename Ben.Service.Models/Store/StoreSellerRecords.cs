using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// Store sellers (backlog 251): what a seller's workspace reads. A seller works only on their own
// items; nothing here carries another seller's, or the site's, anything.

/// <summary>Where one of a seller's items stands, in the seller's words.</summary>
public enum StoreSellerItemStatus
{
    /// <summary>Not on sale, and never has been.</summary>
    Draft = 0,

    /// <summary>On the store.</summary>
    OnSale = 1,

    /// <summary>Was on sale once; taken off.</summary>
    OffSale = 2,
}

/// <summary>The status words and colours, decided once for every page that shows them.</summary>
public static class StoreSellerStatus
{
    /// <param name="isActive">The product is switched on.</param>
    /// <param name="everOnSale">It has been on sale before (a product that sold once has).</param>
    public static StoreSellerItemStatus For(bool isActive, bool everOnSale)
        => isActive ? StoreSellerItemStatus.OnSale : everOnSale ? StoreSellerItemStatus.OffSale : StoreSellerItemStatus.Draft;

    public static string Label(StoreSellerItemStatus status) => status switch
    {
        StoreSellerItemStatus.OnSale => "On sale",
        StoreSellerItemStatus.OffSale => "Off sale",
        _ => "Draft",
    };

    /// <summary>A Bootstrap colour name for the badge.</summary>
    public static string Tone(StoreSellerItemStatus status) => status switch
    {
        StoreSellerItemStatus.OnSale => "success",
        StoreSellerItemStatus.OffSale => "secondary",
        _ => "warning",
    };
}

/// <summary>One of the seller's own items, as their list shows it.</summary>
/// <param name="Price">The selling price the store set — shown, never changed by the seller. Null before it has one.</param>
/// <param name="StockOnHand">Units on the shelf across the item's switched-on variants.</param>
public sealed record SellerProductListRecord(
    Guid Id, string Name, string Slug, string CategoryName, StoreSellerItemStatus Status,
    decimal? MinPrice, decimal? MaxPrice, int StockOnHand, int UnitsSold, DateTime? LastSoldUtc,
    Guid? PrimaryImageUploadFileId, DateTime? DateUpdated);

/// <summary>The counts at the top of the workspace.</summary>
public sealed record SellerWorkspaceSummary(int Drafts, int OnSale, int OffSale);

// ── The seller's editor (P3) ─────────────────────────────────────────────────
// Every request below is the admin's with the store's fields left out — price, web address,
// placement, featuring, tax and seller. SellerRequestsCarryNoStoreFieldsTests holds them to it.

/// <summary>A new draft: a name and, optionally, a shelf.</summary>
public sealed record CreateSellerItemRequest(string Name, Guid? CategoryId = null);

/// <summary>The words about an item. The store's fields are not here to send.</summary>
public sealed record SaveSellerItemRequest(
    Guid CategoryId, string Name, string? ShortDescription, string? LongDescriptionHtml,
    IReadOnlyList<StoreSpecGroup> Specs, DateTime? ExpectedDateUpdated);

/// <summary>A variant, without its price: a new one starts off and unpriced until the store prices it.</summary>
public sealed record SaveSellerVariantRequest(
    string Sku, bool IsActive, bool IsDefault, int SortOrder, IReadOnlyList<Guid> OptionValueIds, int? InitialStock = null);

/// <summary>Asking the store to put an item on sale, and what the seller wants a unit for it.</summary>
public sealed record SellerSaleRequest(decimal AskingPrice, string? Note);

/// <summary>A shelf a seller can file an item under.</summary>
public sealed record SellerCategoryRecord(Guid Id, string Name);

/// <summary>
/// One of the seller's items, whole, for their editor: the item as the store's editor sees it,
/// where it stands, and the request that is waiting or was last answered.
/// </summary>
public sealed record SellerItemRecord(
    StoreProductAdminRecord Item, StoreSellerItemStatus Status, decimal? SellerAskPerUnit, StoreSaleRequestRecord? Request);

