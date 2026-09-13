namespace Ben.Data.Common.Enums;

/// <summary>
/// How somebody who decides bookings wants to hear about them (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>A request is answered because somebody was told.</b> A queue nobody hears about is a
/// queue that fills up while the venue is out, and a party who asked on Monday and heard nothing by
/// Friday has booked somewhere else.</para>
///
/// <para><b>Per group, not per site.</b> Somebody who runs a hotel's weekends and helps at a
/// friend's theatre wants to hear about the first as it happens and about the second once a week,
/// and a single switch could say only one of those.</para>
///
/// <para>Stored as a number and append-only, like every other enumeration here. No row at all means
/// <see cref="AsItHappens"/>, because the failure worth avoiding is the silent one.</para>
/// </remarks>
public enum EventBookingAlertMode
{
    /// <summary>A letter when somebody asks, collapsed while a rush is on — and the digest.</summary>
    AsItHappens = 0,

    /// <summary>The digest only: one letter a day while bookings are open, one a week otherwise.</summary>
    DigestOnly = 1,

    /// <summary>
    /// Nothing by email. The bell still counts, because the bell is not a letter.
    /// </summary>
    /// <remarks>
    /// Kept as a real choice, not hidden: a venue manager who works the board every morning does not
    /// need a letter telling them what they are about to look at.
    /// </remarks>
    Off = 2,
}
