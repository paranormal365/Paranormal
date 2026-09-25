using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;

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
}
