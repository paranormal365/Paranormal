using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Tours;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// The pass a guide scans at the meeting point (item 247).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-20: <i>"qr codes for ghost tour tickets for the staff to scan when they
/// arrive for the tour."</i></para>
///
/// <para>Two things carry weight, and neither is the QR code. <b>A pass belongs to a seat somebody
/// actually holds</b> — a pass against a request is a ticket to a walk nobody agreed to give them,
/// and they would turn up holding it. And <b>every refusal is a sentence</b>, because the reader is
/// a guide on a dark street with a queue behind them: "not recognised" tells them to check the
/// email; a status code tells them nothing.</para>
/// </remarks>
public sealed class TourPassTests
{
    private static OrgCalendarEventAttendee Seat(
        TourSeatStatus? status = TourSeatStatus.Reserved,
        RsvpStatus rsvp = RsvpStatus.Accepted,
        Guid? eventId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OrgCalendarEventId = eventId ?? Guid.NewGuid(),
            AppUserId = Guid.NewGuid(),
            SeatStatus = status,
            RsvpStatus = rsvp,
            Seats = 1,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        };

    // ── who may hold one ─────────────────────────────────────────────────────

    [Fact]
    public void A_reserved_seat_may_have_a_pass()
        => Assert.True(TourPasses.MayHaveAPass(Seat()));

    /// <summary>
    /// A seat only asked for is not a ticket.
    /// </summary>
    /// <remarks>
    /// The load-bearing one. A pass minted against a request would admit somebody the business
    /// never agreed to take, and they would arrive holding proof of it.
    /// </remarks>
    [Fact]
    public void A_requested_seat_may_not()
        => Assert.False(TourPasses.MayHaveAPass(Seat(TourSeatStatus.Requested, RsvpStatus.Invited)));

    [Fact]
    public void A_turned_down_seat_may_not()
        => Assert.False(TourPasses.MayHaveAPass(Seat(TourSeatStatus.TurnedDown, RsvpStatus.Declined)));

    /// <summary>An ordinary calendar attendee is answered by their own acceptance.</summary>
    [Fact]
    public void An_attendee_with_no_tour_status_is_judged_on_their_rsvp()
    {
        Assert.True(TourPasses.MayHaveAPass(Seat(status: null, rsvp: RsvpStatus.Accepted)));
        Assert.False(TourPasses.MayHaveAPass(Seat(status: null, rsvp: RsvpStatus.Invited)));
    }

    // ── minting ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Two passes for one seat is two codes at a meeting point, one of them wrong.
    /// </summary>
    [Fact]
    public void Minting_twice_keeps_the_first_pass()
    {
        var seat = Seat();

        var first = TourPasses.Ensure(seat);
        var again = TourPasses.Ensure(seat);

        Assert.Equal(first, again);
        Assert.NotNull(seat.PassIssuedUtc);
    }

    [Fact]
    public void A_pass_is_long_enough_that_guessing_is_not_a_way_in()
    {
        var token = TourPasses.Ensure(Seat());

        Assert.True(token.Length >= 32, $"a {token.Length}-character pass is guessable");
        Assert.DoesNotContain(" ", token);
    }

    /// <summary>Reissuing replaces the code, which is also how one is taken back.</summary>
    [Fact]
    public void Reissuing_replaces_the_code_and_forgets_the_scan()
    {
        var seat = Seat();
        var first = TourPasses.Ensure(seat);
        TourPasses.CheckIn(seat, Guid.NewGuid());

        var second = TourPasses.Reissue(seat);

        Assert.NotEqual(first, second);
        // The new pass has not been used, whatever the old one did.
        Assert.Null(seat.CheckedInUtc);
    }

    // ── the door ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_unknown_code_says_what_to_do_next()
    {
        var why = TourPasses.WhyThisScanIsRefused(null, Guid.NewGuid(), null);

        Assert.NotNull(why);
        Assert.Contains("look them up by name", why!);
    }

    /// <summary>
    /// The commonest honest mistake at a meeting point, named as itself.
    /// </summary>
    /// <remarks>
    /// Telling somebody with a valid pass for next Friday that their code is "not recognised"
    /// sends them away believing they were never booked.
    /// </remarks>
    [Fact]
    public void A_pass_for_another_walk_says_which_walk()
    {
        var why = TourPasses.WhyThisScanIsRefused(Seat(), Guid.NewGuid(), "Friday Night Ghost Walk");

        Assert.NotNull(why);
        Assert.Contains("Friday Night Ghost Walk", why!);
    }

    [Fact]
    public void A_seat_that_is_not_confirmed_is_refused_at_the_door()
    {
        var eventId = Guid.NewGuid();
        var seat = Seat(TourSeatStatus.Requested, RsvpStatus.Invited, eventId);

        Assert.NotNull(TourPasses.WhyThisScanIsRefused(seat, eventId, null));
    }

    [Fact]
    public void A_good_pass_for_tonight_is_let_in()
    {
        var eventId = Guid.NewGuid();

        Assert.Null(TourPasses.WhyThisScanIsRefused(Seat(eventId: eventId), eventId, null));
    }

    /// <summary>
    /// A second scan is answered, not swallowed.
    /// </summary>
    /// <remarks>
    /// A guide needs "already here, at 7.42" out loud so they can tell a queue-jumper from
    /// somebody whose friend scanned their code a minute ago.
    /// </remarks>
    [Fact]
    public void Scanning_twice_says_they_are_already_in()
    {
        var seat = Seat();
        var guide = Guid.NewGuid();

        var (firstAlready, firstAt) = TourPasses.CheckIn(seat, guide);
        var (secondAlready, secondAt) = TourPasses.CheckIn(seat, Guid.NewGuid());

        Assert.False(firstAlready);
        Assert.True(secondAlready);
        // The FIRST arrival is the one kept — the second scan must not rewrite when they got here.
        Assert.Equal(firstAt, secondAt);
        Assert.Equal(guide, seat.CheckedInByAppUserId);
    }
}
