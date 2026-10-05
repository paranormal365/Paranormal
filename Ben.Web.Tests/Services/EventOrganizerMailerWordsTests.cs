using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The sentence a venue's letter says about one booking (10/05/2026).
/// </summary>
/// <remarks>
/// An overnight request with no room chosen read "A party of 2 has asked for Waiting to be placed on
/// Fri 11/06, Sat 11/07" — the board's label for an unplaced night, dropped into a sentence. Found in a
/// letter photographed for the Hosted Events brochure.
/// </remarks>
public sealed class EventOrganizerMailerWordsTests
{
    private static HostedEventBooking Booking(params (DateTime Date, string? Room)[] nights)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(), PartySize = 2, Kind = HostedEventBookingKind.Overnight,
            Status = HostedEventBookingStatus.Requested,
        };
        foreach (var (date, room) in nights)
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(),
                HostedEventNight = new HostedEventNight { Id = Guid.NewGuid(), Date = date },
                HostedEventLayoutUnit = room is null ? null : new HostedEventLayoutUnit { Id = Guid.NewGuid(), Label = room },
            });
        return booking;
    }

    [Fact]
    public void A_request_with_no_room_yet_asks_for_a_place()
    {
        var words = EventOrganizerMailer.Describe(
            Booking((new DateTime(2026, 11, 6), null), (new DateTime(2026, 11, 7), null)), withWhere: true);

        Assert.Equal("A party of 2 has asked for a place on Fri 11/06, Sat 11/07", words);
    }

    [Fact]
    public void A_request_for_a_room_names_the_room()
    {
        var words = EventOrganizerMailer.Describe(
            Booking((new DateTime(2026, 11, 6), "The Blue Room")), withWhere: true);

        Assert.Equal("A party of 2 has asked for The Blue Room on Fri 11/06", words);
    }
}
