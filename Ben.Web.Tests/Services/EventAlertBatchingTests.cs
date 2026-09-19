using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A rush of bookings is one letter and one summary, not forty letters (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>Both halves of the claim matter.</b> A request nobody hears about is the failure this
/// phase exists for, so the first one must be written about straight away. And forty letters in an
/// hour teach a venue manager to filter the site's mail into a folder they never open, which is the
/// same silence by a longer route.</para>
///
/// <para>These run the rule the way the job does: a pass every five minutes, the cursor moved after
/// every letter, the clock advanced by hand. No mail server and no sleeping — the timing is the
/// thing under test and it should not depend on how fast the machine is.</para>
/// </remarks>
public sealed class EventAlertBatchingTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid Venue = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static HostedEventBooking Request(DateTime at, Guid? lead = null,
        HostedEventBookingStatus status = HostedEventBookingStatus.Requested) => new()
        {
            Id = Guid.NewGuid(),
            HostedEventId = EventId,
            LeadAppUserId = lead ?? Guid.NewGuid(),
            Status = status,
            PartySize = 2,
            DateCreated = at,
        };

    /// <summary>
    /// Runs the job's pass every five minutes from <paramref name="from"/> to
    /// <paramref name="until"/> and returns every letter it would have sent.
    /// </summary>
    private static List<(DateTime At, EventBookingAlerts.Send Kind, int Covers)> Run(
        IReadOnlyList<HostedEventBooking> bookings, DateTime from, DateTime until,
        EventBookingAlertState? state = null)
        => RunCovering(bookings, from, until, state)
            .Select(l => (l.At, l.Kind, l.Covered.Count))
            .ToList();

    /// <summary>The same pass, keeping which bookings each letter told somebody about.</summary>
    private static List<(DateTime At, EventBookingAlerts.Send Kind, IReadOnlyList<HostedEventBooking> Covered)> RunCovering(
        IReadOnlyList<HostedEventBooking> bookings, DateTime from, DateTime until,
        EventBookingAlertState? state = null)
    {
        state ??= new EventBookingAlertState { AppUserId = Venue, HostedEventId = EventId };
        var letters = new List<(DateTime, EventBookingAlerts.Send, IReadOnlyList<HostedEventBooking>)>();

        for (var now = from; now <= until; now = now.AddMinutes(5))
        {
            // Only what has actually been made by this moment exists yet.
            var existing = bookings.Where(b => b.DateCreated <= now).ToList();

            var decision = EventBookingAlerts.Decide(state, existing, Venue, now);
            if (decision.Send == EventBookingAlerts.Send.Nothing) continue;

            letters.Add((now, decision.Send, decision.Covers));
            state.LastAlertUtc = now;
            state.AlertsCoverUpToUtc = decision.CoversUpToUtc;
        }

        return letters;
    }

    // ── the rush ─────────────────────────────────────────────────────────────

    [Fact]
    public void Forty_requests_in_an_hour_are_one_letter_and_one_summary()
    {
        // The plan's own case. One every ninety seconds, for an hour.
        var bookings = Enumerable.Range(0, 40)
            .Select(i => Request(Start.AddSeconds(90 * i)))
            .ToList();

        var letters = Run(bookings, Start, Start.AddHours(3));

        Assert.Equal(2, letters.Count);
        Assert.Equal(EventBookingAlerts.Send.Now, letters[0].Kind);
        Assert.Equal(EventBookingAlerts.Send.Summary, letters[1].Kind);

        // Nobody is missed: between them the two letters cover all forty.
        Assert.Equal(40, letters.Sum(l => l.Covers));
    }

    [Fact]
    public void Two_quick_requests_are_one_letter()
    {
        // The plan's "verified by": two requests a few seconds apart, one letter saying "and one
        // more" rather than two letters a minute apart.
        var bookings = new[] { Request(Start), Request(Start.AddSeconds(20)) };

        var letters = Run(bookings, Start.AddMinutes(1), Start.AddHours(2));

        var letter = Assert.Single(letters);
        Assert.Equal(2, letter.Covers);
    }

    [Fact]
    public void The_first_request_is_written_about_on_the_next_pass_not_after_a_wait()
    {
        // The other half: batching must never delay the letter about a quiet evening's one request.
        var bookings = new[] { Request(Start) };

        var letters = Run(bookings, Start, Start.AddHours(1));

        Assert.Equal(Start, Assert.Single(letters).At);
    }

    [Fact]
    public void A_steady_trickle_cannot_postpone_the_summary_for_ever()
    {
        // One every ten minutes all afternoon never goes quiet for fifteen minutes. Without the
        // hour's ceiling the summary would wait until the evening, which is the silence this is for.
        var bookings = Enumerable.Range(0, 30)
            .Select(i => Request(Start.AddMinutes(10 * i)))
            .ToList();

        var letters = RunCovering(bookings, Start, Start.AddHours(6));

        // THE PROMISE IS PER REQUEST, not per letter. The first draft of this test measured the
        // gap between letters and failed at seventy minutes — a true fact about spacing and the
        // wrong claim: what matters to a guest is how long THEIR request waited before somebody was
        // told. That is at most the hour's ceiling and one pass, whatever the rhythm of the rest.
        foreach (var (at, _, covered) in letters)
            foreach (var booking in covered)
                Assert.True(at - booking.DateCreated <= EventBookingAlerts.LongestWait.Add(TimeSpan.FromMinutes(5)),
                    $"a request made at {booking.DateCreated:HH:mm} was not told about until {at:HH:mm}");

        Assert.Equal(30, letters.Sum(l => l.Covered.Count));
    }

    [Fact]
    public void A_request_after_a_quiet_spell_is_a_new_rush_and_is_written_about_at_once()
    {
        var bookings = new[] { Request(Start), Request(Start.AddHours(3)) };

        var letters = Run(bookings, Start, Start.AddHours(4));

        Assert.Equal(2, letters.Count);
        Assert.All(letters, l => Assert.Equal(EventBookingAlerts.Send.Now, l.Kind));
        Assert.Equal(Start.AddHours(3), letters[1].At);
    }

    // ── what is not news ─────────────────────────────────────────────────────

    [Fact]
    public void Somebody_who_books_their_own_event_is_not_told_that_somebody_asked()
    {
        var bookings = new[] { Request(Start, lead: Venue) };

        Assert.Empty(Run(bookings, Start, Start.AddHours(1)));
    }

    [Fact]
    public void A_booking_already_decided_is_not_a_request_to_write_about()
    {
        // Confirmed between arriving and the next pass — by somebody on the phone to the guest.
        // Nothing is waiting, so nothing is sent.
        var bookings = new[] { Request(Start, status: HostedEventBookingStatus.Confirmed) };

        Assert.Empty(Run(bookings, Start, Start.AddHours(1)));
    }

    [Fact]
    public void A_hold_is_as_much_news_as_a_request()
    {
        // Somebody picked seats and they are out of everybody else's reach until the venue answers.
        // That is exactly a decision waiting on somebody.
        var bookings = new[] { Request(Start, status: HostedEventBookingStatus.Held) };

        Assert.Single(Run(bookings, Start, Start.AddHours(1)));
    }

    [Fact]
    public void Somebody_new_to_the_event_is_not_posted_last_months_queue()
    {
        // A decider added today, or the feature switched on today, hears about what arrives from
        // now — the old queue is on the board and in the digest.
        var bookings = new[] { Request(Start.AddDays(-30)), Request(Start.AddDays(-2)) };

        Assert.Empty(Run(bookings, Start, Start.AddHours(1)));
    }
}
