namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Where a replay's playhead is, MEASURED rather than counted.
/// </summary>
/// <remarks>
/// <para>
/// The Field Kit player used to advance its playhead by a fixed <c>0.25 × rate</c> on every tick,
/// which counts TICKS rather than measuring time. A tick here is <c>Task.Delay</c> plus a map
/// update plus a full Blazor Server render pushed down the circuit, so it is reliably longer than
/// the delay it asked for — and never shorter. A session played at 16× therefore ran slower than
/// 16×, and the busier the server or the slower the connection, the slower it went. Nothing said
/// so: the elapsed readout, the scrubber, the trace and the map all agreed with each other and
/// all lagged together.
/// </para>
/// <para>
/// The same fault was found and fixed in the phone's replay on 2026-09-11
/// (<c>BenKit/Field/SessionReplay.swift</c>); this is its other half. The arithmetic lives here,
/// out of the component, so it can be checked without a scheduler — the scheduler being exactly
/// what cannot be relied on.
/// </para>
/// </remarks>
public static class ReplayClock
{
    /// <summary>
    /// The playhead after <paramref name="elapsed"/> of real time at <paramref name="rate"/>,
    /// measured from <paramref name="anchor"/> and never past <paramref name="end"/>.
    /// </summary>
    /// <remarks>
    /// A tick that arrives late moves the playhead further, which is what keeps a replay at the
    /// speed its label claims. The clamp matters for the same reason: a starved ticker can wake
    /// long after the session finished, and the session never had that moment.
    /// </remarks>
    public static DateTime Playhead(DateTime anchor, TimeSpan elapsed, double rate, DateTime end)
    {
        var moved = anchor.AddSeconds(elapsed.TotalSeconds * rate);
        return moved > end ? end : moved;
    }
}
