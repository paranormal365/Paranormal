using System.Linq.Expressions;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// The one place that says a personal organization is not a group anybody may find or join.
/// </summary>
/// <remarks>
/// <para><b>Why this is a shared expression rather than <c>Where(o =&gt; !o.IsPersonal)</c> at
/// each call site.</b> The rule has to hold in every listing at once, and the failure is silent
/// and asymmetric: forget it in one place and a person who bought a solo plan appears in a public
/// directory as a group. That does not merely look wrong — it publishes the fact that a named
/// individual subscribed, which they never agreed to. A shared expression means the rule is
/// stated once and a new listing can be pointed at it, and it gives
/// <c>PersonalOrganizationsAreHiddenTests</c> a name to scan the source for.</para>
///
/// <para><b>It hides them from directories, not from their owner.</b> A personal organization is
/// a real organization: its owner sees it wherever they see their own memberships, its billing
/// page works, its cases work. What it must never be is a search result, a browse row, a nearby
/// pin, or a promoted card. "Their own" and "everybody's" are different questions, and only the
/// second one is filtered.</para>
///
/// <para><b>Hiding is now the ONLY thing this class does</b> (2026-09-17). It used to refuse a
/// personal organization two actions as well; those were keyed on being personal when they meant
/// to ask about the plan, and <c>PaidPlan</c> asks that properly. See the note above
/// <see cref="Discoverable"/>.</para>
///
/// <para><b>Admin surfaces are deliberately NOT filtered.</b> A SuperAdmin looking at every
/// organization must see these — they carry subscriptions and money, and a billing screen that
/// silently omitted a paying customer would be worse than one that shows a row somebody has to
/// understand. The distinction is presenting-as-a-group versus administering-the-platform.</para>
/// </remarks>
public static class PersonalOrganizations
{
    /// <summary>
    /// Organizations that may appear where groups are presented to be found or joined.
    /// </summary>
    /// <remarks>
    /// Written as an expression so EF translates it into the same SQL a hand-written clause
    /// would, with no client-side evaluation and no cost for using the shared form.
    /// </remarks>
    /// <remarks>
    /// Two reasons a group may not appear, answered together: it is one person's subscription and
    /// never was a group (<c>IsPersonal</c>), or it is a real group that has chosen not to be found
    /// (<c>IsUnlisted</c>). One question with two causes belongs in one predicate — split across
    /// two, they drift, and the drift is invisible until somebody turns up in a directory who
    /// should not be there.
    /// </remarks>
    public static Expression<Func<Organization, bool>> Discoverable =>
        o => !o.IsPersonal && !o.IsUnlisted;

    // ── There are no per-action refusals here any more (Ben, 2026-09-17) ─────────────────────
    //
    // This class used to refuse two things to a personal organization outright: opening a case
    // ("a solo plan does not take client work") and scheduling anything but a public
    // investigation. Both were keyed on IsPersonal — a fact about the RECORD — when the question
    // they were reaching for was about the PLAN.
    //
    // Ben's rule of 2026-09-17 settles it: "if paid, they can make their work private. By default
    // we should be able to collect information as public for unpaid plan." Keyed that way, the old
    // gates were backwards in both directions at once. A paid solo subscriber — exactly the person
    // the solo band is sold to — could not open a case at all, while an unpaid group of one could
    // open one and keep it entirely to itself. So they are gone, and PaidPlan answers instead:
    // PublicByDefaultAsync, WhyCannotKeepCasePrivateAsync, WhyCannotNarrowInvestigationAsync.
    //
    // What stays here is the ONE thing that really is about being personal rather than about
    // money: a personal organization is nobody's group and must never appear where groups are
    // presented to be found or joined. That is Discoverable, below, and it is untouched.
    //
    // AddMembers never lived here either, for the same reason, and the note explaining why was
    // right the whole time: "May this organization take another member" is one question with one
    // answer — PaidPlan.WhyCannotAddMemberAsync — and it is about the plan, not about being
    // personal. Two rules answering one question is how the two come to disagree.

    /// <summary>
    /// The same rule for a query that has already projected past the organization — a membership
    /// row, an investigation, anything reaching its organization by navigation.
    /// </summary>
    public static Expression<Func<T, bool>> DiscoverableVia<T>(
        Expression<Func<T, Organization>> organization)
    {
        // Rebuilds the Discoverable predicate against a navigation property, so callers keep one
        // rule rather than remembering which flags carry it.
        var parameter = organization.Parameters[0];
        var notPersonal = Expression.Not(
            Expression.Property(organization.Body, nameof(Organization.IsPersonal)));
        var notUnlisted = Expression.Not(
            Expression.Property(organization.Body, nameof(Organization.IsUnlisted)));
        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(notPersonal, notUnlisted), parameter);
    }
}
