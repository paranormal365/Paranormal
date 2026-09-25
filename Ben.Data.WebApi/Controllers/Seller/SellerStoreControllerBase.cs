using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>
/// The base of every seller endpoint (store sellers, backlog 251): a seller works only on their
/// own items.
/// </summary>
/// <remarks>
/// <para><b>Another seller's item is "not found", never "forbidden"</b> — the same rule as the
/// buyer's order doors. A 403 would confirm the item exists and whose it is.</para>
///
/// <para><b>Not behind the store switch.</b> Like the admin pages, a seller prepares items while
/// the shop is dark; the Seller role is the switch.</para>
/// </remarks>
public abstract class SellerStoreControllerBase : BenControllerBase
{
    /// <summary>The caller's own items — the only rows any seller endpoint starts from.</summary>
    protected IQueryable<StoreProduct> Mine(BenDataContext db)
    {
        var me = GetCurrentUserIdOrThrow();
        return db.StoreProducts.Where(p => p.SellerAppUserId == me);
    }

    /// <summary>One of the caller's items, tracked for an edit — or null, which the caller answers 404.</summary>
    protected Task<StoreProduct?> MineAsync(BenDataContext db, Guid id, CancellationToken ct)
        => Mine(db).FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <summary>The caller, editing as a seller: their changes read "You" to them and carry the Seller badge.</summary>
    protected StoreEditActor Seller => new(GetCurrentUserIdOrThrow(), StoreChangeActor.Seller);
}
