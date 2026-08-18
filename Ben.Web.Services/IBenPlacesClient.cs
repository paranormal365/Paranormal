using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// The Places slice of <see cref="IBenAdminClient"/> — places and the directions to them.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenPlacesClient
{
    // ── Places (Area 9) ───────────────────────────────────────────────────────

    /// <summary>One place, for the place page header and map.</summary>
    Task<PlaceRecord?> GetPlaceAsync(Guid placeId, CancellationToken token = default);

    /// <summary>
    /// Investigations at a place that the signed-in caller may see — their own group's, anything
    /// public, and anything shared with groups who have also investigated there.
    /// </summary>
    Task<IReadOnlyList<PlaceInvestigationRow>> GetPlaceInvestigationsAsync(
        Guid placeId, CancellationToken token = default);

    /// <summary>"N investigations by M groups since Y", counted over what this caller may see.</summary>
    Task<PlaceSummary?> GetPlaceSummaryAsync(Guid placeId, CancellationToken token = default);

    /// <summary>
    /// Places that are probably the one being typed in — "did you mean this?" before a duplicate
    /// exists.
    /// </summary>
    /// <remarks>
    /// Read-only. The server decides what counts as a probable match (same address, within a tenth
    /// of a mile); the caller shows the answer and the person picks. Returns nothing when there is
    /// neither an address nor a name to go on.
    /// </remarks>
    Task<IReadOnlyList<PlaceCandidate>> FindPlaceCandidatesAsync(
        string? street, string? city, string? state, string? zip, string? name,
        decimal? latitude, decimal? longitude, CancellationToken token = default);

    /// <summary>
    /// The visitor's view: the place plus only what has been published about it.
    /// </summary>
    /// <remarks>
    /// Anonymous, so the place page works signed-out. Never returns anything a signed-in call
    /// would have hidden — both go through the same server-side predicate.
    /// </remarks>
    Task<PublicPlaceResponse?> GetPublicPlaceAsync(Guid placeId, CancellationToken token = default);

    /// <summary>
    /// Hands one attendee the lead of this visit, or takes it back. Returns the whole roster.
    /// </summary>
    /// <remarks>
    /// Exclusive — naming a lead clears the previous holder, so the response carries every row
    /// rather than the one that was clicked. Leading is also an edit right, so the server requires
    /// the caller to already hold one.
    /// </remarks>
    Task<IReadOnlyList<InvestigationRosterEntry>> SetInvestigationLeadAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, bool isLead, CancellationToken token = default);

    /// <summary>Every account filed for an investigation. Any member of the group may read them.</summary>
    Task<IReadOnlyList<InvestigationFindingRecord>> GetInvestigationFindingsAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default);

    /// <summary>
    /// Files or revises the signed-in person's own account of a visit.
    /// </summary>
    /// <remarks>
    /// Attendees only, and only their own — there is no override. Whether somebody turned up is a
    /// fact another person can attest to; what they experienced is not.
    /// </remarks>
    Task<InvestigationFindingRecord?> SaveMyInvestigationFindingAsync(
        Guid orgId, Guid investigationId, string narrative, CancellationToken token = default);

    /// <summary>Withdraws the signed-in person's own account.</summary>
    Task<bool> DeleteMyInvestigationFindingAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default);

    /// <summary>Who is on an investigation's team and who has turned up. Any member may read it.</summary>
    Task<IReadOnlyList<InvestigationRosterEntry>> GetInvestigationRosterAsync(
        Guid orgId, Guid investigationId, CancellationToken token = default);

    /// <summary>
    /// Records the signed-in person's own arrival. <paramref name="statedArrivalTime"/> null means now.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="OverrideInvestigationAttendanceAsync"/> on purpose: this leaves the
    /// record self-reported, which is the provenance the roster shows.
    /// </remarks>
    Task<InvestigationRosterEntry?> CheckInToInvestigationAsync(
        Guid orgId, Guid investigationId, DateTime? statedArrivalTime = null, CancellationToken token = default);

    /// <summary>Records or corrects somebody else's attendance. Needs the right to manage the visit.</summary>
    Task<InvestigationRosterEntry?> OverrideInvestigationAttendanceAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, bool? didAttend,
        DateTime? statedArrivalTime = null, CancellationToken token = default);

    /// <summary>
    /// Schedules an investigation, with a case or without one. Needs a place when there is no case.
    /// </summary>
    /// <remarks>
    /// Returns the plain record the endpoint creates, not an <see cref="OrgInvestigationRow"/>:
    /// the row's denormalised place/case names and permission verdicts are list-view concerns, so
    /// callers that need them refetch the list rather than have the create path assemble a second,
    /// subtly different shape.
    /// </remarks>
    Task<InvestigationRecord?> CreateOrgInvestigationAsync(
        Guid orgId, CreateOrgInvestigationRequest request, CancellationToken token = default);
    Task<OrgCalendarEventTypeRecord?> CreateCalendarEventTypeAsync(Guid orgId, UpsertCalendarEventTypeRequest request, CancellationToken token = default);
    Task<OrgCalendarEventTypeRecord?> UpdateCalendarEventTypeAsync(Guid orgId, Guid id, UpsertCalendarEventTypeRequest request, CancellationToken token = default);
    Task<bool> DeleteCalendarEventTypeAsync(Guid orgId, Guid id, CancellationToken token = default);

    Task<IReadOnlyList<OrgCalendarEventRecord>> GetCalendarEventsAsync(Guid orgId, DateTime? from = null, DateTime? to = null, CancellationToken token = default);
    Task<OrgCalendarEventRecord?> GetCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default);
    Task<OrgCalendarEventRecord?> CreateCalendarEventAsync(Guid orgId, UpsertCalendarEventRequest request, CancellationToken token = default);

    /// <summary>
    /// Saves a calendar event and, when the server refuses, gives back <b>its reason</b>.
    /// </summary>
    /// <remarks>
    /// The public-event rules refuse with a sentence written to be read — a residence, a case link.
    /// The ordinary create/update swallow that into null, leaving the calendar able to say only
    /// "Save failed", which tells an organizer nothing about what to change.
    /// </remarks>
    Task<(OrgCalendarEventRecord? Result, string? Error)> SaveCalendarEventAsync(
        Guid orgId, Guid? eventId, UpsertCalendarEventRequest request, CancellationToken token = default);
    Task<OrgCalendarEventRecord?> UpdateCalendarEventAsync(Guid orgId, Guid eventId, UpsertCalendarEventRequest request, CancellationToken token = default);
    Task<bool> DeleteCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    Task<IReadOnlyList<OrgCalendarEventAttendeeRecord>> GetCalendarEventAttendeesAsync(Guid orgId, Guid eventId, CancellationToken token = default);
    Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeAsync(Guid orgId, Guid eventId, AddAttendeeRequest request, CancellationToken token = default);

    /// <summary>
    /// Invites someone to an event by email address — for people outside the organization.
    /// Returns null when nobody with that address has an account.
    /// </summary>
    Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeByEmailAsync(Guid orgId, Guid eventId, string email, CancellationToken token = default);
    Task<OrgCalendarEventAttendeeRecord?> RsvpCalendarEventAsync(Guid orgId, Guid eventId, Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus status, CancellationToken token = default);
    Task<bool> RemoveCalendarAttendeeAsync(Guid orgId, Guid eventId, Guid attendeeId, CancellationToken token = default);

    // ── Directions ────────────────────────────────────────────────────────────
    Task<DirectionsResult?> GetDirectionsAsync(double fromLat, double fromLon, double toLat, double toLon, CancellationToken token = default);

    // ── Published investigations (item #89) ─────────────────────────────────

    /// <summary>What an organization has published, most recent first. Anonymous.</summary>
    Task<IReadOnlyList<PublicInvestigationListItem>> GetPublishedInvestigationsAsync(
        string orgUrlName, CancellationToken token = default);

    /// <summary>One published investigation, by the address people share.</summary>
    Task<PublicInvestigationDetail?> GetPublishedInvestigationAsync(
        string orgUrlName, string investigationSlug, CancellationToken token = default);
}
