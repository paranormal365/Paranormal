using Ben.Web.Services.WebApi;
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

    // ── Rooms inside a place (item 197) ───────────────────────────────────────
    //
    // Per ORGANIZATION as well as per place: a Place is shared, so two groups describing the same
    // building keep separate lists and neither can edit the other's.

    /// <summary>The rooms this group has named in this place, in the order it arranged them.</summary>
    Task<LoadResult<PlaceRoomRecord>> GetPlaceRoomsAsync(Guid orgId, Guid placeId, CancellationToken token = default);

    /// <summary>Names a room. Null when the name is taken or the caller may not.</summary>
    Task<PlaceRoomRecord?> CreatePlaceRoomAsync(Guid orgId, Guid placeId, SavePlaceRoomRequest request, CancellationToken token = default);

    /// <summary>Edits a room.</summary>
    Task<PlaceRoomRecord?> UpdatePlaceRoomAsync(Guid orgId, Guid placeId, Guid roomId, SavePlaceRoomRequest request, CancellationToken token = default);

    /// <summary>Removes a room.</summary>
    Task<bool> DeletePlaceRoomAsync(Guid orgId, Guid placeId, Guid roomId, CancellationToken token = default);

    /// <summary>One place, for the place page header and map.</summary>
    Task<PlaceRecord?> GetPlaceAsync(Guid placeId, CancellationToken token = default);

    /// <summary>
    /// Investigations at a place that the signed-in caller may see — their own group's, anything
    /// public, and anything shared with groups who have also investigated there.
    /// </summary>
    Task<LoadResult<PlaceInvestigationRow>> GetPlaceInvestigationsAsync(
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
    Task<LoadResult<PlaceCandidate>> FindPlaceCandidatesAsync(
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
    Task<(IReadOnlyList<InvestigationRosterEntry> Roster, string? Error)> SetInvestigationLeadAsync(
        Guid orgId, Guid investigationId, Guid attendeeId, bool isLead, CancellationToken token = default);

    /// <summary>Every account filed for an investigation. Any member of the group may read them.</summary>
    Task<LoadResult<InvestigationFindingRecord>> GetInvestigationFindingsAsync(
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
    // ── Investigation duties + case contacts (item 158) ──────────────────────
    Task<InvestigationDutyBoard?> GetInvestigationDutyBoardAsync(Guid orgId, Guid investigationId, CancellationToken token = default);
    Task<(InvestigationDutyBoard? Board, string? Refusal)> AssignInvestigationDutyAsync(Guid orgId, Guid investigationId, Guid attendeeId, Guid dutyId, bool overrideEligibility, CancellationToken token = default);
    Task<InvestigationDutyBoard?> UnassignInvestigationDutyAsync(Guid orgId, Guid investigationId, Guid attendeeId, Guid dutyId, CancellationToken token = default);
    Task<LoadResult<OrgInvestigationDutyItem>> GetInvestigationDutiesAsync(Guid orgId, CancellationToken token = default);
    Task<OrgInvestigationDutyItem?> CreateInvestigationDutyAsync(Guid orgId, string name, int sortOrder, bool isActive, bool isSingleHolder, Guid? minimumLevelId, CancellationToken token = default);
    Task<OrgInvestigationDutyItem?> UpdateInvestigationDutyAsync(Guid orgId, Guid dutyId, string name, int sortOrder, bool isActive, bool isSingleHolder, Guid? minimumLevelId, CancellationToken token = default);
    Task<bool> DeleteInvestigationDutyAsync(Guid orgId, Guid dutyId, CancellationToken token = default);
    Task<LoadResult<CaseContactItem>> GetCaseContactsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<(IReadOnlyList<CaseContactItem> Contacts, string? Error)> SetCaseContactsAsync(Guid orgId, Guid caseId, IReadOnlyList<Guid> appUserIds, CancellationToken token = default);

    Task<LoadResult<InvestigationRosterEntry>> GetInvestigationRosterAsync(
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
    Task<(InvestigationRecord? Result, string? Error)> CreateOrgInvestigationAsync(
        Guid orgId, CreateOrgInvestigationRequest request, CancellationToken token = default);
    Task<OrgCalendarEventTypeRecord?> CreateCalendarEventTypeAsync(Guid orgId, UpsertCalendarEventTypeRequest request, CancellationToken token = default);
    Task<OrgCalendarEventTypeRecord?> UpdateCalendarEventTypeAsync(Guid orgId, Guid id, UpsertCalendarEventTypeRequest request, CancellationToken token = default);
    Task<bool> DeleteCalendarEventTypeAsync(Guid orgId, Guid id, CancellationToken token = default);

    Task<LoadResult<OrgCalendarEventRecord>> GetCalendarEventsAsync(Guid orgId, DateTime? from = null, DateTime? to = null, CancellationToken token = default);
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

    Task<LoadResult<OrgCalendarEventAttendeeRecord>> GetCalendarEventAttendeesAsync(Guid orgId, Guid eventId, CancellationToken token = default);
    Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeAsync(Guid orgId, Guid eventId, AddAttendeeRequest request, CancellationToken token = default);

    /// <summary>
    /// Invites someone to an event by email address — for people outside the organization.
    /// Returns null when nobody with that address has an account.
    /// </summary>
    Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeByEmailAsync(Guid orgId, Guid eventId, string email, CancellationToken token = default);

    /// <summary>
    /// Sends a sign-up link to somebody who has no account here — the walk-up at the meeting point.
    /// </summary>
    /// <remarks>
    /// The companion to <see cref="AddCalendarAttendeeByEmailAsync"/>, which can only resolve an
    /// address an existing account has published. Requires the calendar permission, and the link
    /// it sends carries the organiser's authority: it confirms after sign-ups close and past a
    /// full house, which is what a late arrival needs.
    /// </remarks>
    Task<bool> InviteEventGuestAsync(Guid orgId, Guid eventId, string email, string? displayName = null, CancellationToken token = default);
    Task<OrgCalendarEventAttendeeRecord?> RsvpCalendarEventAsync(Guid orgId, Guid eventId, Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus status, CancellationToken token = default);
    Task<bool> RemoveCalendarAttendeeAsync(Guid orgId, Guid eventId, Guid attendeeId, CancellationToken token = default);

    // ── Directions ────────────────────────────────────────────────────────────
    Task<DirectionsResult?> GetDirectionsAsync(double fromLat, double fromLon, double toLat, double toLon, CancellationToken token = default);

    // ── Published investigations (item #89) ─────────────────────────────────

    /// <summary>What an organization has published, most recent first. Anonymous.</summary>
    Task<LoadResult<PublicInvestigationListItem>> GetPublishedInvestigationsAsync(
        string orgUrlName, CancellationToken token = default);

    /// <summary>One published investigation, by the address people share.</summary>
    Task<PublicInvestigationDetail?> GetPublishedInvestigationAsync(
        string orgUrlName, string investigationSlug, CancellationToken token = default);

    /// <summary>
    /// Places close enough to one another to be one place typed twice. SuperAdmin.
    /// </summary>
    /// <remarks>
    /// A finder rather than a list: the pairs worth showing are exactly the ones the automatic
    /// matcher could not settle, and they are few.
    /// </remarks>
    /// <summary>
    /// Every field session this account uploaded, newest first. The phone's side of the archive,
    /// which until now the website had no page for.
    /// </summary>
    Task<LoadResult<FieldSessionSummaryRecord>> GetMyFieldSessionsAsync(
        CancellationToken token = default);

    /// <summary>
    /// Where this account's sessions were recorded. Separate from the list because the coordinate
    /// lives inside each session document, so it costs a file read apiece.
    /// </summary>
    Task<ItemResult<FieldSessionMapPage>> GetMyFieldSessionMapAsync(
        MapBounds? bounds = null, CancellationToken token = default);

    /// <summary>Field sessions whose document cannot be read back. Changes nothing.</summary>
    Task<LoadResult<OrphanedFieldSessionRecord>> GetOrphanedFieldSessionsAsync(
        CancellationToken token = default);

    /// <summary>
    /// Deletes the chosen sessions. The server intersects the ids with its own orphan set, so a
    /// stale screen is refused rather than half-obeyed.
    /// </summary>
    Task<(OrphanedFieldSessionPurgeResult? Result, string? Error)> PurgeOrphanedFieldSessionsAsync(
        IReadOnlyList<Guid> ids, CancellationToken token = default);

    Task<LoadResult<DuplicatePlaceGroup>> GetDuplicatePlacesAsync(CancellationToken token = default);

    /// <summary>
    /// Moves everything off one place onto another and deletes the empty one. Irreversible.
    /// </summary>
    Task<(PlaceMergeResult? Result, string? Error)> MergePlaceAsync(
        Guid losingPlaceId, Guid intoPlaceId, CancellationToken token = default);
}
