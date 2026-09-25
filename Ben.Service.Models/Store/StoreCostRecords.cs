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
