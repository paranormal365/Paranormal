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
    IReadOnlyList<HostedEventBookingGuestRecord> Guests);

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
    IReadOnlyList<HostedEventBookingGuestRecord> Guests);

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
    Guid HostedEventNightId, Guid? HostedEventLayoutUnitId = null);

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
    IReadOnlyList<HostedEventBookingRecord> Bookings);

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
