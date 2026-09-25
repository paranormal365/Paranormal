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
