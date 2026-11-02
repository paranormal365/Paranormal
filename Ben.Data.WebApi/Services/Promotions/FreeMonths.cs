using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Promotions;

/// <summary>
/// Free months on a group's plan: referrals and the founding groups' prizes (Ben, 11/02/2026). See
/// <see cref="OrganizationFreeMonths"/>.
/// </summary>
public static class FreeMonths
{
    /// <summary>How long a referred group's first payment stands before the month is earned, so a refund can come first.</summary>
    public static readonly TimeSpan PaymentSettles = TimeSpan.FromDays(14);

    /// <summary>
    /// Gives months once: false when the key was already given. The caller saves.
    /// </summary>
    public static async Task<bool> GiveAsync(
        BenDataContext db, Guid organizationId, FreeMonthReason reason, int months, string key, DateTime now,
        Guid byUserId, CancellationToken ct, Guid? sourceOrganizationId = null, Guid? promotionEntryId = null, string? note = null)
    {
        if (months <= 0) return false;
        if (await db.OrganizationFreeMonths.AnyAsync(m => m.Key == key, ct)
            || db.OrganizationFreeMonths.Local.Any(m => m.Key == key)) return false;

        db.OrganizationFreeMonths.Add(new OrganizationFreeMonths
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, Reason = reason, Months = months, Key = key,
            EarnedUtc = now, SourceOrganizationId = sourceOrganizationId, PromotionEntryId = promotionEntryId, Note = note,
            DateCreated = now, CreatedByAppUserId = byUserId,
        });
        return true;
    }

    /// <summary>
    /// Adds waiting months to every plan that's running: its period end moves that many months later, so a free
    /// year runs longer and a paying group's next renewal comes later. A group with no plan running, or one past
    /// its period end waiting on a renewal, keeps them until it has one. Returns how many were used; saves.
    /// </summary>
    public static async Task<int> ApplyWaitingAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var waiting = await db.OrganizationFreeMonths
            .Where(m => m.AppliedUtc == null && m.RevokedUtc == null)
            .OrderBy(m => m.EarnedUtc)
            .ToListAsync(ct);

        var used = 0;
        foreach (var months in waiting)
        {
            var sub = await db.OrganizationSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == months.OrganizationId, ct);
            if (sub is not { Status: SubscriptionStatus.Active, CurrentPeriodEnd: { } end } || end <= now) continue;

            sub.CurrentPeriodEnd = end.AddMonths(months.Months);
            sub.DateUpdated = now;
            months.AppliedUtc = now;
            months.AppliedToPeriodEndUtc = sub.CurrentPeriodEnd;
            months.DateUpdated = now;
            await db.SaveChangesAsync(ct);
            used++;
        }
        return used;
    }

    // ── referrals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Why this group can't name that one as its referrer, in words; null when it can. Fixed once the group has paid.
    /// </summary>
    public static async Task<string?> WhyCantNameReferrerAsync(
        BenDataContext db, Guid organizationId, Guid referrerId, CancellationToken ct)
    {
        if (referrerId == organizationId) return "A group can't refer itself.";
        if (!await db.Organizations.AnyAsync(o => o.Id == referrerId, ct)) return "That group couldn't be found.";
        if (await HasPaidAsync(db, organizationId, ct))
            return "This group has already paid for its plan, so the group that referred it can't be changed.";

        var leaders = new[] { OrganizationMemberRole.Owner, OrganizationMemberRole.Administrator };
        var ours = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.IsActive && leaders.Contains(m.Role))
            .Select(m => m.AppUserId).ToListAsync(ct);
        var shared = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == referrerId && m.IsActive && leaders.Contains(m.Role) && ours.Contains(m.AppUserId), ct);
        if (shared) return "That group shares an owner or administrator with this one, so it can't be its referrer.";

        return null;
    }

    /// <summary>Whether the group has made a real payment for a plan period.</summary>
    public static Task<bool> HasPaidAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => FirstPlanPayments(db).AnyAsync(e => e.OrganizationId == organizationId, ct);

    private static IQueryable<BillingLedgerEntry> FirstPlanPayments(BenDataContext db)
        => db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.Kind == BillingLedgerKind.Payment && e.Amount > 0 && e.PeriodStart != null && e.OrganizationId != null);

    /// <summary>
    /// Earns the referrer its month for every referred group whose first real payment has stood for
    /// <see cref="PaymentSettles"/>. One month per referred group, ever. Returns how many were earned; the caller saves.
    /// </summary>
    public static async Task<int> EarnReferralMonthsAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var settled = now - PaymentSettles;
        var due = await db.Organizations.AsNoTracking()
            .Where(o => o.ReferredByOrganizationId != null
                     && FirstPlanPayments(db).Any(e => e.OrganizationId == o.Id && e.DateCreated <= settled)
                     && !db.OrganizationFreeMonths.Any(m => m.Reason == FreeMonthReason.Referral && m.SourceOrganizationId == o.Id))
            .Select(o => new { o.Id, o.Name, Referrer = o.ReferredByOrganizationId!.Value })
            .ToListAsync(ct);

        var earned = 0;
        foreach (var o in due)
        {
            if (!await db.Organizations.AnyAsync(r => r.Id == o.Referrer, ct)) continue;
            if (await GiveAsync(db, o.Referrer, FreeMonthReason.Referral, 1, ReferralKey(o.Id), now, Guid.Empty, ct,
                    sourceOrganizationId: o.Id, note: $"{o.Name} named you as the group that referred it, and paid for its plan."))
                earned++;
        }
        return earned;
    }

    public static string ReferralKey(Guid referredOrganizationId) => $"referral:{referredOrganizationId}";
}
