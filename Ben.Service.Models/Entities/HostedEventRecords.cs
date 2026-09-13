using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>One date of a hosted event, as a screen sees it (item 235).</summary>
/// <param name="Label">
/// What to call it — the host's own title, or "Friday 30 October" when they have not given one.
/// Built on the server so the web and the phone cannot disagree about a date's name.
/// </param>
/// <param name="StartsLocal">When it starts, in the event's own zone. Null = the event's default.</param>
public sealed record HostedEventNightRecord(
    Guid Id,
    DateTime Date,
    string Label,
    string? Title,
    TimeSpan? StartsLocal,
    TimeSpan? EndsLocal,
    string? Notes,
    int SortOrder);

/// <summary>
/// What a hosted event costs this organization, and whether another may go live.
/// </summary>
/// <param name="PaysWith">"plan" or "credit" — which of the two answers applies.</param>
/// <param name="Refusal">Why publishing is refused, or null when it may go ahead.</param>
/// <param name="Sentence">
/// What the page says before anybody presses anything: what publishing will do and what it costs.
/// </param>
/// <remarks>
/// Read by the event list and by the publish confirmation, so the answer somebody is warned about
/// and the answer they get are computed once, in the same place.
/// </remarks>
public sealed record HostedEventPlanRecord(
    string PaysWith,
    string? Refusal,
    string Sentence,
    int LiveNow,
    int? Ceiling,
    int CreditsAvailable,
    bool MayPublish,
    bool SpendsACredit);

/// <summary>A hosted event as its organizers see it (item 235).</summary>
/// <param name="DatesAreSeparate">
/// True when each date stands on its own — a run of performances rather than one stay. The event is
/// then the production and each date a performance of it.
/// </param>
/// <param name="DateNoun">
/// "night" or "date", singular, so every screen says the same word for the same thing without
/// nine of them each deciding.
/// </param>
/// <param name="PlanNote">What the last action did to the plan, when it did anything.</param>
/// <param name="LifecycleState">
/// The one answer to "what is this event". It replaced a published flag and two timestamps that
/// six screens each combined differently — one of them kept advertising an event that was off.
/// </param>
/// <param name="HoldMinutes">
/// How long a picked place is held for. Minutes rather than a <c>TimeSpan</c> because EF maps that
/// to SQL <c>time</c>, which caps at 24 hours and would have silently wrapped the two-day default.
/// </param>
/// <param name="DayPassPrice">Shown and never charged. Null is "ask the venue"; zero is free.</param>
public sealed record HostedEventRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Name,
    string UrlName,
    string? Tagline,
    string? Description,
    Guid PlaceId,
    string PlaceLabel,
    bool HideExactLocation,
    string TimeZoneId,
    DateTime StartsOn,
    DateTime EndsOn,
    bool DatesAreSeparate,
    string DateNoun,
    TimeSpan? DefaultStartLocal,
    TimeSpan? DefaultEndLocal,
    HostedEventLifecycleState LifecycleState,
    DateTime? FirstPublishedUtc,
    int? DayPassCapacity,
    string? ContactLine,
    Guid? CoverUploadFileId,
    string? MailSubjectTemplate,
    string? MailBodyTemplate,
    bool CollectsEvidence,
    DateTime? ArchivedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledReason,
    Guid? UmbrellaEventId,
    IReadOnlyList<HostedEventNightRecord> Nights,
    string? PlanNote = null,
    HostedEventBookingMode BookingMode = HostedEventBookingMode.Ask,
    int HoldMinutes = 2880,
    decimal? DayPassPrice = null,
    DateTime? BookingsCloseAtUtc = null,
    HostedEventVenueArrangement VenueArrangement = HostedEventVenueArrangement.Self,
    string? VenueContactName = null,
    DateTime? VenueAgreedOnUtc = null,
    string? VenueReference = null,
    int? MinimumGuests = null,
    DateTime? GoNoGoDeadlineUtc = null,
    HostedEventGoNoGo GoNoGoDecision = HostedEventGoNoGo.Undecided,
    DateTime? GoNoGoDecidedUtc = null,
    DateTime? LiveAtUtc = null,
    DateTime? EndedAtUtc = null);

/// <summary>
/// A venue being entered as part of the event that happens there (item 235).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-11:</b> "They may need to enter the name and address and information about
/// a new venue we have not listed before." Which is how most of them will start: a hotel, a theatre
/// or a hall the site has never heard of, entered by the person who is about to run an event in
/// it.</para>
///
/// <para>Shaped exactly like the inline place an investigation creates, and resolved through the
/// same matcher, so entering "The Thomas House Hotel" twice finds the one that already exists
/// rather than making a second. A venue is a shared place and always has been.</para>
///
/// <para><b>Public by default, and only ever public.</b> Every other inline place on this site
/// defaults to a private residence, which is the cautious answer where somebody's home is the
/// likely subject. Here the cautious answer is the opposite: an event is published by definition,
/// and a private residence is refused outright, so a venue that arrives with no kind is a venue.</para>
/// </remarks>
public sealed record NewVenueRequest(
    string? Name,
    string? StreetAddress1 = null,
    string? StreetAddress2 = null,
    string? City = null,
    string? State = null,
    string? ZipCode = null,
    string? Country = null,
    decimal? Latitude = null,
    decimal? Longitude = null)
{
    /// <summary>Whether anything at all was entered, rather than an empty form.</summary>
    public bool HasAnything =>
        !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(StreetAddress1)
        || !string.IsNullOrWhiteSpace(City);
}

/// <summary>Creating or changing a hosted event.</summary>
/// <remarks>
/// Publishing is not here. It is its own endpoint because it is the only thing on this screen that
/// costs money, and a field on a save form is not a place to spend somebody's $99.
/// </remarks>
public sealed record UpsertHostedEventRequest(
    string Name,
    /// <summary>
    /// An existing venue. Empty when <see cref="NewVenue"/> carries one that is not on the site yet.
    /// </summary>
    Guid PlaceId,
    DateTime StartsOn,
    DateTime EndsOn,
    string? TimeZoneId = null,
    string? Tagline = null,
    string? Description = null,
    bool HideExactLocation = false,
    bool DatesAreSeparate = false,
    TimeSpan? DefaultStartLocal = null,
    TimeSpan? DefaultEndLocal = null,
    int? DayPassCapacity = null,
    string? ContactLine = null,
    Guid? CoverUploadFileId = null,
    string? MailSubjectTemplate = null,
    string? MailBodyTemplate = null,
    bool CollectsEvidence = false,
    /// <summary>
    /// The dates of a run, chosen one at a time. Ignored for a stay, whose dates are every day
    /// between its ends.
    /// </summary>
    IReadOnlyList<DateTime>? Dates = null,
    /// <summary>
    /// A venue the site has never listed, entered here rather than somewhere else first.
    /// </summary>
    NewVenueRequest? NewVenue = null,

    /// <summary>When the venue stops taking requests. Null means it never does.</summary>
    DateTime? BookingsCloseAtUtc = null,

    /// <summary>Shown to guests and never charged. Null is "ask the venue"; zero is free.</summary>
    decimal? DayPassPrice = null,

    /// <summary>Whether guests pick their place on the plan or ask for one and are placed.</summary>
    HostedEventBookingMode BookingMode = HostedEventBookingMode.Ask,

    /// <summary>How long a picked place is held for, in minutes. 15 to 20160.</summary>
    int HoldMinutes = 2880,

    /// <summary>How this event came to be allowed to happen where it happens.</summary>
    HostedEventVenueArrangement VenueArrangement = HostedEventVenueArrangement.Self,

    /// <summary>Who at the venue agreed, for an arrangement made off this site.</summary>
    string? VenueContactName = null,

    /// <summary>When they agreed.</summary>
    DateTime? VenueAgreedOnUtc = null,

    /// <summary>The venue's own reference for it — a contract or invoice number.</summary>
    string? VenueReference = null,

    /// <summary>The fewest people that make it worth running. Null means it runs regardless.</summary>
    int? MinimumGuests = null,

    /// <summary>When the organizer has to decide whether it is going ahead.</summary>
    DateTime? GoNoGoDeadlineUtc = null);

/// <summary>Changing one date of an event.</summary>
public sealed record UpsertHostedEventNightRequest(
    string? Title = null,
    TimeSpan? StartsLocal = null,
    TimeSpan? EndsLocal = null,
    string? Notes = null);

/// <summary>
/// What calling an event off would do to the credit spent on it (item 235 phase 3).
/// </summary>
/// <param name="Sentence">
/// The words the cancel itself will answer with, so the confirmation and the outcome can never
/// read as two different things.
/// </param>
/// <remarks>
/// A record and not a bare string: MVC serves a string through its plain-text formatter, and every
/// client in this solution reads an answer as JSON. That mistake once took a whole admin screen
/// down the moment its endpoint had something to say.
/// </remarks>
public sealed record HostedEventCancellationEffect(bool CreditComesBack, string Sentence);

/// <summary>Changing how guests get a place on an event.</summary>
public sealed record SetHostedEventBookingModeRequest(HostedEventBookingMode Mode);

/// <summary>Calling an event off, with the reason the people who have places will read.</summary>
public sealed record CancelHostedEventRequest(string? Reason = null);

/// <summary>A hosted event as a visitor sees it (item 235).</summary>
/// <remarks>
/// Published events only. The exact address is withheld exactly as a public calendar event withholds
/// it, so a reader without a place sees the town and no more when the host asked for that.
/// </remarks>
public sealed record PublicHostedEventRecord(
    Guid Id,
    Guid UmbrellaEventId,
    string OrganizationName,
    string OrganizationUrlName,
    string Name,
    string UrlName,
    string? Tagline,
    string? Description,
    string TimeZoneId,
    DateTime StartsOn,
    DateTime EndsOn,
    bool DatesAreSeparate,
    string DateNoun,
    string? VenueName,
    string? City,
    string? State,
    string? ExactAddress,
    bool IsExactAddressHidden,
    decimal? Latitude,
    decimal? Longitude,
    int? DayPassCapacity,
    string? ContactLine,
    Guid? CoverUploadFileId,
    bool IsCancelled,
    string? CancelledReason,
    /// <summary>
    /// Whether the host invites photographs and recordings afterwards.
    /// </summary>
    /// <remarks>
    /// Off for most events, because most events are not ghost hunts. A venue running a play does
    /// not want an evidence queue on its public page, and a lock-in does.
    /// </remarks>
    bool CollectsEvidence,
    IReadOnlyList<HostedEventNightRecord> Nights,

    /// <summary>
    /// How places are had: picked off a plan, or asked for in words (item 235 phase 6).
    /// </summary>
    /// <remarks>
    /// Additive with a default, like everything else added below it, because the app already in
    /// people's pockets decodes this record and has to keep doing so unchanged. Ask is what every
    /// event did before the choice existed.
    /// </remarks>
    HostedEventBookingMode BookingMode = HostedEventBookingMode.Ask,

    /// <summary>The one state, so a page does not infer it from four booleans.</summary>
    HostedEventLifecycleState LifecycleState = HostedEventLifecycleState.Published,

    /// <summary>Whether a place can still be had at all.</summary>
    bool IsTakingBookings = true,

    /// <summary>
    /// Why not, in words for a person, or null when it is.
    /// </summary>
    /// <remarks>
    /// "Called off", "bookings closed on Friday" and "this event has happened" are three different
    /// facts, and a page with no button and no sentence leaves a reader refreshing it.
    /// </remarks>
    string? NotTakingBookingsSentence = null,

    DateTime? BookingsCloseAtUtc = null,

    /// <summary>Shown and never charged. Null is "ask the venue"; zero is genuinely free.</summary>
    decimal? DayPassPrice = null,

    /// <summary>
    /// The number this event needs before it definitely runs, when the host set one.
    /// </summary>
    /// <remarks>
    /// A guest asked to keep a weekend free is owed the fact that it might not happen, and the
    /// date by which they will know. Hiding it until the decision is made is how somebody books a
    /// flight for an event that was never going to run.
    /// </remarks>
    int? MinimumGuests = null,
    DateTime? GoNoGoDeadlineUtc = null,
    HostedEventGoNoGo GoNoGoDecision = HostedEventGoNoGo.Undecided,

    /// <summary>
    /// The group that said yes as the venue, when it is another group on this site (phase 9).
    /// </summary>
    string? VenueOrganizationName = null,

    /// <summary>Where the venue's own page is, relative to the site, when it has published one.</summary>
    string? VenuePageUrl = null,

    /// <summary>The building's story, from the venue's profile, when the venue lent it.</summary>
    string? VenueHistory = null,

    /// <summary>The host's pictures, in their order (phase 11). Null on a payload from before them.</summary>
    IReadOnlyList<PublicEventImageRecord>? Gallery = null);

/// <summary>
/// One thing that has to be true before an event can go live (item 235 phase 3).
/// </summary>
/// <param name="Sentence">
/// Why it is not done, in the words the publish button refuses with. The same string in both
/// places on purpose: a checklist that phrases it one way and a refusal that phrases it another
/// reads as two different problems.
/// </param>
/// <param name="Href">
/// Where to go and fix it, relative to the event's page — an anchor for a card on that page, or a
/// path for a screen of its own. The website turns it into a URL; the API has no business knowing
/// the site's routes.
/// </param>
public sealed record HostedEventReadinessItem(
    string Area,
    string Label,
    bool Done,
    string Sentence,
    string Href);

/// <summary>
/// One square of a plan as a visitor sees it (item 235 phase 6).
/// </summary>
/// <remarks>
/// <b>A state and nothing else.</b> No party, no name, no count of who else wanted it: a guest
/// choosing a seat needs to know whether they may have it, and everything beyond that is somebody
/// else's business. The organizer's board asks a different endpoint and joins the names on there.
/// </remarks>
public sealed record PublicHostedEventPlanCellRecord(
    Guid HostedEventNightId,
    Guid HostedEventLayoutUnitId,
    HostedEventPlanCellState State);

/// <summary>A room or a seat as a visitor sees it (item 235 phase 6).</summary>
/// <param name="Holds">
/// How many the room sleeps, or how many the seat seats — which is one. Null when the venue has
/// not said, and the picker then counts places rather than people.
/// </param>
/// <param name="Price">Shown and never charged. Null is "ask the venue"; zero is genuinely free.</param>
/// <remarks>
/// Its own record rather than the organizer's <c>HostedEventLayoutUnitRecord</c>, which carries the
/// venue's private note about the square. A shape where the private thing has to be deliberately
/// ADDED is one that cannot leak it by being extended.
/// </remarks>
public sealed record PublicHostedEventPlanUnitRecord(
    Guid Id,
    string Name,
    string? Section,
    int? Holds,
    decimal? Price,
    int? LayoutRow,
    int? LayoutColumn,
    int SortOrder);

/// <summary>
/// The plan of a published event, with what every square is on every night (item 235 phase 6).
/// </summary>
/// <param name="IsPicking">
/// True when this event sells its places by the square AND is still taking bookings. False draws
/// the same plan read-only, which is what a stranger and a latecomer both see.
/// </param>
/// <param name="ClosedSentence">
/// Why nothing can be picked, in words for a person, or null when it can. "The event has been
/// called off" and "bookings closed on Friday" are different facts and a greyed-out grid says
/// neither.
/// </param>
/// <param name="HoldMinutes">
/// How long a pick is held for, so the page can say "yours for two days" before somebody commits
/// rather than after.
/// </param>
/// <param name="Cells">
/// <b>Only the squares that are not free.</b> Everything absent is free, which is most of a plan
/// on the day it opens and is the difference between a few hundred bytes and a few thousand for a
/// four-hundred-seat house across three nights.
/// </param>
public sealed record PublicHostedEventPlanRecord(
    Guid HostedEventId,
    HostedEventLayoutKind Kind,
    HostedEventBookingMode BookingMode,
    bool IsPicking,
    string? ClosedSentence,
    int HoldMinutes,
    int MaxPartySize,
    IReadOnlyList<HostedEventNightRecord> Nights,
    IReadOnlyList<PublicHostedEventPlanUnitRecord> Units,
    IReadOnlyList<PublicHostedEventPlanCellRecord> Cells);

// ── the programme (item 235 phase 10) ─────────────────────────────────────────

/// <summary>One session, as the host's editor reads it.</summary>
/// <param name="Where">The room's name, or the words the host wrote.</param>
/// <param name="Waiting">How many are queued for a place.</param>
public sealed record HostedEventSessionRecord(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    Guid? PlaceRoomId,
    string? LocationText,
    string? Where,
    string? LedBy,
    int? Capacity,
    bool RequiresSignUp,
    int PlacesTaken,
    int Waiting,
    bool IsCancelled,
    string? CancelledReason,
    DateTime? ChangedUtc);

/// <summary>An event's whole programme, for its host.</summary>
public sealed record HostedEventProgrammeRecord(
    Guid HostedEventId,
    string TimeZoneId,
    DateTime? PublishedUtc,
    IReadOnlyList<DateTime> Nights,
    IReadOnlyList<HostedEventSessionRecord> Sessions,
    string? Note = null);

/// <summary>Creates or changes a session. Times are on the venue's clock; an end before the start is the next day.</summary>
public sealed record SaveHostedEventSessionRequest(
    string Title,
    string? Description,
    DateTime Date,
    TimeSpan StartsLocal,
    TimeSpan EndsLocal,
    Guid? PlaceRoomId,
    string? LocationText,
    string? LedBy,
    int? Capacity,
    bool RequiresSignUp);

/// <summary>Calls a session off, with a reason everybody signed up is told.</summary>
public sealed record CancelHostedEventSessionRequest(string? Reason);

/// <summary>Who has a place in a session, and who is waiting, in order.</summary>
public sealed record HostedEventSessionRosterRecord(
    Guid SessionId,
    string Title,
    IReadOnlyList<HostedEventSessionRosterLine> In,
    IReadOnlyList<HostedEventSessionRosterLine> Waiting);

/// <summary>One person on a roster.</summary>
public sealed record HostedEventSessionRosterLine(Guid SignUpId, string Name, int People, DateTime SignedUpUtc, bool IsHelping);

/// <summary>The published programme, for a guest or a visitor.</summary>
/// <param name="CanSignUp">Whether this viewer may sign up for anything.</param>
/// <param name="WhyNotSignUp">Why not, in words, for a signed-in viewer who may not.</param>
/// <param name="MaxPeople">The most of their party a sign-up may be for.</param>
/// <param name="ChangedSinceSeen">Whether something moved or was cancelled since this guest last looked.</param>
public sealed record PublicProgrammeRecord(
    Guid HostedEventId,
    string TimeZoneId,
    IReadOnlyList<DateTime> Nights,
    IReadOnlyList<PublicSessionRecord> Sessions,
    bool CanSignUp,
    string? WhyNotSignUp,
    int MaxPeople,
    bool ChangedSinceSeen);

/// <summary>One session on the published programme.</summary>
/// <param name="Mine">This viewer's place or position in the queue, when they have one.</param>
public sealed record PublicSessionRecord(
    Guid Id,
    string Title,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string? Where,
    string? LedBy,
    int? Capacity,
    bool RequiresSignUp,
    int PlacesTaken,
    bool IsCancelled,
    string? CancelledReason,
    bool Changed,
    MySessionPlaceRecord? Mine);

/// <summary>A guest's place in one session.</summary>
/// <param name="Position">Where they are in the queue when waiting. 1 is next.</param>
public sealed record MySessionPlaceRecord(Guid SignUpId, int People, bool Waiting, int? Position);

/// <summary>Signs up for a session, for this many of the party.</summary>
public sealed record SessionSignUpRequest(int People = 1);

// ── an event's files (item 235 phase 11) ──────────────────────────────────────

/// <summary>One of an event's files, as the host's page or a guest's list reads it.</summary>
public sealed record HostedEventFileRecord(
    Guid Id,
    Guid UploadFileId,
    string FileName,
    string ContentType,
    long FileSize,
    string? Folder,
    string? Description,
    EventFileAudience Audience,
    int SortOrder,
    DateTime DateCreated);

/// <summary>Changes a file's pile, words, audience or place in the list.</summary>
public sealed record UpdateHostedEventFileRequest(string? Folder, string? Description, EventFileAudience Audience, int SortOrder);

// ── the event's room (item 235 phase 11) ──────────────────────────────────────

/// <summary>The room, as a member reads it.</summary>
/// <param name="WhyNotPost">Why posting is closed, when it is.</param>
/// <param name="HostNames">Who a post's file can be sent to: the organizer, and the venue when there is one.</param>
public sealed record EventRoomRecord(
    bool CanPost,
    string? WhyNotPost,
    bool CanModerate,
    IReadOnlyList<string> HostNames,
    IReadOnlyList<EventRoomMessageRecord> Messages,
    string? Note = null,
    EventPhotoPosting PhotoPosting = EventPhotoPosting.TeamAndGuests,
    bool CanAddPhotos = false,
    /// <summary>Whether this viewer may open the photo wall: the organizers, helpers and venue — not guests.</summary>
    bool CanSeeWall = false,
    /// <summary>Whether this guest still has to agree to <see cref="PhotoNotice"/> before their first photo.</summary>
    bool NeedsPhotoConsent = false,
    /// <summary>What they agree to.</summary>
    string? PhotoNotice = null);

/// <summary>One photo or video on the photo wall.</summary>
public sealed record EventWallPhotoRecord(Guid MessageId, string ContentType, string AuthorName, string? Caption, DateTime PostedUtc);

/// <summary>The photo wall: what to cycle through, newest first.</summary>
public sealed record EventWallRecord(string EventName, IReadOnlyList<EventWallPhotoRecord> Photos);

/// <summary>Changes who may add photos.</summary>
public sealed record EventRoomSettingsRequest(EventPhotoPosting PhotoPosting);

/// <summary>One post in the room.</summary>
/// <param name="MediaWaiting">A file still being checked, seen only by its author and the people who look after the room.</param>
/// <param name="SentToHosts">Whether the author has sent its file to the organizer and venue.</param>
/// <param name="Reports">How many members reported it — for the people who look after the room only.</param>
public sealed record EventRoomMessageRecord(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    string Body,
    DateTime PostedUtc,
    bool HasMedia,
    string? MediaContentType,
    bool MediaWaiting,
    bool IsMine,
    bool IsHidden,
    bool SentToHosts,
    int Reports);

/// <summary>Why a post is being reported.</summary>
public sealed record ReportEventRoomMessageRequest(string? Reason);

// ── the event's gallery (item 235 phase 11) ───────────────────────────────────

/// <summary>One picture in an event's public gallery.</summary>
public sealed record HostedEventImageRecord(Guid Id, Guid UploadFileId, int SortOrder, string? Caption);

/// <summary>A caption, or a new place in the order.</summary>
public sealed record UpdateHostedEventImageRequest(string? Caption, int? SortOrder);

/// <summary>A gallery picture as a visitor's page reads it.</summary>
public sealed record PublicEventImageRecord(Guid UploadFileId, string? Caption);

// ── an event on a group's own page (item 235 phase 11) ────────────────────────

/// <summary>
/// What a group's CMS page shows for one of its events, resolved when the page is read so it is never
/// stale. One shape for all four event sections; each fills the part it is for.
/// </summary>
/// <param name="Missing">The chosen event is not on the public site — drafted, called off or gone.</param>
public sealed record CmsEventSectionRecord(
    bool Missing,
    string? Name = null,
    string? Url = null,
    string? DateLine = null,
    string? Tagline = null,
    string? BookingSentence = null,
    decimal? DayPassPrice = null,
    IReadOnlyList<CmsEventSessionRecord>? Programme = null,
    IReadOnlyList<PublicEventImageRecord>? Gallery = null,
    CmsEventVenueRecord? Venue = null);

/// <summary>One session in an event section's programme.</summary>
public sealed record CmsEventSessionRecord(string Title, string When, string? Where, string? LedBy, string? Places, bool IsCancelled);

/// <summary>The venue, in an event's venue section.</summary>
public sealed record CmsEventVenueRecord(string PlaceName, string? Town, string? RunBy, string? VenuePageUrl, string? History);

// ── after the event: starting the next one from it (item 235 phase 12) ─────────

/// <summary>What copying an event would bring, before anybody presses anything.</summary>
/// <param name="PendingInvitations">Helpers who were invited and never answered; they do not come across.</param>
/// <param name="FileBytes">What the files add up to, against the new event's allowance.</param>
/// <param name="VenueSentence">What happens to the agreement with the venue, in words.</param>
public sealed record HostedEventCopyPreviewRecord(
    Guid HostedEventId,
    string Name,
    DateTime StartsOn,
    DateTime EndsOn,
    int Nights,
    HostedEventLayoutKind LayoutKind,
    int Units,
    int Menus,
    int Sessions,
    int Bands,
    int Helpers,
    int PendingInvitations,
    int Adverts,
    int Files,
    long FileBytes,
    string VenueSentence);

/// <summary>Starts a new draft from an existing event, bringing what is ticked.</summary>
public sealed record CopyHostedEventRequest(
    string Name,
    DateTime StartsOn,
    bool Plan = true,
    bool Menus = true,
    bool Programme = true,
    bool Bands = true,
    bool Helpers = true,
    bool Adverts = true,
    bool Files = false);

/// <summary>The draft a copy made, and what came with it.</summary>
/// <param name="FilesSentence">Said when some files could not be brought, and why.</param>
public sealed record HostedEventCopyResultRecord(
    Guid HostedEventId,
    string Name,
    int Nights,
    int Units,
    int Blocks,
    int Menus,
    int Sessions,
    int Bands,
    int Helpers,
    int Adverts,
    int Files,
    string? FilesSentence);

// ── reviews and the thank-you (item 235 phase 12) ─────────────────────────────

/// <summary>An event's reviews, with what the page around them needs.</summary>
/// <param name="Reviews">The same shape as a tour's, so both pages read reviews alike.</param>
/// <param name="IsOver">Whether the event has happened; before then only the group's past rating is worth showing.</param>
/// <param name="PastAverage">What guests made of this group's other events, for the page of an upcoming one.</param>
public sealed record PublicHostedEventReviewsRecord(
    TourReviewsRecord Reviews,
    string EventName,
    string? EventUrlName,
    string? OrganizationName,
    string? OrganizationUrlName,
    DateTime EndsOn,
    bool IsOver,
    decimal? PastAverage,
    int PastCount);

/// <summary>The organizer's side of what happens after the event.</summary>
/// <param name="GuestsThanked">Parties the thank-you has gone to.</param>
/// <param name="GuestsToThank">Parties with a confirmed place, who will get it.</param>
public sealed record HostedEventAfterRecord(
    Guid HostedEventId,
    string Name,
    HostedEventLifecycleState LifecycleState,
    bool AllowReviews,
    bool SendThankYou,
    string? ThankYouNote,
    DateTime? ThankYouSentUtc,
    int GuestsThanked,
    int GuestsToThank,
    bool HasGallery,
    decimal? Average,
    int Count,
    IReadOnlyList<TourReviewRecord> Reviews);

/// <summary>Changing the thank-you and whether reviews are taken.</summary>
public sealed record SetHostedEventAfterRequest(bool AllowReviews, bool SendThankYou, string? ThankYouNote);

