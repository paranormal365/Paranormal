using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The single answer to "may this person see this investigation".
/// </summary>
/// <remarks>
/// <para>One function on purpose. Sharing rules that live in several queries drift, and the way you
/// find out is that something private turns up on a page nobody thought was public. Every read of
/// another organization's investigations goes through <see cref="VisibleTo"/>, so the rules are in
/// one place and changing them is one edit.</para>
///
/// <para>That matters most for the decision this encodes: <see cref="InvestigationVisibility.PlaceInvestigators"/>
/// is <b>not reciprocal</b> — you do not have to share your own findings to read anyone else's.
/// If that turns out to be wrong, the fix is the <c>reciprocal</c> branch below and nothing else.</para>
/// </remarks>
public static class InvestigationVisibilityFilter
{
    /// <summary>
    /// A predicate for "investigations <paramref name="viewerOrgIds"/> may see", suitable for
    /// composing into a query.
    /// </summary>
    /// <param name="viewerOrgIds">Organizations the viewer is an active member of. Empty for a visitor.</param>
    /// <param name="placeIdsTheyHaveInvestigated">
    /// Places any of the viewer's organizations has investigated. Precomputed by the caller, since
    /// working it out per row is the difference between one query and hundreds.
    /// </param>
    public static Expression<Func<Investigation, bool>> VisibleTo(
        IReadOnlyCollection<Guid> viewerOrgIds,
        IReadOnlyCollection<Guid> placeIdsTheyHaveInvestigated)
    {
        return i =>
            // Your own group's work, whatever its scope. Nothing here restricts an organization's
            // view of itself.
            viewerOrgIds.Contains(i.OrganizationId)

            // Anyone at all, signed in or not.
            || i.Visibility == InvestigationVisibility.Public

            // Shared with whoever has also worked this place. Not reciprocal: having investigated
            // the place is the whole qualification, and publishing your own findings is not
            // required. Reversing that means adding the extra condition here, and only here.
            || (i.Visibility == InvestigationVisibility.PlaceInvestigators
                && i.PlaceId != null
                && placeIdsTheyHaveInvestigated.Contains(i.PlaceId.Value));
    }

    /// <summary>
    /// The places any of these organizations has investigated — the input to <see cref="VisibleTo"/>.
    /// </summary>
    public static async Task<List<Guid>> PlacesInvestigatedByAsync(
        BenDataContext db, IReadOnlyCollection<Guid> orgIds, CancellationToken ct)
    {
        if (orgIds.Count == 0) return [];

        return await db.Investigations.AsNoTracking()
            .Where(i => orgIds.Contains(i.OrganizationId) && i.PlaceId != null)
            .Select(i => i.PlaceId!.Value)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>
    /// The scope a new investigation starts at, given where it happened.
    /// </summary>
    /// <remarks>
    /// Chosen from the place rather than left to whoever clicks fastest, and chosen cautiously:
    /// somebody lives at a private residence and did not volunteer their home to an audience.
    /// </remarks>
    public static InvestigationVisibility DefaultFor(Place? place) => place?.Kind switch
    {
        PlaceKind.PublicLocation => InvestigationVisibility.PlaceInvestigators,
        // Private residence, or no place recorded yet — both stay with the group.
        _ => InvestigationVisibility.GroupOnly,
    };

    /// <summary>
    /// Whether a scope may be applied to an investigation at this place, and why not if it may not.
    /// </summary>
    /// <returns>An error message for a 400, or null when the choice is allowed.</returns>
    public static string? Reject(InvestigationVisibility visibility, Place? place)
    {
        if (visibility == InvestigationVisibility.Public
            && place?.Kind == PlaceKind.PrivateResidence)
        {
            // Publishing what happened inside somebody's home is theirs to agree to, and there is
            // no mechanism yet for asking. Withholding the option beats offering one that quietly
            // skips the consent.
            return "An investigation at a private residence cannot be made public. "
                 + "Share it with the group, or with others who have investigated the same place.";
        }

        if (visibility == InvestigationVisibility.PlaceInvestigators && place is null)
        {
            // The audience is defined by the place, so without one it has no members and the
            // setting would silently behave as group-only.
            return "This investigation has no location yet, so it cannot be shared with others who "
                 + "have investigated the same place. Set where it happened first.";
        }

        return null;
    }
}
