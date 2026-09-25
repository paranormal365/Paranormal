using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Store;

// An item's parts and what one unit costs to make (store sellers, backlog 251, P4). The seller's
// and the store's alone: nothing here is ever in a record a shopper can read
// (StorePublicRecordsCarryNoCostTests).

/// <param name="Price">Per pack or per piece, as <paramref name="PriceBasis"/> says — to the hundredth of a cent.</param>
/// <param name="PiecesPerPack">How many pieces the pack price buys; 1 for a part bought singly.</param>
/// <param name="QuantityPerUnit">Pieces used in one unit built — 0.5 for half a sheet.</param>
/// <param name="OnHand">Pieces in the workshop, when counted; null when not.</param>
/// <param name="CostPerUnit">What this part adds to one unit, before the total is rounded.</param>
public sealed record StorePartRecord(
    Guid Id, string Name, StorePartPriceBasis PriceBasis, decimal Price, int PiecesPerPack, decimal QuantityPerUnit,
    string? InfoUrl, string? BuyUrl, Guid? ThumbnailUploadFileId, int? OnHand, int SortOrder, decimal CostPerUnit);

/// <param name="CostBasisPerUnit">Every part's share plus the "other" line, rounded to the cent once.</param>
/// <param name="BuildableFromOnHand">Units the counted parts would make; null when no part has been counted.</param>
/// <param name="EditedAt">The item's <c>DateUpdated ?? DateCreated</c>, for a save's stale-edit check.</param>
public sealed record StorePartsRecord(
    Guid ProductId, IReadOnlyList<StorePartRecord> Parts, decimal OtherCostPerUnit, string? OtherCostNote,
    decimal CostBasisPerUnit, int? BuildableFromOnHand, DateTime EditedAt);

public sealed record SaveStorePartRequest(
    Guid? Id, string Name, StorePartPriceBasis PriceBasis, decimal Price, int PiecesPerPack, decimal QuantityPerUnit,
    string? InfoUrl, string? BuyUrl, int? OnHand);

/// <summary>An item's parts, whole: parts missing from the list are removed.</summary>
public sealed record SaveStorePartsRequest(
    IReadOnlyList<SaveStorePartRequest> Parts, decimal OtherCostPerUnit, string? OtherCostNote, DateTime? ExpectedDateUpdated);

/// <summary>
/// What a unit costs to make (store sellers, backlog 251, P4). Shared by the server and both
/// editors, so the total a seller sees as they type is the one that is saved.
/// </summary>
/// <remarks>
/// <b>Rounded once, at the end.</b> A part at $0.0699 a piece used three times is $0.2097; rounding
/// each part to the cent first and adding would drift a cent or two on a long parts list, and the
/// seller's pay is built on this number.
/// </remarks>
public static class StoreCostMath
{
    /// <summary>One piece's price, from a pack price or a piece price. Unrounded.</summary>
    public static decimal PiecePrice(StorePartPriceBasis basis, decimal price, int piecesPerPack)
        => basis == StorePartPriceBasis.PerPiece || piecesPerPack <= 1 ? price : price / piecesPerPack;

    /// <summary>What one part adds to a unit. Unrounded.</summary>
    public static decimal PartCostPerUnit(StorePartPriceBasis basis, decimal price, int piecesPerPack, decimal quantityPerUnit)
        => PiecePrice(basis, price, piecesPerPack) * quantityPerUnit;

    /// <summary>The cost basis of one unit: every part plus the "other" line, rounded to the cent once.</summary>
    public static decimal CostBasis(IEnumerable<decimal> partCostsPerUnit, decimal otherPerUnit)
        => StoreMoney.Round(partCostsPerUnit.Sum() + otherPerUnit);

    /// <summary>
    /// How many whole units the counted parts would make: the scarcest part decides. Null when no
    /// part has been counted; parts not counted, or used 0 at a time, do not limit it.
    /// </summary>
    public static int? Buildable(IEnumerable<(int? OnHand, decimal QuantityPerUnit)> parts)
    {
        int? least = null;
        foreach (var (onHand, perUnit) in parts)
        {
            if (onHand is not { } have || perUnit <= 0m) continue;
            var units = (int)Math.Floor(Math.Max(have, 0) / perUnit);
            least = least is { } l ? Math.Min(l, units) : units;
        }
        return least;
    }
}

// ── Economics (P9) — the store's alone ───────────────────────────────────────

/// <summary>
/// What a product's sale is worth to each side, for the store's economics panel (store sellers P9).
/// </summary>
/// <param name="Owed">What a unit must pay out before the store earns: the seller's earning (cost + ask), or the cost of the store's own stock.</param>
/// <param name="Price">The lowest live variant price; null while unpriced.</param>
/// <param name="EstimatedFee">Stripe's fee on one unit bought alone at <paramref name="Price"/>.</param>
/// <param name="Margin">What the store keeps of a unit at that price after the fee; null while unpriced.</param>
/// <param name="BelowCost">The price doesn't cover what's owed plus the fee.</param>
public sealed record StoreProductEconomicsRecord(
    decimal CostBasis, bool HasSeller, decimal? SellerAsk, decimal Owed, decimal? Price, decimal? MaxPrice, decimal EstimatedFee,
    decimal? Margin, decimal SuggestedPrice, bool BelowCost, decimal MarkupPercent, decimal FeePercent, decimal FeeFixed);

/// <summary>One order line's economics, as fixed at checkout.</summary>
public sealed record StoreOrderEconomicsLine(
    Guid ItemId, decimal UnitCostBasis, decimal UnitSellerAsk, decimal UnitSellerEarning, decimal UnitSiteMarkup);

/// <summary>
/// What an order came to for each side (store sellers P9): the sellers' earnings and label credits,
/// what Stripe kept, and the store's margin — on what wasn't refunded.
/// </summary>
/// <param name="StripeFee">Null until read from Stripe.</param>
/// <param name="StoreMargin">Items' markup less the discount, plus shipping kept less sellers' label credits, less the fee (when known).</param>
public sealed record StoreOrderEconomicsRecord(
    IReadOnlyList<StoreOrderEconomicsLine> Lines, decimal SellerEarnings, decimal SellerShippingCredits, decimal ItemsMarkup,
    decimal Discount, decimal ShippingKept, decimal? StripeFee, decimal? StripeNet, decimal StoreMargin);

// ── Product files (P11) ──────────────────────────────────────────────────────

/// <param name="IsManual">Written on the site as HTML, not an upload.</param>
public sealed record StoreProductFileRecord(
    Guid Id, StoreProductFileKind Kind, StoreFileAudience Audience, string Title, string? VersionLabel,
    string? FileName, long? SizeBytes, bool IsManual, int SortOrder, DateTime DateUpdated);

public sealed record SaveStoreProductFileRequest(string Title, StoreProductFileKind Kind, StoreFileAudience Audience, string? VersionLabel, int SortOrder);

/// <summary>A manual written on the site: HTML from the editor, or Markdown/plain text imported (<paramref name="IsMarkdown"/>).</summary>
public sealed record SaveStoreManualRequest(string Title, string Body, bool IsMarkdown, StoreFileAudience Audience, string? VersionLabel);

/// <summary>A written manual, to read or print.</summary>
public sealed record StoreManualRecord(Guid Id, string ProductName, string Title, string? VersionLabel, string Html);

/// <summary>A file a buyer may download from their paid order.</summary>
public sealed record StoreOrderDownloadRecord(
    Guid FileId, Guid ProductId, string ProductName, string Title, StoreProductFileKind Kind, string? VersionLabel,
    string? FileName, long? SizeBytes, bool IsManual);
