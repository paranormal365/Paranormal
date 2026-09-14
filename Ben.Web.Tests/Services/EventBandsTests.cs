using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which colour a party wears (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"blue could be the full event with food, purple could be the full
/// event, red is day one … lets their employees know by glance what a person is registered
/// for."</i></para>
///
/// <para><b>The claim under test is that the derivation never guesses.</b> A band nobody assigned
/// is worse than no bands at all — a steward glancing at a wrist and seeing the wrong colour has
/// been told something false by the screen, and will act on it. So: a hand-picked band always
/// wins, a by-hand band is never derived onto anybody, and where nothing fits, nobody wears
/// anything.</para>
///
/// <para>No database. It is a rule over rows already in hand, and the point of it being pure is
/// that three screens — the door, the board and the guest's pass — cannot disagree.</para>
/// </remarks>
public sealed class EventBandsTests
{
    private static readonly Guid EventId = Guid.NewGuid();

    private static HostedEventBand Band(
        string colour, HostedEventBandRule rule, int order) => new()
        {
            Id = Guid.NewGuid(),
            HostedEventId = EventId,
            Colour = colour,
            Meaning = colour,
            Rule = rule,
            SortOrder = order,
        };

    /// <summary>What a venue running a two-night weekend typically sets up.</summary>
    private static (HostedEventBand Blue, HostedEventBand Red, HostedEventBand Green,
                    HostedEventBand Gold, List<HostedEventBand> All) TheUsualSet()
    {
        var blue = Band("Blue", HostedEventBandRule.EveryNight, 0);
        var red = Band("Red", HostedEventBandRule.SomeNights, 1);
        var green = Band("Green", HostedEventBandRule.DayPass, 2);
        var gold = Band("Gold", HostedEventBandRule.ByHand, 3);

        return (blue, red, green, gold, [blue, red, green, gold]);
    }

    private static HostedEventBooking Booking(
        HostedEventBookingKind kind, int nightsHeld, int nightsReleased = 0,
        Guid? byHand = null)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = EventId,
            Kind = kind,
            HostedEventBandId = byHand,
        };

        for (var i = 0; i < nightsHeld; i++)
            booking.Nights.Add(new HostedEventBookingNight { Id = Guid.NewGuid() });

        for (var i = 0; i < nightsReleased; i++)
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), ReleasedUtc = DateTime.UtcNow,
            });

        return booking;
    }

    // ── what is worked out ───────────────────────────────────────────────────

    [Fact]
    public void Somebody_here_the_whole_run_wears_the_whole_run_colour()
    {
        var (blue, _, _, _, all) = TheUsualSet();

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 2), all, nightsOnTheEvent: 2);

        Assert.Equal(blue.Id, band?.Id);
    }

    [Fact]
    public void The_saturday_only_guest_is_the_case_a_colour_exists_for()
    {
        // The one a steward gets wrong: somebody who looks exactly like a weekender at the door on
        // Saturday and has no bed on the Friday.
        var (_, red, _, _, all) = TheUsualSet();

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 1), all, nightsOnTheEvent: 2);

        Assert.Equal(red.Id, band?.Id);
    }

    [Fact]
    public void A_day_pass_is_not_staying_however_many_nights_the_event_runs()
    {
        var (_, _, green, _, all) = TheUsualSet();

        var band = EventBands.For(
            Booking(HostedEventBookingKind.DayPass, nightsHeld: 0), all, nightsOnTheEvent: 2);

        Assert.Equal(green.Id, band?.Id);
    }

    [Fact]
    public void A_night_given_back_is_not_a_night_they_are_here_for()
    {
        // A party who released the Friday is a Saturday-only party now, and the colour has to
        // follow — a wristband handed out on last week's booking is a wristband that lets somebody
        // into a night they gave up.
        var (_, red, _, _, all) = TheUsualSet();

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 1, nightsReleased: 1),
            all, nightsOnTheEvent: 2);

        Assert.Equal(red.Id, band?.Id);
    }

    // ── what is never worked out ─────────────────────────────────────────────

    [Fact]
    public void A_by_hand_band_is_never_put_on_anybody_by_the_rules()
    {
        // The whole reason by-hand exists: "with food" cannot be derived, because nothing on a
        // booking says a party is eating. A guess here would hand somebody a dinner.
        var gold = Band("Gold", HostedEventBandRule.ByHand, 0);

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 2), [gold], nightsOnTheEvent: 2);

        Assert.Null(band);
    }

    [Fact]
    public void A_band_given_by_hand_beats_every_rule()
    {
        // It is the one thing the venue said out loud about this party; the rules are a
        // convenience for everybody they did not.
        var (blue, _, _, gold, all) = TheUsualSet();

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 2, byHand: gold.Id),
            all, nightsOnTheEvent: 2);

        Assert.Equal(gold.Id, band?.Id);
        Assert.NotEqual(blue.Id, band?.Id);
    }

    [Fact]
    public void The_venues_own_order_decides_which_rule_wins()
    {
        // A party here every night matches "every night" and would also match nothing else — but
        // a venue that puts "some nights" first has said something, and the code must not overrule
        // it. Reversed, the same party wears the other colour.
        var some = Band("Red", HostedEventBandRule.SomeNights, 0);
        var every = Band("Blue", HostedEventBandRule.EveryNight, 1);

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 1),
            [some, every], nightsOnTheEvent: 2);

        Assert.Equal(some.Id, band?.Id);
    }

    [Fact]
    public void An_event_with_no_bands_puts_nobody_in_one()
    {
        Assert.Null(EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 2), [], nightsOnTheEvent: 2));
    }

    [Fact]
    public void A_hand_picked_band_that_has_been_deleted_leaves_them_wearing_nothing()
    {
        // The venue stopped using that colour. Falling back to the rules would be defensible; this
        // does not, because the id was the venue's explicit answer and its absence is a deletion
        // rather than a change of mind about this party. Either way it must not throw.
        var (_, _, _, _, all) = TheUsualSet();

        var band = EventBands.For(
            Booking(HostedEventBookingKind.Overnight, nightsHeld: 2, byHand: Guid.NewGuid()),
            all, nightsOnTheEvent: 2);

        Assert.Null(band);
    }
}
