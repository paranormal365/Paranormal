using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Joins orders a person placed as a guest to their account (storefront S6.1, plan decision "Guest
/// orders after sign-up") — so they appear under My Orders and the person may review what they bought.
/// </summary>
/// <remarks>
/// <para><b>A confirmed email is the proof</b> — the same proof the guest link relies on: whoever
/// reads that mailbox already holds every guest order sent to it. An account whose email is not
/// confirmed joins nothing.</para>
///
/// <para><b>Joined when it is needed, not at sign-in.</b> Password sign-in is Identity's own endpoint
/// with no hook of ours, so the join runs where the orders are read — My Orders, a product's review
/// state, writing a review — and again at email confirmation. One conditional update: only paid,
/// unowned orders with that email, and never one waiting to be scrubbed for a closed account.</para>
/// </remarks>
public static class StoreGuestOrders
{
    /// <returns>How many orders were joined.</returns>
    public static async Task<int> AttachAsync(BenDataContext db, Guid userId, CancellationToken ct = default)
    {
        var user = await db.AppUsers.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.EmailConfirmed }).FirstOrDefaultAsync(ct);
        if (user is not { EmailConfirmed: true, Email: { Length: > 0 } email }) return 0;

        var normalized = StoreEmail.Normalize(email);
        return await db.StoreOrders
            .Where(o => o.BuyerAppUserId == null && o.BuyerEmailNormalized == normalized && o.PaidUtc != null
                     && o.PendingAnonymisationSinceUtc == null && o.ShipStreet1 != StoreOrderScrub.RemovedStreet)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.BuyerAppUserId, userId), ct);
    }
}
