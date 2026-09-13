using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// One thing this event allocates — a room or a seat — as every screen reads it.
/// </summary>
/// <param name="PlaceRoomId">The venue's own room behind it, on a Rooms layout. Null for a seat.</param>
/// <param name="Name">
/// Its label, or the venue's name for the room behind it. Resolved on the server so no screen has
/// to know that a Rooms unit deliberately carries no label of its own.
/// </param>
/// <param name="Holds">
/// What it holds for THIS event — the event's own number where there is one, otherwise the room's.
/// Null means nobody has said, which is allowed and simply cannot be over-filled.
/// </param>
/// <param name="Price">
/// Shown and <b>never charged</b>. Null is "ask the venue", not free; zero is genuinely free.
/// </param>
public sealed record HostedEventLayoutUnitRecord(
    Guid Id,
    Guid? PlaceRoomId,
    string Name,
    string? Section,
    string? Floor,
    string? BedNote,
    int? Holds,
    int? Capacity,
    decimal? Price,
    string? Note,
    int? LayoutRow,
    int? LayoutColumn,
    int SortOrder);

/// <summary>
/// The whole plan for an event, replacing whatever was there.
/// </summary>
/// <remarks>
/// Replace-the-set, because the screen is one designer somebody arranges and saves. A unit left out
/// is removed, and one with confirmed bookings against it is refused rather than quietly dropped.
/// </remarks>
public sealed record SetHostedEventLayoutRequest(
    HostedEventLayoutKind Kind,
    IReadOnlyList<HostedEventLayoutUnitChoice> Units);

/// <param name="Id">
/// The existing unit this choice is, when it is one. Matched by id FIRST, then by room or label:
/// without it, renaming a seat that has a confirmed party in it reads as delete-and-create, and
/// the delete is refused because the seat is booked. With it, a rename is a rename.
/// </param>
/// <param name="PlaceRoomId">
/// Required on a Rooms layout and refused on a Seats one: a seat is not one of the venue's rooms.
/// </param>
/// <param name="Label">
/// Required on a Seats layout and ignored on a Rooms one, which uses the room's own name so that
/// renaming the room renames it everywhere at once.
/// </param>
public sealed record HostedEventLayoutUnitChoice(
    Guid? Id = null,
    Guid? PlaceRoomId = null,
    string? Label = null,
    string? Section = null,
    int? Capacity = null,
    decimal? Price = null,
    string? Note = null,
    int? LayoutRow = null,
    int? LayoutColumn = null);

/// <summary>
/// One night of one booking: which night they are here, and what they hold that night.
/// </summary>
/// <param name="HostedEventLayoutUnitId">
/// The room or seat, or null when they are here that day without one — somebody coming for
/// Saturday and going home again.
/// </param>
/// <param name="UnitName">
/// What to call it. "Just for the day" when they hold nothing, rather than an empty cell that
/// reads as missing data.
/// </param>
public sealed record HostedEventBookingNightRecord(
    Guid HostedEventNightId,
    DateTime Date,
    Guid? HostedEventLayoutUnitId,
    string UnitName);

/// <summary>Somebody in the party.</summary>
/// <param name="DietaryNotes">
/// Health information about a named person. Sent only to staff who may see bookings, and to door
/// staff for their own night; it never reaches a public record.
/// </param>
public sealed record HostedEventBookingGuestRecord(
    Guid Id,
    string DisplayName,
    Guid? AppUserId,
    string? DietaryNotes,
    int SortOrder);

/// <summary>A party's place at an event, as the venue sees it.</summary>
public sealed record HostedEventBookingRecord(
    Guid Id,
    Guid HostedEventId,
    Guid LeadAppUserId,
    string LeadName,
    string? LeadEmail,
    int PartySize,
    HostedEventBookingKind Kind,
    HostedEventBookingStatus Status,
    DateTime? DecidedUtc,
    string? DecidedByName,
    string? DecisionNote,
    DateTime? GuestAcknowledgedUtc,
    string? Note,
    /// <summary>Set when the guest has asked to get out of a booking the venue confirmed.</summary>
    DateTime? CancellationRequestedUtc,
    string? CancellationReason,
    DateTime DateCreated,
    IReadOnlyList<HostedEventBookingNightRecord> Nights,
    IReadOnlyList<HostedEventBookingGuestRecord> Guests,

    /// <summary>
    /// When a picked party's hold runs out. Null on anything that was never held.
    /// </summary>
    /// <remarks>
    /// The board counts down from it, which is the whole reason it is on this record: a host
    /// looking at their queue needs to know which of these decisions is about to be made for them.
    /// Kept after the hold lapses, so an Expired booking can say when.
    /// </remarks>
    DateTime? HoldExpiresUtc = null,

    /// <summary>
    /// The colour this party wears, worked out on the server (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// Worked out here and not on the board, so the board, the door and the guest's pass cannot
    /// disagree about a colour — three copies of the rule would be three chances to.
    /// </remarks>
    HostedEventBandRecord? Band = null,

    /// <summary>
    /// The band a venue gave this party by hand, or null when the rules decide.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Band"/> because they answer different questions: what they are
    /// wearing, and whether somebody chose it. A picker that showed the worked-out colour as the
    /// chosen one would make "back to the rules" impossible to see.
    /// </remarks>
    Guid? HandPickedBandId = null);

/// <summary>
/// A guest's own booking, as their own screen and the phone read it.
/// </summary>
/// <remarks>
/// Deliberately not the host's record. It carries no other party's details, no decision-maker's
/// name and no dietary note but their own party's: a guest's screen is not a window into the
/// venue's book.
/// </remarks>
public sealed record MyHostedEventBookingRecord(
    Guid Id,
    Guid HostedEventId,
    string EventName,
    string? EventUrlName,
    string? OrganizationName,
    string? OrganizationUrlName,
    string? VenueName,
    DateTime StartsOn,
    DateTime EndsOn,
    int PartySize,
    HostedEventBookingKind Kind,
    HostedEventBookingStatus Status,
    string? DecisionNote,
    DateTime? GuestAcknowledgedUtc,
    DateTime? CancellationRequestedUtc,
    string? Note,
    IReadOnlyList<HostedEventBookingNightRecord> Nights,
    IReadOnlyList<HostedEventBookingGuestRecord> Guests,

    /// <summary>
    /// When the guest's own hold runs out (item 235 phase 6).
    /// </summary>
    /// <remarks>
    /// The guest's screen counts down from it. Without this the one state with a clock on it read
    /// exactly like the one without: a hold that lapses is a decision the clock takes instead of
    /// the venue, and somebody who was never shown the deadline lost their seats without being
    /// asked. Kept after it lapses, so an expired booking can say when.
    /// </remarks>
    DateTime? HoldExpiresUtc = null);

/// <summary>What a guest, or a host on their behalf, is asking for.</summary>
/// <param name="Nights">
/// Empty for a day pass. For an overnight party, one entry per night with the room wanted; the
/// venue may put them somewhere else when it confirms.
/// </param>
public sealed record RequestHostedEventBookingRequest(
    HostedEventBookingKind Kind,
    int PartySize,
    IReadOnlyList<HostedEventBookingNightChoice>? Nights = null,
    IReadOnlyList<HostedEventBookingGuestInput>? Guests = null,
    string? Note = null);

/// <summary>
/// One night being asked for or given.
/// </summary>
/// <param name="HostedEventLayoutUnitId">
/// The room or seat wanted, or null to say "here that day, sleeping elsewhere" — which is how a
/// three-night event sells the Saturday on its own.
/// </param>
public sealed record HostedEventBookingNightChoice(
    Guid HostedEventNightId, Guid? HostedEventLayoutUnitId = null, int? People = null);

/// <summary>A guest picking their own places on the plan (item 235 phase 4).</summary>
/// <remarks>
/// Separate from asking, because they are different acts with different consequences: an ask holds
/// nothing and joins a queue, a pick takes the squares out of everybody else's reach until the
/// venue answers. One endpoint serving both would have had to guess which the caller meant.
/// </remarks>
public sealed record HoldHostedEventPlacesRequest(
    IReadOnlyList<HostedEventBookingNightChoice> Nights,
    int PartySize,
    IReadOnlyList<HostedEventBookingGuestInput>? Guests = null,
    string? Note = null);

/// <summary>
/// Why a hold could not be taken, and what the plan looks like now (item 235 phase 4).
/// </summary>
/// <param name="Sentence">
/// Names the square that went, because "those seats are taken" in front of four hundred of them
/// tells a guest nothing they can act on.
/// </param>
/// <param name="TakenUnitIds">
/// The squares somebody else got. The picker rings these and leaves the rest of the selection
/// alone, so a party of four who lost one seat does not have to choose all four again.
/// </param>
/// <remarks>
/// Returned as the body of a 409 so the picker can repaint without a second round trip. The whole
/// point of losing a race is that the loser finds out immediately and can choose again while the
/// rest of the row is still free.
/// </remarks>
public sealed record HoldRefusedRecord(
    string Sentence,
    IReadOnlyList<Guid> TakenUnitIds);

public sealed record HostedEventBookingGuestInput(
    string DisplayName,
    Guid? AppUserId = null,
    string? DietaryNotes = null);

/// <summary>
/// A host putting a party in rooms and agreeing to it.
/// </summary>
/// <param name="Nights">
/// Where they actually sleep, which need not be what they asked for. Empty confirms a day pass.
/// </param>
public sealed record ConfirmHostedEventBookingRequest(
    IReadOnlyList<HostedEventBookingNightChoice>? Nights = null,
    string? DecisionNote = null);

/// <summary>Saying no, or releasing a booking already confirmed.</summary>
public sealed record DecideHostedEventBookingRequest(string? DecisionNote = null);

/// <summary>
/// Changing a booking that already exists — a guest dropping out on the Thursday, a party moving
/// rooms. Every field is optional; what is sent is what changes.
/// </summary>
public sealed record EditHostedEventBookingRequest(
    int? PartySize = null,
    IReadOnlyList<HostedEventBookingNightChoice>? Nights = null,
    IReadOnlyList<HostedEventBookingGuestInput>? Guests = null,
    string? Note = null);

/// <summary>
/// A host creating a booking for somebody who asked by phone or at the door.
/// </summary>
/// <param name="LeadAppUserId">
/// The account the booking belongs to. One of this or <paramref name="LeadEmail"/> is required.
/// </param>
/// <param name="LeadEmail">
/// Somebody with no account. Goes through the same guest-invite door the walk-up sign-up uses, so
/// exactly one path creates an account for a person who never asked for one.
/// </param>
public sealed record CreateHostedEventBookingOnBehalfRequest(
    Guid? LeadAppUserId,
    string? LeadEmail,
    string? LeadName,
    HostedEventBookingKind Kind,
    int PartySize,
    IReadOnlyList<HostedEventBookingNightChoice>? Nights = null,
    IReadOnlyList<HostedEventBookingGuestInput>? Guests = null,
    string? Note = null,
    bool ConfirmImmediately = false);

/// <summary>
/// How full each room or seat is on each night, so a host sees the weekend at a glance.
/// </summary>
/// <param name="Taken">People confirmed into it on that night.</param>
/// <param name="Asked">
/// People who have ASKED for it and hold nothing. Shown because over-asking is a fact worth
/// seeing, and because it is the difference between a full house and a popular one.
/// </param>
public sealed record HostedEventUnitNightRecord(
    Guid HostedEventNightId,
    DateTime Date,
    Guid HostedEventLayoutUnitId,
    string UnitName,
    int? Holds,
    int Taken,
    int Asked,

    /// <summary>True when what is on it is a HOLD rather than a confirmation.</summary>
    /// <remarks>
    /// Drawn hatched rather than solid, because "somebody is deciding about this" and "this is
    /// settled" are different facts to a host looking at their own house. Both count against
    /// capacity; only one of them is finished.
    /// </remarks>
    bool Pending = false,

    /// <summary>Why the venue is not offering it that night, or null when it is.</summary>
    /// <remarks>
    /// The reason a guest may read — "not on offer", "the venue is using this one" — never the
    /// venue's own note about it.
    /// </remarks>
    string? NotOffered = null);

/// <summary>How many day passes are out on one night.</summary>
/// <remarks>
/// Day passes were one number for the whole event, which is right for a weekend somebody comes to
/// once and wrong for a three-night run where Saturday is the busy one. A host catering Saturday
/// needs Saturday's number.
/// </remarks>
public sealed record HostedEventDayPassNightRecord(
    Guid HostedEventNightId,
    DateTime Date,
    int Taken,
    int Asked);

/// <summary>The whole booking picture for one event.</summary>
public sealed record HostedEventBookingBoardRecord(
    Guid HostedEventId,
    bool IsOpenForRequests,
    DateTime? BookingsCloseAtUtc,
    int? DayPassCapacity,
    int DayPassesTaken,
    int DayPassesAsked,
    IReadOnlyList<HostedEventLayoutUnitRecord> Units,
    IReadOnlyList<HostedEventUnitNightRecord> UnitNights,
    IReadOnlyList<HostedEventBookingRecord> Bookings,

    /// <summary>Day passes per night, for a run where one night is the busy one.</summary>
    IReadOnlyList<HostedEventDayPassNightRecord>? DayPassNights = null,

    /// <summary>How guests get a place here, which decides what the queue can offer to do.</summary>
    /// <remarks>
    /// Extending a hold is meaningless on an Ask event, and a board that offered it would be
    /// offering a button that does nothing.
    /// </remarks>
    HostedEventBookingMode BookingMode = HostedEventBookingMode.Ask,

    /// <summary>How many holds have already run out and are waiting to be given back.</summary>
    /// <remarks>
    /// The job gives them back within five minutes, but a host looking at a stale screen should see
    /// that the number is about to change rather than wonder why a seat says taken.
    /// </remarks>
    int LapsedHolds = 0);

/// <summary>
/// One person the kitchen has to cook differently for.
/// </summary>
/// <param name="Nights">
/// Which nights that party is here, so a cook planning Saturday knows whether this one is theirs.
/// Empty for a day pass.
/// </param>
public sealed record HostedEventDietaryLineRecord(
    Guid BookingId,
    string LeadName,
    HostedEventBookingStatus Status,
    string GuestName,
    string Notes,
    IReadOnlyList<DateTime> Nights);

/// <summary>
/// How many people said the same thing, word for word.
/// </summary>
/// <remarks>
/// Grouped on the note as typed, lower-cased and with its spacing tidied, and on nothing cleverer.
/// "No nuts" and "nut allergy" are the same requirement to a cook and different strings here, and
/// a tally that guessed they were one thing would eventually merge two that are not.
/// </remarks>
public sealed record HostedEventDietaryTallyRecord(string Notes, int People);

/// <summary>
/// What the kitchen needs to know, for one event.
/// </summary>
/// <param name="PeopleExpected">
/// Everybody in every counted party, whether or not they said anything. The denominator: eight
/// notes out of twelve people is a very different service from eight out of two hundred.
/// </param>
/// <param name="PeopleUnnamed">
/// People counted in a party size that nobody named. A party of four who listed two names has two,
/// and the kitchen is cooking for them without knowing anything about them.
/// </param>
public sealed record HostedEventDietaryRecord(
    Guid HostedEventId,
    bool IncludesRequests,
    int PeopleExpected,
    int PeopleWithNotes,
    int PeopleUnnamed,
    IReadOnlyList<HostedEventDietaryTallyRecord> Tally,
    IReadOnlyList<HostedEventDietaryLineRecord> Lines);

/// <summary>
/// A host asking somebody with no account here to come for the day.
/// </summary>
/// <param name="Email">Where the link goes. Nothing is held until they click it.</param>
/// <param name="PartySize">Carried on the invitation, so the link does not ask them again.</param>
public sealed record InviteHostedEventGuestRequest(
    string Email,
    string? DisplayName = null,
    int PartySize = 1);

/// <summary>
/// What came of asking somebody by email.
/// </summary>
/// <remarks>
/// There is deliberately no booking in here. An emailed invitation holds no room and no day pass
/// until the person clicks it, and returning a booking-shaped answer would tell a host they had
/// reserved something they had not.
/// </remarks>
/// <param name="Sent">
/// False when this deployment has no mail configured. The invitation still exists and the link is
/// in the log, so a host is told the truth rather than left waiting for a letter nobody posted.
/// </param>
public sealed record HostedEventGuestInviteRecord(
    string Email,
    bool Sent,
    DateTime ExpiresUtc);

/// <summary>
/// An event's whole plan: what it allocates, and every room or seat on it.
/// </summary>
/// <param name="Kind">
/// Rooms or seats. One per event — there is no hotel that is also a theatre on the same weekend,
/// and saying it once is what lets a booking screen ask a guest a single answerable question.
/// </param>
/// <param name="DayPassCapacity">
/// How many may come for the day without holding a unit. Null is "we have not limited it"; zero is
/// "we do not sell them", and the two send a host to completely different places.
/// </param>
/// <param name="DayPassPrice">Shown, never charged. Null is "ask the venue"; zero is free.</param>
public sealed record HostedEventLayoutRecord(
    Guid HostedEventId,
    HostedEventLayoutKind Kind,
    int? DayPassCapacity,
    decimal? DayPassPrice,
    IReadOnlyList<HostedEventLayoutUnitRecord> Units);

/// <summary>
/// Why a plan could not be saved, in words AND in ids.
/// </summary>
/// <remarks>
/// The sentence is for the person; the ids are for the designer, which rings the offending units
/// so a venue with four hundred seats is not left reading "C4 and C5 still have confirmed bookings"
/// and hunting for row C. Returned as a 409 body, which is what lets the website tell it apart from
/// a plain refusal.
/// </remarks>
public sealed record LayoutRefusalRecord(
    string Sentence,
    IReadOnlyList<Guid> UnitIds);

// ── who is helping, and what they may do (item 235 phase 7) ──────────────────

/// <summary>
/// Somebody helping at one event.
/// </summary>
/// <remarks>
/// <b>Flags rather than a role</b>, because Ben's own list is not a ladder: a kitchen manager sees
/// the dietary sheet and never the door, and a steward is the other way round. The role label is
/// what a rota calls them and grants nothing.
/// </remarks>
/// <param name="Name">What to call them — their account's name, or the one the invitation used.</param>
/// <param name="Email">
/// Where an invitation went. Null once they have accepted, because the account's own address is
/// the one that matters then and this screen is not a directory.
/// </param>
/// <param name="Accepted">
/// Whether the row grants anything yet. An invitation nobody has clicked has no account attached
/// and so allows nothing at all.
/// </param>
public sealed record HostedEventStaffRecord(
    Guid Id,
    Guid? AppUserId,
    string Name,
    string? Email,
    string? RoleLabel,
    bool SeesBookings,
    bool Decides,
    bool RunsTheDoor,
    bool SeesMenus,
    bool SeesFiles,
    bool Accepted,
    DateTime? DateExpires,
    DateTime DateCreated);

/// <summary>Everybody helping at one event.</summary>
public sealed record HostedEventStaffListRecord(
    Guid HostedEventId,
    IReadOnlyList<HostedEventStaffRecord> Staff);

/// <summary>
/// Adding somebody, or changing what they may do.
/// </summary>
/// <param name="AppUserId">A member of the group. One of this and <paramref name="Email"/>.</param>
/// <param name="Email">
/// Somebody with no account here, who gets a link. A weekend steward is usually this one.
/// </param>
public sealed record SaveHostedEventStaffRequest(
    Guid? AppUserId = null,
    string? Email = null,
    string? DisplayName = null,
    string? RoleLabel = null,
    bool SeesBookings = false,
    bool Decides = false,
    bool RunsTheDoor = false,
    bool SeesMenus = false,
    bool SeesFiles = false);

/// <summary>What somebody accepting a staff invitation is told before they accept it.</summary>
/// <remarks>
/// Named plainly, because the person reading it may never have heard of this site: which event,
/// which venue, which group, and what they are being asked to be able to do.
/// </remarks>
public sealed record HostedEventStaffInviteRecord(
    Guid HostedEventId,
    string EventName,
    string OrganizationName,
    string? VenueName,
    DateTime StartsOn,
    DateTime EndsOn,
    string? RoleLabel,
    IReadOnlyList<string> WhatTheyCanDo,
    bool AlreadyAccepted,

    /// <summary>
    /// Whether the account this was attached to can be signed into.
    /// </summary>
    /// <remarks>
    /// <b>The server's answer, not the page's guess.</b> An account made by clicking this very
    /// link has no password, and the door screen is behind signing in — a steward who finds that
    /// out on the night is a steward standing at a door with a phone that will not let them in. It
    /// is asked of the account rather than inferred from whether the reader happened to be signed
    /// in, which is a different question with the same answer most of the time and the wrong one
    /// exactly when it matters.
    /// </remarks>
    bool AccountHasNoPassword = false);
