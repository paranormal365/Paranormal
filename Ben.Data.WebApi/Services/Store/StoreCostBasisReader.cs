using Ben.Data.Source.Context;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What one unit of each item costs to make, read in one go (store sellers, backlog 251, P4) — for
/// checkout's snapshot of what a seller earns (P9) and the admin's economics panel.
/// </summary>
public static class StoreCostBasisReader
{
    /// <summary>Cost basis per unit for each item asked about; an item with no parts and no "other" line costs $0.00.</summary>
    public static async Task<Dictionary<Guid, decimal>> ReadAsync(BenDataContext db, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        var others = await db.StoreProducts.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.OtherCostPerUnit }).ToDictionaryAsync(p => p.Id, p => p.OtherCostPerUnit, ct);
        var parts = await db.StoreProductParts.AsNoTracking().Where(x => productIds.Contains(x.ProductId))
            .Select(x => new { x.ProductId, x.PriceBasis, x.Price, x.PiecesPerPack, x.QuantityPerUnit }).ToListAsync(ct);
        return others.ToDictionary(o => o.Key, o => StoreCostMath.CostBasis(
            parts.Where(x => x.ProductId == o.Key).Select(x => StoreCostMath.PartCostPerUnit(x.PriceBasis, x.Price, x.PiecesPerPack, x.QuantityPerUnit)),
            o.Value));
    }
}
