using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The number a buyer quotes — "Order #100231" (storefront plan §5.4).
/// </summary>
/// <remarks>
/// <para>Max plus one, with the unique index on <c>StoreOrders.OrderNumber</c> as the referee: two
/// checkouts that read the same maximum both try the same number, one save is refused, and that
/// one reads again and takes the next. The same dance the billing ledger's receipt numbers do
/// (<c>StripeFulfillmentService</c>).</para>
///
/// <para>Starts at 100001 so the first order does not announce itself as the store's first.</para>
/// </remarks>
public static class StoreOrderNumbers
{
    public const int First = 100001;

    public static async Task<int> NextAsync(BenDataContext db, CancellationToken ct = default)
    {
        var max = await db.StoreOrders.MaxAsync(o => (int?)o.OrderNumber, ct);
        return max is null ? First : Math.Max(First, max.Value + 1);
    }

    /// <summary>
    /// Numbers <paramref name="order"/> (already added to the context) and saves, taking the next
    /// number when another checkout got there first. Saves everything else the context is tracking
    /// with it.
    /// </summary>
    public static async Task SaveNumberedAsync(BenDataContext db, StoreOrder order, CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            order.OrderNumber = await NextAsync(db, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                // The failed rows stay Added; the next pass renumbers and saves them again.
            }
        }
    }
}
