using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Tours;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// How many places a date has taken, and what the business may approve (item 234).
/// </summary>
/// <remarks>
/// <para>The counting rule lived in four separate places before this, each of them counting ROWS.
/// These pin the two properties that make one shared rule safe to move to: a sign-up may hold more
/// than one place, and <b>every row written before this holds exactly one</b>, so the sum equals
/// the old count and nothing about an ordinary event moves.</para>
///
/// <para>The other property worth pinning is the refusal. A business told "full" and nothing else
/// cannot tell whether to approve a smaller party or none at all, so the sentence names the number
/// of places left.</para>
/// </remarks>
public sealed class TourSeatsTests
{
    private static OrgCalendarEventAttendee Seat(
        RsvpStatus rsvp, int seats = 1, Guid? appUserId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            OrgCalendarEventId = Guid.Empty,
            AppUserId = appUserId ?? Guid.NewGuid(),
            RsvpStatus = rsvp,
            Seats = seats,
        };

    [Fact]
    public void A_row_written_before_seats_existed_still_holds_one_place()
    {
        // Seats defaults to 1 in the entity and in the column, but a row read from an older
        // snapshot could arrive as 0 — and counting it as nothing would quietly overbook a walk.
        var attendees = new[]
        {
            Seat(RsvpStatus.Accepted, seats: 0),
            Seat(RsvpStatus.Accepted, seats: 1),
        };

        Assert.Equal(2, TourSeats.PlacesTaken(attendees));
    }

    [Fact]
    public void Places_are_counted_not_people()
    {
        var attendees = new[]
        {
            Seat(RsvpStatus.Accepted, seats: 4),
            Seat(RsvpStatus.Accepted, seats: 2),
        };

        Assert.Equal(6, TourSeats.PlacesTaken(attendees));
    }

    [Fact]
    public void A_request_holds_nothing_until_it_is_approved()
    {
        // The whole reason a request sits at Invited rather than Accepted: a queue of hopefuls
        // must not be able to fill a walk that nobody has been given a place on.
        var attendees = new[]
        {
            Seat(RsvpStatus.Invited, seats: 6),   // waiting on the business
            Seat(RsvpStatus.Accepted, seats: 2),  // approved
            Seat(RsvpStatus.Declined, seats: 3),  // turned down
        };

        Assert.Equal(2, TourSeats.PlacesTaken(attendees));
    }

    [Fact]
    public void Somebody_is_never_counted_against_their_own_seat()
    {
        // Re-signing-up after cancelling must not be refused by the place they are not occupying.
        var sarah = Guid.NewGuid();
        var attendees = new[]
        {
            Seat(RsvpStatus.Accepted, seats: 3, appUserId: sarah),
            Seat(RsvpStatus.Accepted, seats: 1),
        };

        Assert.Equal(1, TourSeats.PlacesTaken(attendees, excludingAppUserId: sarah));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(3, 3)]
    [InlineData(999, TourSeats.MaxSeatsPerRequest)]
    public void A_party_size_is_clamped_rather_than_argued_with(int? asked, int expected)
        => Assert.Equal(expected, TourSeats.Clamp(asked));

    [Fact]
    public void A_date_with_no_capacity_can_always_be_approved()
    {
        Assert.Null(TourSeats.PlacesLeft(capacity: null, taken: 40));
        Assert.Null(TourSeats.WhyTheseSeatsCannotBeApproved(capacity: null, taken: 40, wanted: 9));
    }

    [Fact]
    public void A_refusal_names_how_many_places_are_left()
    {
        // "This event is full" tells a business nothing it can act on. This tells them to approve
        // a party of two, or to go back to the guest.
        var refusal = TourSeats.WhyTheseSeatsCannotBeApproved(capacity: 10, taken: 8, wanted: 4);

        Assert.NotNull(refusal);
        Assert.Contains("2 places left", refusal);
        Assert.Contains("request for 4", refusal);
    }

    [Fact]
    public void One_place_left_is_said_in_the_singular()
    {
        var refusal = TourSeats.WhyTheseSeatsCannotBeApproved(capacity: 10, taken: 9, wanted: 2);
        Assert.Contains("1 place left", refusal);
    }

    [Fact]
    public void A_full_date_says_what_to_do_about_it()
    {
        var refusal = TourSeats.WhyTheseSeatsCannotBeApproved(capacity: 10, taken: 10, wanted: 1);

        Assert.NotNull(refusal);
        Assert.Contains("full", refusal);
        Assert.Contains("Turn a reserved seat down", refusal);
    }

    [Fact]
    public void Exactly_filling_the_date_is_allowed()
    {
        // The off-by-one that would refuse the last party on a walk they exactly fit.
        Assert.Null(TourSeats.WhyTheseSeatsCannotBeApproved(capacity: 10, taken: 6, wanted: 4));
    }

    [Fact]
    public void Only_a_date_that_names_a_tour_plays_by_these_rules()
    {
        Assert.False(TourSeats.IsTourDate(new OrgCalendarEvent { Title = "Open evening" }));
        Assert.True(TourSeats.IsTourDate(new OrgCalendarEvent { Title = "Saturday walk", TourId = Guid.NewGuid() }));
    }
}
