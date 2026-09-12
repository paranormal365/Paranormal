using Ben.Data.Common.Enums;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The groups of lifecycle states the site asks about, in one place (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>Arrays rather than the entity's own computed properties</b>, because these are used
/// inside <c>IQueryable</c> where a C# property cannot go. EF translates
/// <c>Array.Contains(e.LifecycleState)</c> to an <c>IN</c>, which is also what the index on
/// (organization, state) is shaped for.</para>
///
/// <para><b>The entity keeps the same questions as properties</b> for code holding a loaded row.
/// The two must agree, and a test asserts they do rather than trusting that two lists of the same
/// three words stay the same list — the previous version of this idea was four boolean flags and
/// six screens each combining them differently, which is the bug this replaced.</para>
/// </remarks>
public static class HostedEventStates
{
    /// <summary>Visible to a stranger: on the site, on now, or over but still readable.</summary>
    public static readonly HostedEventLifecycleState[] OnThePublicSite =
    [
        HostedEventLifecycleState.Published,
        HostedEventLifecycleState.Live,
        HostedEventLifecycleState.Ended,
    ];

    /// <summary>
    /// Will still take a booking.
    /// </summary>
    /// <remarks>
    /// Ended is deliberately absent. The event happened; a request arriving afterwards is somebody
    /// who has misread the date, and taking it would put them on a list nobody will ever answer.
    /// </remarks>
    public static readonly HostedEventLifecycleState[] TakingBookings =
    [
        HostedEventLifecycleState.Published,
        HostedEventLifecycleState.Live,
    ];

    /// <summary>
    /// Counts against what the group's price band allows.
    /// </summary>
    /// <remarks>
    /// The same three as the public site, and that is not a coincidence worth collapsing: an event
    /// a stranger can see is one the group is getting the benefit of. They are written out
    /// separately because the day they diverge, they should diverge here and not everywhere.
    /// </remarks>
    public static readonly HostedEventLifecycleState[] CountingAgainstTheBand =
    [
        HostedEventLifecycleState.Published,
        HostedEventLifecycleState.Live,
        HostedEventLifecycleState.Ended,
    ];

    /// <summary>Called off, by whichever side called it.</summary>
    public static readonly HostedEventLifecycleState[] CalledOff =
    [
        HostedEventLifecycleState.Cancelled,
        HostedEventLifecycleState.VenueWithdrawn,
    ];
}
