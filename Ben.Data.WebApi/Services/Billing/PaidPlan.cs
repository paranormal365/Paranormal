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
    /// <para><b>This sentence reaches the iPhone app verbatim</b> and therefore says nothing about
    /// a plan, a price or where one is bought. App Review reads wording that points at a purchase
    /// made outside the app under Guideline 3.1.1, and the app sells nothing — so the refusal
    /// describes the rule and stops. The website is free to explain the plan beside it, in its own
    /// markup, which no phone ever renders (see <c>MyFieldSessions.razor</c>).</para>
    /// </remarks>
    public static async Task<string?> WhyCannotKeepPrivateAsync(
        BenDataContext db, Guid appUserId, CancellationToken ct)
        => await CoversAsync(db, appUserId, ct)
            ? null
            : "Publishing to a place's archive cannot be undone on this account. What you publish "
            + "stays there — it is what makes the archive worth reading.";

    /// <summary>
    /// Whether everything this group records at a public place is public because it pays nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-17:</b> "Everything a solo person submits is going to be public by
    /// default… if paid, they can make their work private. By default we should be able to collect
    /// information as public for unpaid plan." The same bargain
    /// <see cref="WhyCannotKeepPrivateAsync"/> already strikes over field sessions, applied to the
    /// cases and investigations at a public place.</para>
    ///
    /// <para><b>Keyed on the plan, never on <c>IsPersonal</c>.</b> Those are different questions and
    /// the old gates asked the wrong one: a paid solo subscriber was refused a case outright while an
    /// unpaid group of one could open one and keep it to itself — backwards in both directions. A
    /// personal organization is an organization; what it pays is what decides this.</para>
    ///
    /// <para><b>The other half of the rule lives in the places it governs</b>, because "public by
    /// default" is a decision about one case or one investigation and only its own controller knows
    /// which place it is at. A private residence is never touched by this: that is the paid lane, and
    /// nobody is publishing somebody's home for want of a subscription.</para>
    /// </remarks>
    public static async Task<bool> PublicByDefaultAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
        => !await CoversOrganizationAsync(db, organizationId, ct);

    /// <summary>
    /// Why this group may not keep a case at a public place to itself, or null when it may.
    /// </summary>
    /// <remarks>
    /// Asked at the moment a case would stop being public, not on every save: a case that is already
    /// private stays private, the way the member cap never evicted anybody. Says nothing about a
    /// price or where one is bought, for the reason given on <see cref="WhyCannotKeepPrivateAsync"/> —
    /// these sentences reach the phone one day.
    /// </remarks>
    public static async Task<string?> WhyCannotKeepCasePrivateAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
        => await PublicByDefaultAsync(db, organizationId, ct)
            ? "On this account a case at a public location is public. What you record there joins "
            + "the place's own page, which is what makes it worth reading."
            : null;

    /// <summary>
    /// Why this group may not narrow an investigation at a public place, or null when it may.
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="WhyCannotKeepCasePrivateAsync"/>, worded for the scope control
    /// rather than the publish box. Both are one question — does this account pay — and both are
    /// asked here so the two answers cannot drift apart.
    /// </remarks>
    public static async Task<string?> WhyCannotNarrowInvestigationAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
        => await PublicByDefaultAsync(db, organizationId, ct)
            ? "On this account a visit to a public location is shared with everyone. What you find "
            + "there joins the place's own page, which is what makes it worth reading."
            : null;

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
