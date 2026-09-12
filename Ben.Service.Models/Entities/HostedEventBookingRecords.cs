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
    DateTime DateCreated,
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
