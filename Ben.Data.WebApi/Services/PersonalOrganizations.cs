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

    /// <summary>
    /// Why this thing may not be done inside a personal organization, or null when it may.
    /// </summary>
    /// <remarks>
    /// <para><b>What a solo plan sells is privacy over your own data and evidence</b> (Ben,
    /// 2026-08-31) — not a group in miniature. So the group-shaped machinery is not merely hidden
    /// from the screens, it is refused at the door: there is nobody to add, no client to open a
    /// case for, and no audience for a private investigation but the one person who made it.</para>
    ///
    /// <para><b>Refused here as well as hidden in the UI, and both matter.</b> Hiding alone leaves
    /// the endpoint open to anybody who guesses the URL. Refusing alone leaves a person clicking a
    /// button that always fails, which reads as the site being broken — the standing rule that a
    /// server guard needs a UI path, applied in its other direction.</para>
    ///
    /// <para>The sentences say why rather than only no, because "not available on your plan" sends
    /// somebody to the pricing page to buy something that would not help.</para>
    /// </remarks>
    public static string? WhyNotInAPersonalOrganization(Organization organization, PersonalAction action)
    {
        if (!organization.IsPersonal) return null;

        return action switch
        {
            PersonalAction.CreateCase =>
                "Cases are how a group takes on somebody else's haunting. A solo plan covers your "
                + "own investigating and keeps your data private; it does not take client work.",
            PersonalAction.CreatePrivateInvestigation =>
                "A solo plan's investigations are public ones. Your readings, recordings and "
                + "evidence stay private on your account — it is the investigation record itself "
                + "that has no separate audience to be private from.",
            _ => null,
        };
    }

    /// <summary>The group-shaped things a personal organization does not do.</summary>
    public enum PersonalAction
    {
        // AddMembers deliberately does NOT live here. "May this organization take another
        // member" is one question with one answer — PaidPlan.WhyCannotAddMemberAsync — and it is
        // about the plan, not about being personal: a solo person who pays may work with somebody,
        // at which point their organization stops being personal and becomes a group. Two rules
        // answering one question is how the two come to disagree.

        /// <summary>Opening a case — client work, which a solo plan does not cover.</summary>
        CreateCase = 2,

        /// <summary>An investigation with any visibility other than Public.</summary>
        CreatePrivateInvestigation = 3,
    }

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
