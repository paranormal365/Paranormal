using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>A room this event is offering, as the organiser's screen and the guest's both read it.</summary>
/// <param name="Sleeps">
/// What it sleeps for THIS event — the override where there is one, otherwise the room's own
/// number. Null means the venue has never said, which is allowed and simply cannot be over-filled.
/// </param>
public sealed record HostedEventRoomRecord(
    Guid Id,
    Guid PlaceRoomId,
    string Name,
    string? Floor,
    string? BedNote,
    int? Sleeps,
    int? CapacityOverride,
    string? Note,
    int SortOrder);

/// <summary>Which rooms an event offers. Replaces the whole set, so unticking one removes it.</summary>
public sealed record SetHostedEventRoomsRequest(IReadOnlyList<HostedEventRoomChoice> Rooms);

public sealed record HostedEventRoomChoice(
    Guid PlaceRoomId,
    int? CapacityOverride = null,
    string? Note = null,
    int SortOrder = 0);

/// <summary>One night of one booking: which night, and which room they are in.</summary>
public sealed record HostedEventBookingNightRecord(
    Guid HostedEventNightId,
    DateTime Date,
    Guid PlaceRoomId,
    string RoomName);

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

public sealed record HostedEventBookingNightChoice(Guid HostedEventNightId, Guid PlaceRoomId);

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
/// How full each room is on each night, so a host can see the weekend at a glance.
/// </summary>
/// <param name="Taken">People confirmed into that room on that night.</param>
/// <param name="Asked">
/// People who have ASKED for it and hold nothing. Shown because over-asking is a fact worth
/// seeing, and because it is the difference between a full house and a popular one.
/// </param>
public sealed record HostedEventRoomNightRecord(
    Guid HostedEventNightId,
    DateTime Date,
    Guid PlaceRoomId,
    string RoomName,
    int? Sleeps,
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
    IReadOnlyList<HostedEventRoomRecord> Rooms,
    IReadOnlyList<HostedEventRoomNightRecord> RoomNights,
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
