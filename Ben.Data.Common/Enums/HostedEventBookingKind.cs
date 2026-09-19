namespace Ben.Data.Common.Enums;

/// <summary>
/// What a party booked: a bed for the night, or a ticket for the day.
/// </summary>
/// <remarks>
/// <para>Two kinds because they consume two different capacities. An overnight party occupies a
/// room on named nights and is counted per room per night; a day pass occupies nothing but a place
/// in the building and is counted against one number for the event. A single "how many are
/// coming" would let a venue that sleeps eight sell forty beds.</para>
///
/// <para>Append-only. The number is stored.</para>
/// </remarks>
public enum HostedEventBookingKind
{
    /// <summary>A room, on one or more nights. Carries <c>HostedEventBookingNight</c> rows.</summary>
    Overnight = 0,

    /// <summary>
    /// Attending without staying. Counted against the event's day-pass capacity, and the reason a
    /// booking may exist with no room-nights at all.
    /// </summary>
    DayPass = 1,
}
