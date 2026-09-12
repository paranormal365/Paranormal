using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// How full a hosted event is: per room per night for people staying, and one number for people
/// coming for the day (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Modelled on <see cref="Tours.TourSeats"/>, and for the same reason.</b> Four screens
/// asking the same question four ways is four chances for them to disagree about whether a house
/// is full, and the one that gets it wrong puts two parties in one bed.</para>
///
/// <para><b>Only a confirmed booking holds anything</b> (DECISION 7). A request holds no room and
/// no place, so a queue of hopefuls cannot fill a weekend and the venue keeps taking requests
/// after it is full — which turns the overflow into a waiting list somebody can work through
/// rather than a closed door. Requests are still shown to the host as "asked for", because
/// over-asking is a fact worth seeing.</para>
///
/// <para><b>Capacity counts people, not bookings.</b> A party of four in a room that sleeps two is
/// the mistake this exists to catch, and counting rows would miss it entirely.</para>
///
/// <para><b>A room with no stated capacity cannot be over-filled.</b> Null is "the venue has not
/// said", and refusing bookings against it would make a venue's own rooms unbookable until it
/// filled in a form. Zero is different and is honoured: nobody sleeps in the chapel.</para>
/// </remarks>
public static class EventCapacity
{
    /// <summary>The largest party one booking may ask for.</summary>
    /// <remarks>
    /// Bigger than a walk's, because a family taking a whole floor of a hotel is an ordinary
    /// booking and a coach party is not. A box with no ceiling is a box somebody types 100000 into.
    /// </remarks>
    public const int MaxPartySize = 40;

    /// <summary>A party size somebody may actually ask for.</summary>
    /// <remarks>
    /// Clamped rather than refused, exactly as a walk clamps its seats: "1" is what somebody who
    /// sent nothing meant, and a party of zero is a mistake with an obvious reading.
    /// </remarks>
    public static int ClampPartySize(int? partySize) => Math.Clamp(partySize ?? 1, 1, MaxPartySize);

    /// <summary>Whether this booking's state holds capacity.</summary>
    public static bool Holds(HostedEventBookingStatus status)
        => status == HostedEventBookingStatus.Confirmed;

    /// <summary>What a room sleeps for this event: the event's override, else the room's own.</summary>
    public static int? CapacityOf(HostedEventRoom offered)
        => offered.CapacityOverride ?? offered.PlaceRoom?.Capacity;

    // ── Overnight: per room, per night ───────────────────────────────────────

    /// <summary>
    /// How many people are already sleeping in one room on one night.
    /// </summary>
    /// <param name="bookings">Every booking for the event, with their nights loaded.</param>
    /// <param name="excludingBookingId">
    /// The booking being edited. A party moving from the Blue Room to the Suite must not be
    /// refused by the beds it is about to stop occupying.
    /// </param>
    public static int PeopleIn(
        IEnumerable<HostedEventBooking> bookings,
        Guid nightId,
        Guid placeRoomId,
        Guid? excludingBookingId = null)
        => bookings
            .Where(b => Holds(b.Status))
            .Where(b => excludingBookingId is not { } id || b.Id != id)
            .Where(b => b.Nights.Any(n => n.HostedEventNightId == nightId
                                       && n.PlaceRoomId == placeRoomId))
            .Sum(b => Math.Max(1, b.PartySize));

    /// <summary>Beds still free in a room on a night, or null when the room states no capacity.</summary>
    public static int? BedsLeft(int? capacity, int taken)
        => capacity is int cap ? Math.Max(0, cap - taken) : null;

    /// <summary>
    /// Why this party cannot be confirmed into this room on this night, or null when it can.
    /// </summary>
    /// <remarks>
    /// Names the room and the number left, because a host refused with "full" cannot tell whether
    /// to offer a smaller room, split the party, or say no.
    /// </remarks>
    public static string? WhyThisRoomCannotTakeThem(
        string roomName, DateTime night, int? capacity, int taken, int partySize)
    {
        if (BedsLeft(capacity, taken) is not { } left) return null;
        if (partySize <= left) return null;

        var date = night.ToString("MM/dd/yyyy");
        return left == 0
            ? $"{roomName} is full on {date}. Free a place there first, or put them somewhere else."
            : $"{roomName} sleeps {left} more on {date}, and this is a party of {partySize}.";
    }

    // ── Day passes: one number for the event ─────────────────────────────────

    /// <summary>How many people hold a day pass.</summary>
    public static int DayPassesTaken(
        IEnumerable<HostedEventBooking> bookings, Guid? excludingBookingId = null)
        => bookings
            .Where(b => b.Kind == HostedEventBookingKind.DayPass)
            .Where(b => Holds(b.Status))
            .Where(b => excludingBookingId is not { } id || b.Id != id)
            .Sum(b => Math.Max(1, b.PartySize));

    /// <summary>
    /// Why this party cannot be given day passes, or null when they can.
    /// </summary>
    /// <remarks>
    /// A null capacity means the event has not limited day passes; <b>zero means it does not sell
    /// them at all</b>, and that refusal says so rather than reporting a full house, because the
    /// two send a host to completely different places.
    /// </remarks>
    public static string? WhyTheseDayPassesCannotBeGiven(int? capacity, int taken, int partySize)
    {
        if (capacity is 0) return "This event isn't selling day passes.";
        if (BedsLeft(capacity, taken) is not { } left) return null;
        if (partySize <= left) return null;

        return left == 0
            ? "Day passes are gone. Turn one down first if you want to make room."
            : $"Only {left} day pass{(left == 1 ? "" : "es")} left, and this is a party of {partySize}.";
    }

    // ── The whole booking ────────────────────────────────────────────────────

    /// <summary>
    /// Whether the event is still taking requests.
    /// </summary>
    /// <remarks>
    /// A closed deadline stops NEW requests only. One already waiting on the day it closes is
    /// still the venue's to decide, because a guest who asked in time must not be refused by a
    /// clock while the host was asleep.
    /// </remarks>
    public static bool IsOpenForRequests(HostedEvent ev, DateTime utcNow)
        => ev.BookingsCloseAtUtc is not { } closes || utcNow < closes;
}
