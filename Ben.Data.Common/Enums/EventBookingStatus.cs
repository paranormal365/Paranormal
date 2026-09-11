namespace Ben.Data.Common.Enums;

/// <summary>
/// Where a party's place at a hosted event stands (item 235).
/// </summary>
/// <remarks>
/// <para><b>Nobody has a place until the venue says so</b>, and what the venue is saying is that
/// the money is settled between the two of them. The site does not take it — exactly the rule
/// <see cref="TourSeatStatus"/> was built on, restated here because it is the whole shape of the
/// thing.</para>
///
/// <para>A separate enum from <see cref="TourSeatStatus"/> rather than a shared one: a booking can
/// be cancelled after it was confirmed, and a walk's seat never can. Sharing the enum would freeze
/// both to whichever grew slower, and a reader of either would have to know which cases were
/// really theirs.</para>
///
/// <para>Append-only.</para>
/// </remarks>
public enum EventBookingStatus
{
    /// <summary>Asked for, holding nothing. A full event still takes the ask — that is a waiting
    /// list, not a closed door.</summary>
    Requested = 0,

    /// <summary>The venue has a room for them and says the money is settled.</summary>
    Confirmed = 1,

    /// <summary>The venue could not take this one. The row is kept, so somebody who is not coming
    /// can see that they are not coming.</summary>
    TurnedDown = 2,

    /// <summary>Called off after it was confirmed, by either side.</summary>
    Cancelled = 3,
}
