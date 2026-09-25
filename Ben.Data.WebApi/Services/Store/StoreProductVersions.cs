using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What happens to the old version of an item when its new version first goes on sale (store sellers,
/// backlog 251, P13) — the seller's choice (Ben's list): keep offering it, sell out what's left, or
/// discontinue it. And the links each way, for the pages.
/// </summary>
public static class StoreProductVersions
{
    /// <summary>
    /// Applies <paramref name="product"/>'s policy to the version before it, once — on the day it first
    /// goes on sale. Unsaved: the caller's save carries it with the switch. Returns the old version's id
    /// when anything was done.
    /// </summary>
    public static async Task<Guid?> ApplyPolicyAsync(BenDataContext db, StoreProduct product, StoreEditActor actor, DateTime now, CancellationToken ct)
    {
        if (product.PreviousVersionProductId is not { } previousId || product.SupersededAppliedUtc is not null) return null;
        var previous = await db.StoreProducts.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == previousId, ct);
        product.SupersededAppliedUtc = now;
        if (previous is null) return null;

        var name = product.VersionLabel is { } l ? $"Version {l}" : "The new version";
        switch (product.SupersededPolicy)
        {
            case StoreSupersededPolicy.Discontinue:
                Discontinue(db, previous, $"{name} went on sale, replacing this one — taken off sale.", actor.UserId, actor.Role, now);
                break;
            case StoreSupersededPolicy.SellOut when previous.IsActive && !previous.Variants.Any(v => v.IsActive && v.StockOnHand > 0):
                Discontinue(db, previous, $"{name} went on sale, and this one had nothing left to sell — taken off sale.", actor.UserId, actor.Role, now);
                break;
            case StoreSupersededPolicy.SellOut:
                previous.SellingOutSinceUtc = now;
                StoreProductHistory.Record(db, previous.Id, StoreProductChangeArea.Versions,
                    $"{name} went on sale. This one sells what's left, then comes off sale.", actor.UserId, actor.Role, now);
                break;
            default:
                StoreProductHistory.Record(db, previous.Id, StoreProductChangeArea.Versions,
                    $"{name} went on sale. This one stays on sale beside it.", actor.UserId, actor.Role, now);
                break;
        }
        return previous.Id;
    }

    private static void Discontinue(BenDataContext db, StoreProduct previous, string sentence, Guid? actorId, StoreChangeActor role, DateTime now)
    {
        previous.IsActive = false;
        previous.DiscontinuedUtc = now;
        previous.SellingOutSinceUtc = null;
        previous.DateUpdated = now;
        StoreProductHistory.Record(db, previous.Id, StoreProductChangeArea.Versions, sentence, actorId, role, now);
    }

    /// <summary>
    /// Takes off sale every replaced version that was selling out and has nothing left — the
    /// "sell out" policy's second half (<see cref="Scheduling.StoreSupersededSweepJob"/>). Returns how many.
    /// </summary>
    /// <remarks>
    /// "Nothing left" is nothing on hand — not "nothing available": a unit a checkout is holding may
    /// come back, and an item taken off sale under a buyer paying for it would be worse than a day's wait.
    /// </remarks>
    public static async Task<int> SweepSoldOutAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var selling = await db.StoreProducts.Include(p => p.Variants)
            .Where(p => p.SellingOutSinceUtc != null && p.IsActive)
            .ToListAsync(ct);
        var gone = selling.Where(p => !p.Variants.Any(v => v.IsActive && v.StockOnHand > 0)).ToList();
        foreach (var p in gone)
            Discontinue(db, p, "Sold out after a newer version replaced it — taken off sale.", null, StoreChangeActor.Store, now);
        if (gone.Count > 0) await db.SaveChangesAsync(ct);
        return gone.Count;
    }

    /// <summary>An item's place among its versions, for its editors.</summary>
    public static async Task<StoreVersionInfo> InfoAsync(BenDataContext db, StoreProduct product, CancellationToken ct)
    {
        var previous = product.PreviousVersionProductId is { } p
            ? await db.StoreProducts.AsNoTracking().Where(x => x.Id == p).Select(x => new StoreVersionLink(x.Id, x.Name, x.Slug, x.VersionLabel)).FirstOrDefaultAsync(ct)
            : null;
        var next = await db.StoreProducts.AsNoTracking().Where(x => x.PreviousVersionProductId == product.Id)
            .Select(x => new StoreVersionLink(x.Id, x.Name, x.Slug, x.VersionLabel)).FirstOrDefaultAsync(ct);
        return new StoreVersionInfo(product.VersionLabel, product.SupersededPolicy, product.SupersededAppliedUtc is not null, previous, next,
            product.SellingOutSinceUtc, product.DiscontinuedUtc);
    }
}
