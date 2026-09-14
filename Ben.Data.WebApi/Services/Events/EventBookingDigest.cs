using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// What a venue's digest says, and when it is due (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>Never when there is nothing to say.</b> A daily letter reading "nothing new" is a letter
/// that teaches somebody to stop opening them, and the next one — the one with a party waiting three
/// days — goes unread with it.</para>
///
/// <para><b>Daily while bookings are open, weekly otherwise</b>, because those are the two tempos a
/// venue actually works at: the weeks a weekend is selling, and the weeks it has sold and is only
/// being tidied.</para>
///
/// <para>Pure, like the alert timing, so both the cadence and the contents can be tested without a
/// clock, a database or a mail server.</para>
/// </remarks>
public static class EventBookingDigest
{
    /// <summary>How soon a hold lapsing counts as worth a line of its own.</summary>
    public static readonly TimeSpan SoonLapsing = TimeSpan.FromDays(1);

    /// <param name="Waiting">Undecided, oldest first — the ones most likely to give up.</param>
    /// <param name="Lapsing">Holds that run out within a day, soonest first.</param>
    /// <param name="Left">Free places on the next night, by section.</param>
    /// <param name="ArrivedLastNight">People through the door last night, while the event is on.</param>
    public sealed record Contents(
        DateTime AsOfUtc,
        IReadOnlyList<HostedEventBooking> Waiting,
        IReadOnlyList<HostedEventBooking> Lapsing,
        IReadOnlyList<(string Section, int Free)> Left,
        int? ArrivedLastNight)
    {
        /// <summary>
        /// Nothing waiting, nothing lapsing, nobody arrived — so nothing to write.
        /// </summary>
        /// <remarks>
        /// What is left does not count on its own. "Twelve seats free" every morning of a quiet
        /// week is precisely the letter that trains somebody to ignore the next one.
        /// </remarks>
        public bool IsEmpty => Waiting.Count == 0 && Lapsing.Count == 0 && ArrivedLastNight is null or 0;
    }

    /// <summary>
    /// Whether this person's digest for this event is due.
    /// </summary>
    /// <remarks>
    /// <b>An event that is over, called off or filed away sends none.</b> There is nothing left to
    /// decide, and the lifecycle job deals with whatever was left undecided.
    /// </remarks>
    public static bool IsDue(HostedEvent ev, DateTime? lastDigestUtc, DateTime now)
    {
        if (!HostedEventStates.TakingBookings.Contains(ev.LifecycleState)) return false;

        var cadence = EventCapacity.IsOpenForRequests(ev, now)
            ? TimeSpan.FromDays(1)
            : TimeSpan.FromDays(7);

        // A few minutes' grace, so a job that ran at 8:02 yesterday is not a day late at 8:01.
        return lastDigestUtc is not { } last || now - last >= cadence - TimeSpan.FromMinutes(10);
    }

    /// <summary>Everything the digest says, from rows already in hand.</summary>
    /// <param name="units">The event's plan, for what is left.</param>
    /// <param name="blocks">What the venue is holding back.</param>
    /// <param name="arrivals">Last night's check-ins, when the event is on.</param>
    public static Contents Build(
        HostedEvent ev,
        IReadOnlyList<HostedEventBooking> bookings,
        IReadOnlyList<HostedEventLayoutUnit> units,
        IReadOnlyList<HostedEventUnitBlock> blocks,
        IReadOnlyList<HostedEventCheckIn> arrivals,
        DateTime now)
    {
        var waiting = bookings
            .Where(b => EventBookingAlerts.IsWaiting(b.Status))
            .OrderBy(b => b.DateCreated)
            .ToList();

        var lapsing = bookings
            .Where(b => b.Status == HostedEventBookingStatus.Held
                     && b.HoldExpiresUtc is { } at && at > now && at - now <= SoonLapsing)
            .OrderBy(b => b.HoldExpiresUtc)
            .ToList();

        var zone = HostedEventCalendarSync.ZoneOf(ev.TimeZoneId);
        var today = TimeZoneInfo.ConvertTimeFromUtc(now, zone).Date;
        var nights = ev.Nights.OrderBy(n => n.Date).ToList();

        // WHAT IS LEFT ON THE NEXT NIGHT, not summed across the run. "Forty seats free" added up over
        // three nights is a number that describes no evening anybody can come to.
        var next = nights.FirstOrDefault(n => n.Date.Date >= today);
        var left = new List<(string, int)>();

        if (next is not null && units.Count > 0)
        {
            var taken = bookings
                .Where(b => BookingTransitions.Holds(b.Status))
                .SelectMany(b => b.Nights)
                .Where(n => n.HostedEventNightId == next.Id && n.ReleasedUtc is null
                         && n.HostedEventLayoutUnitId is not null)
                .Select(n => n.HostedEventLayoutUnitId!.Value)
                .ToHashSet();

            var blocked = blocks
                .Where(b => b.HostedEventNightId is null || b.HostedEventNightId == next.Id)
                .Select(b => b.HostedEventLayoutUnitId)
                .ToHashSet();

            left = [.. units
                .Where(u => !taken.Contains(u.Id) && !blocked.Contains(u.Id))
                .GroupBy(u => u.Section is { Length: > 0 } s
                    ? s
                    : ev.LayoutKind == HostedEventLayoutKind.Seats ? "Seats" : "Rooms")
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, g.Count()))];
        }

        // LAST NIGHT'S DOOR, only while the event is on — which is the one stretch a venue wants a
        // morning number more than a queue.
        int? arrivedLastNight = null;
        if (ev.LifecycleState == HostedEventLifecycleState.Live
            && nights.FirstOrDefault(n => n.Date.Date == today.AddDays(-1)) is { } lastNight)
        {
            arrivedLastNight = arrivals
                .Where(a => a.HostedEventNightId == lastNight.Id)
                .Sum(a => a.People
                    ?? bookings.FirstOrDefault(b => b.Id == a.HostedEventBookingId)?.PartySize
                    ?? 1);
        }

        return new Contents(now, waiting, lapsing, left, arrivedLastNight);
    }
}
