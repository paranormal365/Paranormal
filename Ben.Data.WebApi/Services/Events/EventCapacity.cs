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
/// <para><b>A held or a confirmed booking holds a unit-night; a request holds nothing</b> (item 235
/// phase 4, superseding DECISION 7's "only Confirmed"). On an Ask event nobody holds anything until
/// the venue agrees, so a queue of hopefuls cannot fill a weekend and the overflow is a waiting
/// list rather than a closed door. On a Pick event what a guest chose is theirs until the venue
/// answers, because a hold that did not count would be a promise the site could not keep. Requests
/// are still shown to the host as "asked for", because over-asking is a fact worth seeing.</para>
///
/// <para><b>A unit is let to ONE party, and capacity bounds that party.</b> Two strangers are never
/// put in the same twin because the arithmetic allowed it, so the question is "whose is this room
/// tonight" and the answer is a booking or nobody. Capacity still refuses a family of five in a
/// double. Counting rows rather than people would miss that entirely.</para>
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
    /// <remarks>
    /// Held as well as Confirmed, since item 235 phase 4. A picked seat that did not count would
    /// be a promise the site could not keep: everybody else would go on being offered it while one
    /// guest believed it was theirs. Requested still counts for nothing, which is what lets an Ask
    /// event keep taking requests after it is full.
    /// </remarks>
    public static bool Holds(HostedEventBookingStatus status)
        => BookingTransitions.Holds(status);

    /// <summary>
    /// What a unit holds for this event: the event's own number, else the room's.
    /// </summary>
    /// <remarks>
    /// A seat carries its own 1 and has no room behind it, so the fallback simply finds nothing —
    /// which is the same answer as a room whose venue has never stated a capacity, and correctly
    /// so in both cases: "we have not said" cannot be over-filled.
    /// </remarks>
    public static int? CapacityOf(HostedEventLayoutUnit unit)
        => unit.Capacity ?? unit.PlaceRoom?.Capacity;

    /// <summary>
    /// What to call a unit: its own label, else the venue's name for the room behind it.
    /// </summary>
    /// <remarks>
    /// A Rooms unit deliberately has no label of its own, so renaming the room renames it
    /// everywhere at once rather than leaving an event showing last year's name for it.
    /// </remarks>
    /// <summary>
    /// What to call what a party holds on one night.
    /// </summary>
    /// <remarks>
    /// A night with no unit is somebody here for the day and going home again, and it says so —
    /// an empty cell there reads as missing data rather than as a fact about the booking.
    /// </remarks>
    public static string NameOf(HostedEventBookingNight night)
        => night.HostedEventLayoutUnit is { } unit ? NameOf(unit) : "Just for the day";

    /// <inheritdoc cref="NameOf(HostedEventBookingNight)"/>
    public static string NameOf(HostedEventLayoutUnit unit)
        => unit.Label?.Trim() is { Length: > 0 } label ? label
         : unit.PlaceRoom?.Name is { Length: > 0 } room ? room
         : "That space";

    // ── Overnight: per room, per night ───────────────────────────────────────

    /// <summary>
    /// How many people are already in one unit on one night.
    /// </summary>
    /// <param name="bookings">Every booking for the event, with their nights loaded.</param>
    /// <param name="excludingBookingId">
    /// The booking being edited. A party moving from the Blue Room to the Suite must not be
    /// refused by the beds it is about to stop occupying.
    /// </param>
    public static int PeopleIn(
        IEnumerable<HostedEventBooking> bookings,
        Guid nightId,
        Guid unitId,
        Guid? excludingBookingId = null)
        => LiveNightsIn(bookings, nightId, unitId, excludingBookingId)
            .Sum(x => x.People);

    /// <summary>
    /// The one party in a unit on a night, or null when nothing live holds it.
    /// </summary>
    /// <remarks>
    /// <para><b>One party, not a sum of heads.</b> A room is let to a party, not to a number of
    /// individuals: two strangers are never put in the same twin because the arithmetic allowed it.
    /// So the question a screen actually asks is "whose is this room tonight", and the answer is a
    /// booking or nobody. The database agrees — a filtered unique index makes a second live party
    /// in the same unit-night impossible to write.</para>
    ///
    /// <para>Capacity still matters, but it bounds the party rather than accumulating strangers:
    /// a family of five cannot take a double.</para>
    /// </remarks>
    public static HostedEventBooking? PartyIn(
        IEnumerable<HostedEventBooking> bookings,
        Guid nightId,
        Guid unitId,
        Guid? excludingBookingId = null)
        => LiveNightsIn(bookings, nightId, unitId, excludingBookingId)
            .Select(x => x.Booking)
            .FirstOrDefault();

    /// <summary>Every live holding of one unit on one night, with how many it is holding for.</summary>
    /// <remarks>
    /// A released night is history and never counts — that is the column the database's own index
    /// filters on, and reading it any other way here would make the two disagree.
    /// </remarks>
    private static IEnumerable<(HostedEventBooking Booking, int People)> LiveNightsIn(
        IEnumerable<HostedEventBooking> bookings,
        Guid nightId,
        Guid unitId,
        Guid? excludingBookingId)
        => bookings
            .Where(b => Holds(b.Status))
            .Where(b => excludingBookingId is not { } id || b.Id != id)
            .SelectMany(b => b.Nights
                .Where(n => n.HostedEventNightId == nightId
                         && n.HostedEventLayoutUnitId == unitId
                         && n.ReleasedUtc is null)
                // People on the night when the party is split across rooms, else the whole party.
                .Select(n => (Booking: b, People: Math.Max(1, n.People ?? b.PartySize))));

    /// <summary>
    /// Whether a unit is on offer on a night at all, given what the venue is holding back.
    /// </summary>
    /// <param name="blocks">Every block on this event's units, night-specific and whole-run.</param>
    public static bool IsOffered(
        IEnumerable<HostedEventUnitBlock> blocks, Guid unitId, Guid nightId)
        => !blocks.Any(b => b.HostedEventLayoutUnitId == unitId
                         && (b.HostedEventNightId is null || b.HostedEventNightId == nightId));

    /// <summary>Why the venue is not offering this one, in words a guest may read.</summary>
    /// <remarks>
    /// The venue's own note is never returned. "Mrs Cole's family" is exactly what a host writes on
    /// a block and exactly what must not reach a public plan; what a guest gets is the difference
    /// between "not on offer" and "the venue is using this one".
    /// </remarks>
    public static string? WhyItIsNotOffered(
        IEnumerable<HostedEventUnitBlock> blocks, Guid unitId, Guid nightId, string unitName)
    {
        var block = blocks.FirstOrDefault(
            b => b.HostedEventLayoutUnitId == unitId
              && (b.HostedEventNightId is null || b.HostedEventNightId == nightId));

        return block is null ? null
            : block.Kind == HostedEventBlockKind.HouseHeld
                ? $"{unitName} is being used by the venue that night."
                : $"{unitName} is not on offer that night.";
    }

    /// <summary>Beds still free in a room on a night, or null when the room states no capacity.</summary>
    public static int? BedsLeft(int? capacity, int taken)
        => capacity is int cap ? Math.Max(0, cap - taken) : null;

    /// <summary>
    /// Why this party cannot be confirmed into this unit on this night, or null when it can.
    /// </summary>
    /// <remarks>
    /// <para>Names the unit and the number left, because a host refused with "full" cannot tell
    /// whether to offer a smaller room, split the party, or say no.</para>
    ///
    /// <para><b>The verb follows the layout.</b> A room "sleeps" two more and a seat "seats" them;
    /// a theatre told its seats sleep nobody would read as a bug, and the whole point of naming
    /// the number is that the sentence is one a host can act on without translating it.</para>
    /// </remarks>
    public static string? WhyThisRoomCannotTakeThem(
        string unitName, DateTime night, int? capacity, int taken, int partySize,
        HostedEventLayoutKind kind = HostedEventLayoutKind.Rooms)
    {
        if (BedsLeft(capacity, taken) is not { } left) return null;
        if (partySize <= left) return null;

        var date = night.ToString("MM/dd/yyyy");
        if (left == 0)
            return kind == HostedEventLayoutKind.Seats
                ? $"{unitName} is taken on {date}. Free it first, or seat them somewhere else."
                : $"{unitName} is full on {date}. Free a place there first, or put them somewhere else.";

        var verb = kind == HostedEventLayoutKind.Seats ? "seats" : "sleeps";
        return $"{unitName} {verb} {left} more on {date}, and this is a party of {partySize}.";
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
