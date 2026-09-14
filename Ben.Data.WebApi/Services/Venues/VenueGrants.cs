using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Venues;

/// <summary>
/// Whether a venue on this site has said yes to an event, in the words the organizer is shown
/// (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>What the venue said yes to is dates at its address for one group</b>, and every one of
/// those three is checked. A grant for the Thomas House is no use at the Orpheum, a grant to one
/// society is no use to another that heard about it, and a yes for Friday to Sunday does not cover
/// the Monday somebody added afterwards.</para>
///
/// <para>Pure apart from <see cref="VerifiedVenueAtAsync"/>, so the rule is tested without a
/// database.</para>
/// </remarks>
public static class VenueGrants
{
    /// <summary>The verified venue at a place, with its group, or null when nobody has proved it.</summary>
    public static Task<OrganizationVenueProfile?> VerifiedVenueAtAsync(
        BenDataContext db, Guid placeId, CancellationToken ct)
        => db.OrganizationVenueProfiles.AsNoTracking()
            .Include(v => v.Organization)
            .FirstOrDefaultAsync(v => v.PlaceId == placeId && v.VerifiedUtc != null, ct);

    /// <summary>
    /// What the readiness list needs to know about the venue at this event's place, or null when
    /// there is no verified venue there or it is the event's own group.
    /// </summary>
    /// <remarks>
    /// A grant from some OTHER group is ignored rather than trusted: if the place's verified venue
    /// has changed hands since the yes was given, the yes was given by somebody who no longer
    /// speaks for the building.
    /// </remarks>
    public static async Task<Events.HostedEventReadiness.VenueOnTheSite?> ForEventAsync(
        BenDataContext db, HostedEvent hosted, CancellationToken ct)
    {
        var venue = await VerifiedVenueAtAsync(db, hosted.PlaceId, ct);
        if (venue is null || venue.OrganizationId == hosted.OrganizationId) return null;

        var grant = hosted.VenueGrantId is Guid grantId
            ? await db.OrganizationVenueGrants.AsNoTracking().FirstOrDefaultAsync(g => g.Id == grantId, ct)
            : null;
        if (grant is not null && grant.VenueOrganizationId != venue.OrganizationId) grant = null;

        var asked = await db.VenueHostingRequests.AsNoTracking()
            .Where(r => r.HostedEventId == hosted.Id
                     && r.VenueOrganizationId == venue.OrganizationId
                     && r.Status == Ben.Data.Common.Enums.VenueHostingRequestStatus.Pending)
            .Select(r => (DateTime?)r.DateCreated)
            .FirstOrDefaultAsync(ct);

        return new(venue.OrganizationId, venue.Organization.Name, grant, asked);
    }

    /// <summary>
    /// Why this grant does not let this event happen, or null when it does.
    /// </summary>
    /// <param name="nightDates">The event's nights as calendar dates at the venue.</param>
    public static string? WhyItDoesNotCover(
        OrganizationVenueGrant grant, Guid eventOrganizationId, Guid eventPlaceId,
        IEnumerable<DateTime> nightDates, string venueName)
    {
        if (grant.RevokedUtc is not null)
            return grant.RevokedReason is { Length: > 0 } why
                ? $"{venueName} withdrew their yes: “{why}”"
                : $"{venueName} withdrew their yes.";

        if (grant.GranteeOrganizationId != eventOrganizationId)
            return $"{venueName} said yes to a different group, not to yours.";

        if (grant.PlaceId != eventPlaceId)
            return $"That yes from {venueName} was for a different address.";

        var outside = nightDates
            .Select(d => d.Date)
            .Where(d => d < grant.ValidFrom.Date || d > grant.ValidTo.Date)
            .OrderBy(d => d)
            .ToList();

        if (outside.Count > 0)
            return $"{venueName} said yes to {Span(grant)}, and this event now includes "
                 + $"{string.Join(", ", outside.Select(d => d.ToString("MM/dd/yyyy")))}. "
                 + "Ask them again for the new dates.";

        return null;
    }

    /// <summary>"10/30/2026–11/01/2026", or one date when the yes is for one day.</summary>
    public static string Span(OrganizationVenueGrant grant)
        => grant.ValidFrom.Date == grant.ValidTo.Date
            ? grant.ValidFrom.ToString("MM/dd/yyyy")
            : $"{grant.ValidFrom:MM/dd/yyyy}–{grant.ValidTo:MM/dd/yyyy}";

    /// <summary>
    /// The venue's group, when this event rests on a standing yes that lent its rooms. Null otherwise.
    /// </summary>
    public static async Task<Guid?> RoomsLentToAsync(BenDataContext db, HostedEvent hosted, CancellationToken ct)
    {
        if (hosted.VenueGrantId is not Guid grantId) return null;

        var grant = await db.OrganizationVenueGrants.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == grantId, ct);

        return grant is { RevokedUtc: null, AllowRooms: true }
               && grant.PlaceId == hosted.PlaceId
               && grant.GranteeOrganizationId == hosted.OrganizationId
            ? grant.VenueOrganizationId
            : null;
    }

    /// <summary>Whether the grant is standing and today is inside it — for what it lends, not for publishing.</summary>
    public static bool IsStanding(OrganizationVenueGrant? grant)
        => grant is { RevokedUtc: null };
}
