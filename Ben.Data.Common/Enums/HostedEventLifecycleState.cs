namespace Ben.Data.Common.Enums;

/// <summary>
/// Where a hosted event is in its life, as one answer (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>It replaces four independent flags</b> — <c>IsPublished</c>, <c>CancelledAtUtc</c>,
/// <c>ArchivedAtUtc</c> and an implied "it is on right now" — which between them could describe a
/// published, cancelled, archived event and left every screen to decide for itself which of those
/// facts won. They did not agree: the list badge, the public page and the entitlement count each
/// read a different pair. One column with one value is the only version of this that cannot drift.
/// </para>
///
/// <para><b>The timestamps stay</b> and are still written. The state says WHAT it is; the stamps
/// say WHEN it became that, which a person on a support call asks for and a state cannot answer.
/// Nothing may read a stamp to decide what an event is.</para>
///
/// <para><b>The job drives the middle three.</b> Published becomes Live at the first night's start
/// in the venue's own zone, Live becomes Ended after the last one, and Ended becomes Archived
/// fourteen days later. Nobody presses a button for those and nobody should have to: an event that
/// happened last month should not still be advertising itself because its organizer never came
/// back.</para>
///
/// <para><b>Append only.</b> The numbers are stored.</para>
/// </remarks>
public enum HostedEventLifecycleState
{
    /// <summary>
    /// Being set up. Nobody outside the group can see it, and nothing has been paid for it.
    /// </summary>
    /// <remarks>
    /// Zero, so every row that existed before this column did and was not published lands here
    /// without a backfill having to guess.
    /// </remarks>
    Draft = 0,

    /// <summary>On the public site and taking bookings. This is the state a credit buys.</summary>
    Published = 1,

    /// <summary>
    /// Happening now — somewhere between the start of the first night and the end of the last.
    /// </summary>
    /// <remarks>
    /// Its own state rather than a date comparison because half the site asks the question. The
    /// door only opens for a Live event, the board leads with arrivals rather than requests, and
    /// the public page says "on now" instead of "book". Every one of those computing its own answer
    /// from two dates and a time zone is three chances to disagree about whether tonight has begun.
    /// </remarks>
    Live = 2,

    /// <summary>Over. Still readable, still on the group's page, no longer taking anybody.</summary>
    Ended = 3,

    /// <summary>Filed away. Off the lists, out of the counts, nothing destroyed.</summary>
    Archived = 4,

    /// <summary>
    /// Called off by the organizer.
    /// </summary>
    /// <remarks>
    /// Terminal in the sense that the job never moves it, but not in the sense that it cannot be
    /// undone: an event called off by mistake can be brought back, and the credit that came back
    /// with the cancellation is spent again if it is still spendable. What cannot be undone is that
    /// everybody who had a place was told it was off.
    /// </remarks>
    Cancelled = 5,

    /// <summary>
    /// The venue withdrew. Not the organizer's doing, and treated differently for that reason.
    /// </summary>
    /// <remarks>
    /// The credit comes back whatever the timing, where an organizer's own cancellation has a
    /// forty-eight hour window. Somebody whose venue pulled out two days beforehand has already had
    /// the worse week.
    /// </remarks>
    VenueWithdrawn = 6,

    /// <summary>
    /// Taken off the site by IsHaunted (item 235 phase 17b).
    /// </summary>
    /// <remarks>
    /// Called off, like the two above, so nothing can be booked and everybody with a place is told — but also off the
    /// public site entirely, because the reason an event is removed is usually what its page says. The credit comes back
    /// whatever the timing. The organizer is sent a generic letter and may appeal; an upheld appeal brings the event
    /// back as a draft.
    /// </remarks>
    Removed = 7,
}
