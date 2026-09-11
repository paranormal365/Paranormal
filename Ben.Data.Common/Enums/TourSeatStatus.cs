namespace Ben.Data.Common.Enums;

/// <summary>
/// Where a sign-up for a tour date has got to (item 234, Ben 2026-09-10).
/// </summary>
/// <remarks>
/// <para>Ben: <i>"They would not be confirmed until the tour guide or manager approves them meaning
/// they have settled how money will be or has been exchanged."</i> <b>This site never takes the
/// money.</b> The state is the two of them agreeing that it is settled, and nothing here should
/// ever be read as a payment record.</para>
///
/// <para><b>Separate from <see cref="RsvpStatus"/> on purpose.</b> That enum is read in more than
/// twenty places, including investigations, where "reserved" would mean nothing. A tour date's
/// attendee carries BOTH: <see cref="Requested"/> alongside <c>Invited</c>, then
/// <see cref="Reserved"/> alongside <c>Accepted</c>. So every existing count of accepted attendees
/// goes on meaning <i>has a place</i>, and not one of them had to learn what a tour is.</para>
///
/// <para>Null — the default for every attendee row that already exists and for every event that is
/// not a tour date — means the old rule: signing up is coming.</para>
/// </remarks>
public enum TourSeatStatus
{
    /// <summary>Asked for. Holds no place yet, and the business has not looked at it.</summary>
    Requested = 0,

    /// <summary>The guide or manager approved it. The places are held.</summary>
    Reserved = 1,

    /// <summary>The business said no. Said plainly, because a guest who is not coming must know.</summary>
    TurnedDown = 2,
}
