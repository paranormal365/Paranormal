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
    PublicEventFlags Flags,
    // ── Tours (item 233) ────────────────────────────────────────────────────
    // Appended with defaults so an older client reads this exactly as it did before.
    /// <summary>The tour this date runs, when it is one.</summary>
    string? TourName = null,
    /// <summary>Its slug, for the link to the tour's own page.</summary>
    string? TourUrlName = null,
    /// <summary>
    /// Who is leading it.
    /// </summary>
    /// <remarks>
    /// Ben asked for the guide's name and picture in what a guest is sent, "for safety" — the
    /// person walking into the dark should know who they are meeting. The same facts are on the
    /// page, so a guest who never opens the mail still knows.
    /// </remarks>
    IReadOnlyList<PublicGuideRecord>? Guides = null,
    /// <summary>Average stars out of five, when anyone has rated the tour.</summary>
    decimal? TourRating = null,
    int TourRatingCount = 0);

/// <summary>A guide as a guest sees them: a name, and a face when they have published one.</summary>
public sealed record PublicGuideRecord(string DisplayName, string? Handle, Guid? PhotoUploadFileId);

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
    bool IsOnline,
    // Item 233, appended with defaults: a card that does not say which tour it is makes a
    // business's three walks look like three unrelated evenings.
    string? TourName = null,
    string? TourUrlName = null);


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

/// <summary>One attendee evidence submission, in every view that shows one (item 111).</summary>
public sealed record EventEvidenceRecord(
    Guid Id,
    Guid OrgCalendarEventId,
    string EventTitle,
    string SubmitterDisplayName,
    Guid UploadFileId,
    string FileName,
    string ContentType,
    string? Note,
    Ben.Data.Common.Enums.EvidenceSubmissionStatus Status,
    string? RejectionReason,
    DateTime DateCreated,
    /// <summary>
    /// When the SUBMITTER contributed this to the place's archive, or null. Deliberately separate
    /// from <paramref name="Status"/>: that is the operator's verdict on their own gallery, and
    /// this is the photographer's decision about the place's public record.
    /// </summary>
    DateTime? PublishedToPlaceAtUtc = null,
    /// <summary>Whether the event is at a public place, so there is an archive to contribute to.</summary>
    bool PlaceAcceptsArchive = false);

// ── Tours (item 233) ─────────────────────────────────────────────────────────
// Ben, 2026-09-10: "Tours are public so, they show up on the map and are searchable." A tour is
// the product a business sells; these are the shapes a visitor reads it in.

/// <summary>A tour on a list or a search result.</summary>
public sealed record PublicTourListItem(
    Guid Id,
    string Name,
    string UrlName,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationUrlName,
    string? City,
    string? State,
    decimal? Latitude,
    decimal? Longitude,
    int? DurationMinutes,
    DateTime? NextDateStartUtc,
    int UpcomingDateCount,
    decimal? Rating,
    int RatingCount,
    double? DistanceMiles = null,
    /// <summary>The tour's first picture, for the card. Null when it has none.</summary>
    Guid? CoverUploadFileId = null);

/// <summary>
/// One tour's own page.
/// </summary>
/// <remarks>
/// The meeting point is given in full, unlike a case or a private investigation: a tour exists to
/// be turned up to, and an address withheld from the person deciding whether to come is an
/// address withheld from the wrong reader.
/// </remarks>
public sealed record PublicTourRecord(
    Guid Id,
    string Name,
    string UrlName,
    string? Description,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationUrlName,
    string MeetingPoint,
    string? City,
    string? State,
    decimal? Latitude,
    decimal? Longitude,
    int? DurationMinutes,
    int? DefaultCapacity,
    string TimeZoneId,
    string? ContactLine,
    bool IsBookable,
    bool AllowReviews,
    IReadOnlyList<PublicGuideRecord> Guides,
    IReadOnlyList<PublicEventListItem> UpcomingDates,
    decimal? Rating,
    int RatingCount,
    /// <summary>The tour's own pictures, in the order the business put them.</summary>
    IReadOnlyList<PublicTourImage>? Gallery = null,
    /// <summary>
    /// Pictures guests took on the nights out, as accepted by the business (item 233).
    /// </summary>
    /// <remarks>
    /// Ben asked for a slideshow "as taken by guests and the tour guides and company", so the two
    /// sources sit side by side and each slide says whose it is. Only accepted submissions from
    /// public dates appear, which is the rule the event page already publishes under — a guest is
    /// told at the moment they upload that acceptance makes it public and credited.
    /// </remarks>
    IReadOnlyList<PublicTourGuestPhoto>? GuestGallery = null);

/// <summary>One picture a guest took on a tour, credited to them.</summary>
/// <param name="EventId">The date it was taken on — also where its bytes are served from.</param>
public sealed record PublicTourGuestPhoto(
    Guid SubmissionId,
    Guid EventId,
    string FileName,
    string ContentType,
    string? Note,
    string By,
    DateTime WhenUtc);

/// <summary>A picture on a tour's public page.</summary>
/// <param name="Caption">Also the alt text, so a picture here is never mute.</param>
public sealed record PublicTourImage(Guid UploadFileId, string? Caption);

/// <summary>A tour as a pin: the least a map needs, and nothing a map does not.</summary>
public sealed record PublicTourMapPin(
    Guid Id,
    string Name,
    string UrlName,
    string OrganizationName,
    string OrganizationUrlName,
    decimal Latitude,
    decimal Longitude,
    DateTime? NextDateStartUtc);
