namespace Ben.Data.Common.Enums;

/// <summary>
/// How a guest gets a place: they pick one, or they ask for one (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>The venue chooses, per event.</b> Ben, 2026-09-12: "For seating that is like dragging
/// whatever seats you are trying to reserve or clicking them on and off with mouse. It would put
/// those seats in a waiting for confirmation mode until the event organizer confirms them and then
/// they are locked in." That is Pick. But a hotel placing families in rooms does not want a guest
/// taking the Suite off the board at midnight, and for that event asking is right — so it is a
/// choice and not a rule.</para>
///
/// <para><b>It changes what a request holds.</b> Asking holds nothing, which is what lets an event
/// keep taking requests after it is full and turn the overflow into a waiting list. Picking holds
/// the seats for a set time, which is what stops two people spending an evening on the same row.
/// The rest of the site reads the mode rather than guessing from whether a hold exists.</para>
///
/// <para><b>Append only.</b> The numbers are stored.</para>
/// </remarks>
public enum HostedEventBookingMode
{
    /// <summary>
    /// The guest says what they would like and the venue places them.
    /// </summary>
    /// <remarks>
    /// Zero, because it is what every event on the branch already did. A backfill that had to guess
    /// would guess wrong for exactly the events that already have bookings.
    /// </remarks>
    Ask = 0,

    /// <summary>
    /// The guest picks on the plan, and what they pick is held until the venue answers.
    /// </summary>
    Pick = 1,
}
