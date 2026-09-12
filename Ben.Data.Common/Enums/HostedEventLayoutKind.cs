namespace Ben.Data.Common.Enums;

/// <summary>
/// What an event allocates to its guests: rooms to sleep in, or seats to sit in
/// (item 235 phase 2.4).
/// </summary>
/// <remarks>
/// <para><b>One per event</b> (Ben, 2026-09-12). There is no hotel that is also a theatre on the
/// same weekend, so an event picks a kind and gets one plan. Making it a choice rather than a set
/// is what keeps the booking screen answerable: a guest is asked for a room, or asked for a seat,
/// never asked which sort of thing they would like to be asked for.</para>
///
/// <para><b>Both kinds are the same shape underneath</b>, which is why they share a model. A room
/// and a seat are each picked by the guest while booking, and each bounds how many people can come
/// at all — so each is capacity-checked at the moment the venue confirms.</para>
///
/// <para><b>A dining plan is deliberately absent</b>, and the reasoning is worth keeping. A table
/// bounds nothing: everybody who booked the weekend eats on Saturday whether or not the tables
/// have been arranged. And a table is assigned by the organiser AFTER bookings close, from the
/// confirmed list — the opposite workflow from a guest choosing a room at the moment they ask.
/// Seating people at dinner is therefore an assignment laid over confirmed bookings, sharing this
/// designer's canvas but not this enum, and it is better for waiting until the dietary notes exist
/// to arrange people around.</para>
///
/// <para><b>Append only.</b> The numbers are stored.</para>
/// </remarks>
public enum HostedEventLayoutKind
{
    /// <summary>
    /// Rooms people sleep in, allocated per night.
    /// </summary>
    /// <remarks>
    /// The default, because a hosted event is an overnight stay until somebody says otherwise, and
    /// because it is the only kind whose units are backed by the venue's own described rooms —
    /// which carry the history that is half the reason anybody is booking.
    /// </remarks>
    Rooms = 1,

    /// <summary>
    /// Seats people sit in, one person each.
    /// </summary>
    /// <remarks>
    /// A seat holds exactly one person and the designer enforces it. A "seat" that sleeps three is
    /// not a seat, and letting one exist would quietly break every count that trusts the number.
    /// </remarks>
    Seats = 2,
}
