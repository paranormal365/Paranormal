using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Whether somebody is covered by a plan that is actually being paid for, and what that buys.
/// </summary>
/// <remarks>
/// <para><b>One definition of "paid", because there are now several callers and they must not
/// drift.</b> Storage, archive privacy and group membership all ask the same question, and a
/// version of it that answered differently in one place would show up as a feature that works
/// until you look at it from another screen.</para>
///
/// <para><b>Active, not merely present.</b> A Lapsed subscription is not a paid plan — otherwise
/// letting one expire would be a way to keep everything it bought, forever.</para>
/// </remarks>
public static class PaidPlan
{
    /// <summary>True when an active subscription covers this person through some group.</summary>
    public static Task<bool> CoversAsync(BenDataContext db, Guid appUserId, CancellationToken ct)
        => db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == appUserId && m.IsActive)
            .AnyAsync(m => db.OrganizationSubscriptions
                .Any(s => s.OrganizationId == m.OrganizationId
                       && s.Status == SubscriptionStatus.Active), ct);

    /// <summary>True when this group itself is on an active subscription.</summary>
    public static Task<bool> CoversOrganizationAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.OrganizationSubscriptions.AsNoTracking()
            .AnyAsync(s => s.OrganizationId == organizationId
                        && s.Status == SubscriptionStatus.Active, ct);

    /// <summary>
    /// Why this person may not keep a field session to themselves, or null when they may.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the free lane's paywall, and it is the honest one</b> (Ben, 2026-08-31):
    /// free means your findings join the public archive, paid means your work is yours. One
    /// person's readings are an anecdote; a location recorded by eleven people is a persistent
    /// artifact or a demonstrated absence of one, and the free tier is what fills it.</para>
    ///
    /// <para><b>What it gates is RETRACTION, not publication.</b> Publishing stays a deliberate
    /// act somebody performs — auto-publishing would make it a side effect, which the archive's
    /// own design rules out, and would put unreviewed media live by default. What a free account
    /// cannot do is publish and then pull it back, which is the whole exploit: take the credit,
    /// then hide the evidence. Deciding not to publish in the first place is not gaming anything
    /// — it is bounded by the free storage cap, which is what makes a private vault unattractive
    /// rather than forbidden.</para>
    /// </remarks>
    public static async Task<string?> WhyCannotKeepPrivateAsync(
        BenDataContext db, Guid appUserId, CancellationToken ct)
        => await CoversAsync(db, appUserId, ct)
            ? null
            : "Keeping your sessions private is part of a paid plan. On a free account, what you "
            + "publish to a place's archive stays there — it is what makes the archive worth "
            + "reading. A paid plan lets you keep your work to yourself.";

    /// <summary>
    /// Why this group may not take on another member, or null when it may.
    /// </summary>
    /// <remarks>
    /// <para><b>One person is free; working with other people is the paid part</b> (Ben,
    /// 2026-08-31). The count is of members it already has, so the FIRST person never meets this
    /// and the second is what asks for a plan.</para>
    ///
    /// <para><b>Nobody is ever removed by this.</b> It refuses an addition and touches nothing
    /// that already exists — a group that had members before it was written keeps every one of
    /// them, and keeps working exactly as it did. A rule that retroactively evicted people would
    /// be a very expensive way to make a point.</para>
    /// </remarks>
    public static async Task<string?> WhyCannotAddMemberAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        if (await CoversOrganizationAsync(db, organizationId, ct)) return null;

        var members = await db.OrganizationUserMemberships.AsNoTracking()
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);

        return members < 1
            ? null
            : "Working with other people is part of a paid plan — a free group is just you. "
            + "Everybody already here stays; adding somebody new needs a plan.";
    }
}
