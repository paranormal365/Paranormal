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


// ── The seller's packages (P7) ───────────────────────────────────────────────

public sealed record SellerParcelLineRecord(string ProductName, string? VariantName, string Sku, int Quantity, Guid? ImageUploadFileId);

/// <summary>
/// One package a seller sends (store sellers, backlog 251, P7): what goes in it, where it's going,
/// and where it stands.
/// </summary>
/// <param name="PackageCount">How many packages the order ships in — "package 2 of 2".</param>
/// <param name="ShipTo">The buyer's name and address, until the package is delivered; null after, and for a cancelled one.</param>
/// <param name="LabelCredit">What the seller is credited for the label — the flat rate, even when it shipped free.</param>
/// <param name="OnHold">The store is looking at the order first; nothing can be packed or shipped until it clears it.</param>
public sealed record SellerParcelRecord(
    Guid Id, Guid OrderId, int OrderNumber, int Number, int PackageCount, DateTime? PaidUtc, StoreParcelStatus Status,
    IReadOnlyList<SellerParcelLineRecord> Lines, StoreOrderAddressView? ShipTo, decimal LabelCredit,
    string? Carrier, string? TrackingNumber, string? TrackingUrl, DateTime? ShippedUtc, DateTime? DeliveredUtc,
    bool OnHold, bool CanPack, bool CanShip, bool CanCorrectTracking, bool CanDeliver);

// ── Earnings and payouts (P10) ───────────────────────────────────────────────

/// <summary>
/// One seller's earnings (store sellers, backlog 251, P10), for their own page and the store's.
/// </summary>
/// <param name="Owed">Every line not yet paid.</param>
/// <param name="PayableNow">The unpaid lines up to <paramref name="Cutoff"/> — past the returns window, so a return can no longer take them back.</param>
/// <param name="Waiting">The unpaid lines still inside the returns window.</param>
public sealed record SellerEarningsRecord(
    decimal Owed, decimal PayableNow, decimal Waiting, decimal PaidToDate, DateTime Cutoff,
    IReadOnlyList<SellerEarningsItemRecord> Items, IReadOnlyList<SellerEarningsOrderRecord> Orders,
    IReadOnlyList<SellerEarningLineRecord> Lines, IReadOnlyList<SellerPayoutRecord> Payouts,
    IReadOnlyList<SellerEarningsYearRecord> Years);

public sealed record SellerEarningsItemRecord(Guid ProductId, string Name, int Units, decimal Earned);

public sealed record SellerEarningsOrderRecord(Guid OrderId, int OrderNumber, DateTime EarnedUtc, decimal Earned, bool Paid);

public sealed record SellerEarningLineRecord(
    Guid Id, StoreEarningKind Kind, decimal Amount, int? Units, string? Note, int? OrderNumber, DateTime OccurredUtc, Guid? PayoutId);

public sealed record SellerPayoutRecord(Guid Id, decimal Amount, DateTime CutoffUtc, DateTime PaidOnUtc, string? Reference, DateTime? VoidedUtc, string? VoidReason);

/// <summary>A calendar year's earnings and payments — what a seller's tax forms want.</summary>
public sealed record SellerEarningsYearRecord(int Year, decimal Earned, decimal Paid);

/// <summary>A seller on the store's sellers page.</summary>
public sealed record StoreSellerBalanceRecord(
    Guid SellerAppUserId, string Name, string? Email, decimal Owed, decimal PayableNow, decimal PaidToDate, DateTime? LastPaidOnUtc, int ItemsOnSale);

/// <summary>The store's view of one seller: their earnings and who they are.</summary>
public sealed record StoreSellerDetailRecord(Guid SellerAppUserId, string Name, string? Email, SellerEarningsRecord Earnings, int ReturnsWindowDays);

/// <param name="Expected">What the page showed as payable — a payment is refused if the lines changed since.</param>
public sealed record RecordSellerPaymentRequest(DateTime CutoffUtc, decimal Expected, DateTime PaidOnUtc, string? Reference);

public sealed record VoidSellerPaymentRequest(string Reason);

public sealed record SellerAdjustmentRequest(decimal Amount, string Note);
