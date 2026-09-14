using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// When to write to somebody about new bookings, and when to wait (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>A request is answered because somebody was told.</b> A queue nobody hears about fills up
/// while the venue is out, and a party who asked on Monday and heard nothing by Friday has booked
/// somewhere else. So the first request gets a letter straight away.</para>
///
/// <para><b>And a rush is one letter, not forty.</b> The weekend a venue announces its dates, forty
/// people ask in an hour; forty letters teach a venue manager to filter the site's mail into a
/// folder they never open, which is the silence this exists to prevent by a longer route. Anything
/// arriving within fifteen minutes of the last letter is part of the same rush and waits for one
/// summary, sent once things have been quiet for fifteen minutes — or after an hour regardless, so a
/// steady trickle all afternoon cannot postpone it for ever.</para>
///
/// <para><b>Pure, and here rather than in the job</b>, because the timing is the whole claim and it
/// is worth being certain about without a clock or a mail server around it.</para>
/// </remarks>
public static class EventBookingAlerts
{
    /// <summary>How long after a letter an arrival still counts as part of the same rush.</summary>
    public static readonly TimeSpan SameRush = TimeSpan.FromMinutes(15);

    /// <summary>How long a rush may run before its summary goes regardless.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    /// <summary>
    /// How far back a person with no history is told about.
    /// </summary>
    /// <remarks>
    /// An hour, so turning this on — or making somebody a decider — does not post them last month's
    /// queue. What is older than that is on the board and in the digest.
    /// </remarks>
    public static readonly TimeSpan FirstLook = TimeSpan.FromHours(1);

    /// <summary>What to do about one person's new bookings at one event.</summary>
    public enum Send { Nothing, Now, Summary }

    /// <param name="Covers">The bookings the letter is about, oldest first.</param>
    /// <param name="CoversUpToUtc">What the person's cursor moves to once it has gone.</param>
    public sealed record Decision(Send Send, IReadOnlyList<HostedEventBooking> Covers, DateTime? CoversUpToUtc);

    /// <summary>The bookings that are still waiting on somebody — the ones worth a letter.</summary>
    public static bool IsWaiting(HostedEventBookingStatus status)
        => status is HostedEventBookingStatus.Requested or HostedEventBookingStatus.Held;

    /// <summary>
    /// Whether to write now, write the summary, or wait.
    /// </summary>
    /// <param name="bookings">
    /// Bookings at this event. Filtered here to the ones that are new to this person and still
    /// waiting, so the rule for "new" lives in one place.
    /// </param>
    /// <param name="recipientId">
    /// Who the letter is for. Their own booking is never news to them — an owner who books a room at
    /// their own weekend does not need telling that somebody asked.
    /// </param>
    public static Decision Decide(
        EventBookingAlertState? state,
        IEnumerable<HostedEventBooking> bookings,
        Guid recipientId,
        DateTime now)
    {
        var since = state?.AlertsCoverUpToUtc ?? now - FirstLook;

        var fresh = bookings
            .Where(b => IsWaiting(b.Status)
                     && b.DateCreated > since
                     && b.LeadAppUserId != recipientId)
            .OrderBy(b => b.DateCreated)
            .ToList();

        if (fresh.Count == 0) return new Decision(Send.Nothing, [], null);

        var oldest = fresh[0].DateCreated;
        var newest = fresh[^1].DateCreated;

        // A NEW RUSH: nobody has been written to about this event, or the last letter was long
        // enough before these arrived that they are not the same burst. Write now.
        if (state?.LastAlertUtc is not { } lastLetter || oldest - lastLetter > SameRush)
            return new Decision(Send.Now, fresh, newest);

        // THE SAME RUSH: hold, and send one summary once it has gone quiet — or once it has run
        // long enough that waiting any longer would be the silence this exists to prevent.
        var quiet = now - newest >= SameRush;
        var tooLong = now - oldest >= LongestWait;

        return quiet || tooLong
            ? new Decision(Send.Summary, fresh, newest)
            : new Decision(Send.Nothing, [], null);
    }
}
