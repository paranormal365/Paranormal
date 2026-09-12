using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// How full a hosted event is, per room per night and in day passes (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para>The failure this exists to prevent is two parties in one bed, and every rule below is a
/// way of getting there: counting bookings instead of people, counting a request as if it held
/// something, counting a whole event instead of one night, or refusing a party by the beds it is
/// about to vacate.</para>
///
/// <para>The other half is the refusal. A host told "full" and nothing else cannot tell whether to
/// offer a smaller room, split the party or say no, so the sentence names the room, the date and
/// the number of places left.</para>
/// </remarks>
public sealed class EventCapacityTests
{
    private static readonly Guid FridayId = Guid.NewGuid();
    private static readonly Guid SaturdayId = Guid.NewGuid();
    private static readonly Guid BlueRoomId = Guid.NewGuid();
    private static readonly Guid SuiteId = Guid.NewGuid();
    private static readonly DateTime Friday = new(2026, 10, 30);

    private static HostedEventBooking Booking(
        HostedEventBookingStatus status,
        int partySize,
        HostedEventBookingKind kind = HostedEventBookingKind.Overnight,
        params (Guid nightId, Guid roomId)[] nights)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            Status = status,
            PartySize = partySize,
            Kind = kind,
        };
        foreach (var (nightId, roomId) in nights)
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                HostedEventNightId = nightId,
                HostedEventLayoutUnitId = roomId,
            });
        }
        return booking;
    }

    // ── Counting people, not rows ────────────────────────────────────────────

    [Fact]
    public void A_room_counts_people_not_bookings()
    {
        // The mistake this whole class exists to catch: two bookings of two are four people, and a
        // room that sleeps two is full after the first of them.
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Confirmed, 2, nights: (FridayId, BlueRoomId)),
            Booking(HostedEventBookingStatus.Confirmed, 2, nights: (FridayId, BlueRoomId)),
        };

        Assert.Equal(4, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
    }

    [Fact]
    public void A_party_size_of_zero_still_counts_as_one_person()
    {
        // A row that arrives as 0 — an older snapshot, a bad import — must not count as nobody,
        // because a booking that occupies no beds is how a room quietly takes one party too many.
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Confirmed, 0, nights: (FridayId, BlueRoomId)),
        };

        Assert.Equal(1, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
    }

    // ── Only a confirmed booking holds anything (DECISION 7) ─────────────────

    [Theory]
    [InlineData(HostedEventBookingStatus.Requested)]
    [InlineData(HostedEventBookingStatus.TurnedDown)]
    [InlineData(HostedEventBookingStatus.Cancelled)]
    public void Only_a_confirmed_booking_holds_a_bed(HostedEventBookingStatus status)
    {
        // A queue of hopefuls must not fill a house, or the venue could never work through it.
        var bookings = new[] { Booking(status, 4, nights: (FridayId, BlueRoomId)) };

        Assert.Equal(0, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
        Assert.False(EventCapacity.Holds(status));
    }

    [Fact]
    public void A_confirmed_booking_holds_one()
    {
        Assert.True(EventCapacity.Holds(HostedEventBookingStatus.Confirmed));
    }

    // ── Per night, and per room ──────────────────────────────────────────────

    [Fact]
    public void A_room_full_on_friday_can_be_empty_on_saturday()
    {
        // The reason nights are rows rather than a range. Counting the event instead of the night
        // would refuse a Saturday booking because somebody slept there on Friday.
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Confirmed, 2, nights: (FridayId, BlueRoomId)),
        };

        Assert.Equal(2, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
        Assert.Equal(0, EventCapacity.PeopleIn(bookings, SaturdayId, BlueRoomId));
    }

    [Fact]
    public void People_in_one_room_do_not_fill_another()
    {
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Confirmed, 2, nights: (FridayId, BlueRoomId)),
        };

        Assert.Equal(0, EventCapacity.PeopleIn(bookings, FridayId, SuiteId));
    }

    [Fact]
    public void A_party_across_two_nights_is_counted_on_both()
    {
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Confirmed, 3,
                    nights: [(FridayId, BlueRoomId), (SaturdayId, BlueRoomId)]),
        };

        Assert.Equal(3, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
        Assert.Equal(3, EventCapacity.PeopleIn(bookings, SaturdayId, BlueRoomId));
    }

    // ── Editing a booking must not be refused by its own beds ────────────────

    [Fact]
    public void A_party_moving_rooms_is_not_refused_by_the_beds_it_is_leaving()
    {
        // Ben asked for editable reservations. Without the exclusion, moving a party of two from
        // the Blue Room to the Suite and back would be refused by itself.
        var moving = Booking(HostedEventBookingStatus.Confirmed, 2, nights: (FridayId, BlueRoomId));
        var bookings = new[] { moving };

        Assert.Equal(2, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
        Assert.Equal(0, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId,
                                               excludingBookingId: moving.Id));
    }

    // ── What a room sleeps ───────────────────────────────────────────────────

    [Fact]
    public void An_event_may_state_its_own_capacity_for_a_room()
    {
        // A hotel putting four camp beds in a double for a séance weekend. The override wins, and
        // it keeps winning if the venue later re-describes the room.
        var offered = new HostedEventLayoutUnit
        {
            Capacity = 4,
            PlaceRoom = new PlaceRoom { Name = "Blue Room", Capacity = 2 },
        };

        Assert.Equal(4, EventCapacity.CapacityOf(offered));
    }

    [Fact]
    public void Without_an_override_the_rooms_own_capacity_is_used()
    {
        var offered = new HostedEventLayoutUnit
        {
            PlaceRoom = new PlaceRoom { Name = "Blue Room", Capacity = 2 },
        };

        Assert.Equal(2, EventCapacity.CapacityOf(offered));
    }

    [Fact]
    public void A_room_nobody_has_measured_cannot_be_over_filled()
    {
        // Null is "the venue has not said". Refusing bookings against it would make a venue's own
        // rooms unbookable until somebody filled in a form.
        var offered = new HostedEventLayoutUnit { PlaceRoom = new PlaceRoom { Name = "Attic" } };

        Assert.Null(EventCapacity.CapacityOf(offered));
        Assert.Null(EventCapacity.BedsLeft(null, taken: 99));
        Assert.Null(EventCapacity.WhyThisRoomCannotTakeThem(
            "Attic", Friday, capacity: null, taken: 99, partySize: 6));
    }

    [Fact]
    public void A_room_that_states_zero_sleeps_nobody()
    {
        // Zero is a statement, not a missing value: nobody sleeps in the chapel.
        Assert.Equal(0, EventCapacity.BedsLeft(0, taken: 0));
        Assert.NotNull(EventCapacity.WhyThisRoomCannotTakeThem(
            "Chapel", Friday, capacity: 0, taken: 0, partySize: 1));
    }

    // ── The refusals say enough to act on ────────────────────────────────────

    [Fact]
    public void A_party_that_fits_is_not_refused()
    {
        Assert.Null(EventCapacity.WhyThisRoomCannotTakeThem(
            "Blue Room", Friday, capacity: 4, taken: 1, partySize: 3));
    }

    [Fact]
    public void A_room_with_room_left_names_the_number_and_the_date()
    {
        var why = EventCapacity.WhyThisRoomCannotTakeThem(
            "Blue Room", Friday, capacity: 4, taken: 2, partySize: 3);

        Assert.NotNull(why);
        Assert.Contains("Blue Room", why);
        Assert.Contains("sleeps 2 more", why);      // not "full" — a party of two would fit
        Assert.Contains("10/30/2026", why);          // US format sitewide
        Assert.Contains("party of 3", why);
    }

    [Fact]
    public void A_full_room_says_so_and_says_what_to_do()
    {
        var why = EventCapacity.WhyThisRoomCannotTakeThem(
            "Blue Room", Friday, capacity: 2, taken: 2, partySize: 1);

        Assert.NotNull(why);
        Assert.Contains("full", why);
        Assert.Contains("10/30/2026", why);
    }

    [Fact]
    public void One_bed_left_is_said_in_the_singular()
    {
        var why = EventCapacity.WhyThisRoomCannotTakeThem(
            "Blue Room", Friday, capacity: 3, taken: 2, partySize: 2);

        Assert.Contains("sleeps 1 more", why);
    }

    // ── Day passes ───────────────────────────────────────────────────────────

    [Fact]
    public void Day_passes_are_counted_apart_from_beds()
    {
        // Someone staying the night is not holding a day pass, and vice versa. One number for both
        // would let a venue that sleeps eight sell forty.
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Confirmed, 2, nights: (FridayId, BlueRoomId)),
            Booking(HostedEventBookingStatus.Confirmed, 3, HostedEventBookingKind.DayPass),
        };

        Assert.Equal(3, EventCapacity.DayPassesTaken(bookings));
        Assert.Equal(2, EventCapacity.PeopleIn(bookings, FridayId, BlueRoomId));
    }

    [Fact]
    public void A_requested_day_pass_holds_nothing_either()
    {
        var bookings = new[]
        {
            Booking(HostedEventBookingStatus.Requested, 5, HostedEventBookingKind.DayPass),
        };

        Assert.Equal(0, EventCapacity.DayPassesTaken(bookings));
    }

    [Fact]
    public void An_event_selling_no_day_passes_says_that_rather_than_full()
    {
        // Zero and "sold out" send a host to completely different places: one is a setting to
        // change, the other is a booking to turn down.
        var why = EventCapacity.WhyTheseDayPassesCannotBeGiven(capacity: 0, taken: 0, partySize: 1);

        Assert.NotNull(why);
        Assert.Contains("isn't selling day passes", why);
    }

    [Fact]
    public void An_event_with_no_day_pass_limit_refuses_nobody()
    {
        Assert.Null(EventCapacity.WhyTheseDayPassesCannotBeGiven(
            capacity: null, taken: 500, partySize: 4));
    }

    [Fact]
    public void Day_passes_running_out_names_how_many_are_left()
    {
        var why = EventCapacity.WhyTheseDayPassesCannotBeGiven(capacity: 10, taken: 8, partySize: 4);

        Assert.Contains("Only 2 day passes left", why);
        Assert.Contains("party of 4", why);
    }

    // ── Party size ───────────────────────────────────────────────────────────

    [Fact]
    public void A_missing_party_size_means_one()
    {
        Assert.Equal(1, EventCapacity.ClampPartySize(null));
        Assert.Equal(1, EventCapacity.ClampPartySize(0));
        Assert.Equal(1, EventCapacity.ClampPartySize(-3));
    }

    [Fact]
    public void A_party_size_has_a_ceiling()
    {
        Assert.Equal(EventCapacity.MaxPartySize,
                     EventCapacity.ClampPartySize(100_000));
    }

    // ── The deadline ─────────────────────────────────────────────────────────

    [Fact]
    public void An_event_with_no_deadline_is_always_open()
    {
        var ev = new HostedEvent();

        Assert.True(EventCapacity.IsOpenForRequests(ev, DateTime.UtcNow.AddYears(5)));
    }

    [Fact]
    public void A_deadline_closes_new_requests_when_it_passes()
    {
        var closes = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var ev = new HostedEvent { BookingsCloseAtUtc = closes };

        Assert.True(EventCapacity.IsOpenForRequests(ev, closes.AddMinutes(-1)));
        Assert.False(EventCapacity.IsOpenForRequests(ev, closes.AddMinutes(1)));
    }
}
