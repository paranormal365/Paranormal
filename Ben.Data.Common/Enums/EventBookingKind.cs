namespace Ben.Data.Common.Enums;

/// <summary>What a party bought their way into (item 235).</summary>
/// <remarks>
/// Ben, 2026-09-11: a venue "may sell access to people outside of those staying at the location".
/// So the two are counted differently — an overnight party holds a room on each of its nights, a
/// day pass holds none and is capped on its own.
/// </remarks>
public enum EventBookingKind
{
    /// <summary>Staying at the venue: holds a room on each night it booked.</summary>
    Overnight = 0,

    /// <summary>Coming for the event but not sleeping there.</summary>
    DayPass = 1,
}
