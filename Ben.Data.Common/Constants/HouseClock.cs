namespace Ben.Data.Common.Constants;

/// <summary>
/// The clock this site keeps when an event does not name one of its own.
/// </summary>
/// <remarks>
/// <para>It was already the house default in three places — <c>TourController</c> and
/// <c>HostedEventController</c> both stamp it on a row whose request left the zone out, and the
/// seeders write it on everything — but it was written out by hand each time, so a fourth caller
/// could disagree with the other three and nothing would notice.</para>
///
/// <para>The fourth caller is what went wrong. An ordinary calendar event's zone is nullable and
/// nothing stamped it, so the public What's On list and the event's own public page rendered a
/// night walk at a cave in Adams, Tennessee as <b>8:00 PM UTC</b> — five hours out, on the page a
/// stranger reads before deciding whether to come (first-run walk, 2026-09-20).</para>
///
/// <para>Named rather than inferred from the group's address on purpose: an address is optional
/// on this site, a group works away from its base regularly, and a guess that changes as somebody
/// edits their address would move times that are already advertised.</para>
/// </remarks>
public static class HouseClock
{
    /// <summary>Central. The IANA id, because Windows' own ids are not portable.</summary>
    public const string ZoneId = "America/Chicago";
}
