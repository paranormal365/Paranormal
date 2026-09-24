using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>One line of a bulk stock change: add <see cref="Delta"/>, or set the shelf to <see cref="SetTo"/>.</summary>
public sealed record StoreStockChange(Guid VariantId, int? Delta, int? SetTo);

/// <summary>
/// Every change to a variant's stock (storefront plan §5.4).
/// </summary>
/// <remarks>
/// <para><b>One statement per change.</b> Stock is two numbers — on hand and reserved — and a CHECK
/// keeps reserved within on hand. Every method here is a single conditional
/// <c>ExecuteUpdateAsync</c> whose WHERE carries the rule, so two buyers racing for the last unit,
/// or an admin correcting the count while a checkout reserves, cannot both win: the loser's UPDATE
/// matches no row and the method answers false. Nothing reads a number and writes it back.</para>
///
/// <para><b>The ledger.</b> Every change to what is on the shelf writes a
/// <see cref="StoreStockMovement"/> with the quantity read back after the update. Reservations are
/// not movements — nothing left the building.</para>
///
/// <para><b>Saving.</b> A movement is added and saved here, so a method that writes one saves
/// whatever else the context is tracking. Callers that need the stock and their own rows to land
/// together hold a transaction around the call, as checkout and refunds do.</para>
/// </remarks>
public static class StoreStock
{
    /// <summary>
    /// Holds <paramref name="quantity"/> of a variant for a checkout. True only when the variant is
    /// sellable (it, its product and its category all active) and that many are free.
    /// </summary>
    /// <remarks>Callers reserving several lines reserve them in <c>VariantId</c> order: two
    /// checkouts taking the same two variants in opposite orders deadlock on SQL Server.</remarks>
    public static async Task<bool> TryReserveAsync(BenDataContext db, Guid variantId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Reserve at least one.");

        var rows = await db.StoreProductVariants
            .Where(v => v.Id == variantId
                     && v.IsActive
                     && v.StockOnHand - v.StockReserved >= quantity
                     && db.StoreProducts.Any(p => p.Id == v.ProductId && p.IsActive && p.Category.IsActive))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockReserved, v => v.StockReserved + quantity), ct);
        return rows == 1;
    }

    /// <summary>Lets go of a hold. False when there is not that much held (it was already released).</summary>
    public static async Task<bool> ReleaseReservationAsync(BenDataContext db, Guid variantId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Release at least one.");

        var rows = await db.StoreProductVariants
            .Where(v => v.Id == variantId && v.StockReserved >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockReserved, v => v.StockReserved - quantity), ct);
        return rows == 1;
    }

    /// <summary>
    /// Turns a paid hold into a sale: the units leave the shelf and the hold together, and the
    /// variant's sold count goes up. False when the hold is not there to convert.
    /// </summary>
    public static async Task<bool> CommitSaleAsync(
        BenDataContext db, Guid variantId, int quantity, Guid orderId, DateTime now, CancellationToken ct = default)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Sell at least one.");

        var rows = await db.StoreProductVariants
            .Where(v => v.Id == variantId && v.StockReserved >= quantity && v.StockOnHand >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.StockOnHand, v => v.StockOnHand - quantity)
                .SetProperty(v => v.StockReserved, v => v.StockReserved - quantity)
                .SetProperty(v => v.UnitsSold, v => v.UnitsSold + quantity), ct);
        if (rows != 1) return false;

        await RecordAsync(db, variantId, -quantity, StoreStockReason.Sold, null, orderId, null, null, now, ct);
        return true;
    }

    /// <summary>Puts units back on the shelf after a refund or a cancellation. False when the variant is gone.</summary>
    public static async Task<bool> RestockAsync(
        BenDataContext db, Guid variantId, int quantity, StoreStockReason reason,
        Guid? orderId, Guid? refundId, Guid? actorAppUserId, DateTime now, CancellationToken ct = default)
    {
        if (quantity < 1) throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Restock at least one.");
        if (reason is not (StoreStockReason.Refunded or StoreStockReason.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A restock is a refund or a cancellation.");

        var rows = await db.StoreProductVariants
            .Where(v => v.Id == variantId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockOnHand, v => v.StockOnHand + quantity), ct);
        if (rows != 1) return false;

        await RecordAsync(db, variantId, quantity, reason, null, orderId, refundId, actorAppUserId, now, ct);
        return true;
    }

    /// <summary>
    /// An admin's change to the count — received, damaged, corrected. False, with nothing written,
    /// when it would leave fewer on the shelf than open checkouts are holding.
    /// </summary>
    public static async Task<bool> AdjustAsync(
        BenDataContext db, Guid variantId, int delta, StoreStockReason reason, string? note,
        Guid? actorAppUserId, DateTime now, CancellationToken ct = default)
    {
        if (delta == 0) throw new ArgumentOutOfRangeException(nameof(delta), delta, "A change of nothing is not a change.");

        var rows = await db.StoreProductVariants
            .Where(v => v.Id == variantId && v.StockOnHand + delta >= v.StockReserved)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockOnHand, v => v.StockOnHand + delta), ct);
        if (rows != 1) return false;

        await RecordAsync(db, variantId, delta, reason, note, null, null, actorAppUserId, now, ct);
        return true;
    }

    /// <summary>
    /// Sets the shelf to a counted number. False when that is below what open checkouts hold, or
    /// when the count changed between reading it and writing it (nothing is written either way).
    /// </summary>
    public static async Task<bool> SetToAsync(
        BenDataContext db, Guid variantId, int onHand, StoreStockReason reason, string? note,
        Guid? actorAppUserId, DateTime now, CancellationToken ct = default)
    {
        if (onHand < 0) return false;

        var current = await db.StoreProductVariants.AsNoTracking()
            .Where(v => v.Id == variantId).Select(v => (int?)v.StockOnHand).SingleOrDefaultAsync(ct);
        if (current is null) return false;
        if (current == onHand) return true;

        var was = current.Value;
        var rows = await db.StoreProductVariants
            .Where(v => v.Id == variantId && v.StockOnHand == was && onHand >= v.StockReserved)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StockOnHand, onHand), ct);
        if (rows != 1) return false;

        await RecordAsync(db, variantId, onHand - was, reason, note, null, null, actorAppUserId, now, ct);
        return true;
    }

    /// <summary>
    /// Applies a list of changes all together or not at all. Returns null when every line applied,
    /// otherwise the sentence naming the first line that could not — and nothing has moved.
    /// </summary>
    public static async Task<string?> AdjustManyAsync(
        BenDataContext db, IReadOnlyList<StoreStockChange> changes, StoreStockReason reason, string? note,
        Guid? actorAppUserId, DateTime now, CancellationToken ct = default)
    {
        // Its own transaction, or a savepoint inside the caller's; InMemory has neither.
        var relational = db.Database.IsRelational();
        var outer = relational ? db.Database.CurrentTransaction : null;
        await using var own = relational && outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
        const string savepoint = "store_stock_batch";
        if (outer is not null) await outer.CreateSavepointAsync(savepoint, ct);

        foreach (var change in changes)
        {
            bool applied;
            if (change.SetTo is { } setTo)
                applied = await SetToAsync(db, change.VariantId, setTo, reason, note, actorAppUserId, now, ct);
            else if (change.Delta is { } delta and not 0)
                applied = await AdjustAsync(db, change.VariantId, delta, reason, note, actorAppUserId, now, ct);
            else
                continue;

            if (applied) continue;

            if (own is not null) await own.RollbackAsync(ct);
            else if (outer is not null) await outer.RollbackToSavepointAsync(savepoint, ct);
            return await RefusalForAsync(db, change, ct);
        }

        if (own is not null) await own.CommitAsync(ct);
        else if (outer is not null) await outer.ReleaseSavepointAsync(savepoint, ct);
        return null;
    }

    private static async Task<string> RefusalForAsync(BenDataContext db, StoreStockChange change, CancellationToken ct)
    {
        var variant = await db.StoreProductVariants.AsNoTracking()
            .Where(v => v.Id == change.VariantId)
            .Select(v => new { v.Sku, v.StockOnHand, v.StockReserved })
            .SingleOrDefaultAsync(ct);
        if (variant is null) return "Nothing was changed — one of those items no longer exists.";

        var wanted = change.SetTo ?? variant.StockOnHand + (change.Delta ?? 0);
        return wanted < 0
            ? $"Nothing was changed — {variant.Sku} can't go below zero."
            : wanted < variant.StockReserved
                ? $"Nothing was changed — {variant.Sku} can't go below the {variant.StockReserved} held by open checkouts."
                : $"Nothing was changed — {variant.Sku} changed while you were editing. Reload and try again.";
    }

    private static async Task RecordAsync(
        BenDataContext db, Guid variantId, int delta, StoreStockReason reason, string? note,
        Guid? orderId, Guid? refundId, Guid? actorAppUserId, DateTime now, CancellationToken ct)
    {
        var after = await db.StoreProductVariants.AsNoTracking()
            .Where(v => v.Id == variantId).Select(v => v.StockOnHand).SingleAsync(ct);

        db.StoreStockMovements.Add(new StoreStockMovement
        {
            Id = Guid.NewGuid(),
            VariantId = variantId,
            Delta = delta,
            QuantityAfter = after,
            Reason = reason,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            OrderId = orderId,
            RefundId = refundId,
            ActorAppUserId = actorAppUserId,
            OccurredUtc = now,
        });
        await db.SaveChangesAsync(ct);
    }
}
