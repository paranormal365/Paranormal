using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── Public events (backlog item #87) ─────────────────────────────────────────
// An organization's public events, as a visitor sees them. Defined here so both sides share one
// definition rather than hand-mirroring it across the API boundary.

/// <summary>
/// Where an event is, as far as this particular reader is entitled to know.
/// </summary>
/// <remarks>
/// <para>A nested optional record rather than fields that are sometimes null. When an event hides
/// its exact location until somebody is coming, a reader who is not coming gets a payload with
/// <b>no slot</b> for the street address — absence is structural, not a matter of a client
/// remembering to hide something it was sent.</para>
///
/// <para><see cref="ApproximateLatitude"/> is always the redacted grid point, even for attendees:
/// it exists to put a pin on a discovery map, and the map is the same map for everybody. Somebody
/// entitled to the exact address gets the address.</para>
/// </remarks>
public sealed record PublicEventLocationRecord(
    string? City,
    string? State,
    decimal? ApproximateLatitude,
    decimal? ApproximateLongitude,
    /// <summary>Set only for a reader entitled to it: the organizer, or somebody attending.</summary>
    string? ExactAddress,
    /// <summary>True when there is an exact address being withheld, so the page can say so.</summary>
    bool IsExactAddressHidden);

/// <summary>What a visitor may do about this event right now, decided server-side.</summary>
public sealed record PublicEventFlags(
    bool CanRsvp,
    bool HasRsvpd,
    bool IsFull,
    bool RsvpHasClosed,
    /// <summary>Why they cannot come, written to be shown to a person.</summary>
    string? RsvpBlockedReason);

/// <summary>One public event.</summary>
public sealed record PublicEventRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationUrlName,
    string Title,
    string? Description,
    DateTime StartDateTime,
    DateTime EndDateTime,
    bool IsAllDay,
    string? MeetingUrl,
    PublicEventLocationRecord Location,
    int AttendingCount,
    int? AttendeeCapacity,
    DateTime? RsvpClosesAt,
    PublicEventFlags Flags);

/// <summary>One public event as it appears in a list.</summary>
public sealed record PublicEventListItem(
    Guid Id,
    /// <summary>
    /// The readable slug this event is reached by. Without it a card has nowhere to link, which is
    /// how a list of events becomes a list nobody can open.
    /// </summary>
    string? UrlName,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationUrlName,
    string Title,
    DateTime StartDateTime,
    DateTime EndDateTime,
    bool IsAllDay,
    string? City,
    string? State,
    decimal? ApproximateLatitude,
    decimal? ApproximateLongitude,
    int AttendingCount,
    int? AttendeeCapacity,
    bool IsOnline);


// ── Coming along without an account (item #87b) ──────────────────────────────

/// <summary>
/// Asks to attend a public event by giving an email address rather than signing in.
/// </summary>
/// <remarks>
/// <paramref name="DisplayName"/> is optional. An email is enough to come along, and demanding a
/// name at the door is the kind of friction that loses the person the event was advertised to.
/// </remarks>
public sealed record RequestEventAttendanceRequest(string Email, string? DisplayName);

/// <summary>What a confirmation link points at, shown before it is used.</summary>
public sealed record EventAttendanceInviteInfo(
    Guid EventId,
    string Title,
    string OrganizationName,
    string OrganizationUrlName,
    string? EventUrlName,
    DateTime StartDateTime,
    string Email);

/// <summary>The result of using a confirmation link.</summary>
public sealed record EventAttendanceConfirmation(
    Guid EventId,
    string Title,
    string OrganizationName,
    string OrganizationUrlName,
    string? EventUrlName,
    DateTime StartDateTime);


// ── Published investigations (backlog item #89) ──────────────────────────────

/// <summary>One published investigation in a list.</summary>
public sealed record PublicInvestigationListItem(
    Guid Id,
    /// <summary>The readable address to link to; null for one published before slugs existed.</summary>
    string? UrlName,
    string Title,
    DateTime ScheduledDateTime,
    InvestigationStatus Status,
    string OrganizationName,
    string OrganizationUrlName,
    string? PlaceName,
    string? City,
    string? State);

/// <summary>
/// One published investigation as a visitor sees it.
/// </summary>
/// <remarks>
/// Carries the write-up (<c>Notes</c>) rather than the plan (<c>Description</c>), and only an
/// approximate location: a published account says a group was somewhere, not precisely where.
/// </remarks>
public sealed record PublicInvestigationDetail(
    Guid Id,
    string? UrlName,
    string Title,
    string? Notes,
    DateTime ScheduledDateTime,
    DateTime? EndDateTime,
    InvestigationStatus Status,
    string OrganizationName,
    string OrganizationUrlName,
    Guid? PlaceId,
    string? PlaceName,
    string? City,
    string? State,
    decimal? ApproximateLatitude,
    decimal? ApproximateLongitude);
