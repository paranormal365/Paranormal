using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The words the staff room posts, and what it refuses to post (item 238C).
/// </summary>
/// <remarks>
/// Composing HTML by hand is where a guest's name slips into something it should not be in, and
/// where a venue called <c>&lt;script&gt;</c> stops being funny — so the composition is pure and
/// these read the actual string.
/// </remarks>
public sealed class EventStaffRoomTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private static HostedEvent Event(string name = "Halloween Lock-In") => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Name = name,
        TimeZoneId = "America/Chicago",
    };

    private static HostedEventBooking Booking(int party = 2, string? guest = null)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            PartySize = party,
            Status = HostedEventBookingStatus.Requested,
            Kind = HostedEventBookingKind.DayPass,
            DateCreated = Now,
            Nights = [],
        };

        // Hung where a real one hangs, and on the lead account too, so the assertion below is about
        // the composer not reaching for a name that IS within its reach — not about a name that
        // happened not to be loaded.
        if (guest is not null)
        {
            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id, DisplayName = guest,
            });
            booking.LeadAppUser = new AppUser { Id = Guid.NewGuid(), DisplayName = guest };
        }

        return booking;
    }

    [Fact]
    public void Nothing_to_say_posts_nothing()
        => Assert.Null(EventStaffRoom.Arrivals(Event(), [], summary: false, "https://x.test/board"));

    [Fact]
    public void One_booking_reads_as_a_sentence_not_a_list()
    {
        var html = EventStaffRoom.Arrivals(Event(), [Booking(4)], false, "https://x.test/board");

        Assert.NotNull(html);
        Assert.Contains("A party of 4", html);
        Assert.DoesNotContain("<ul>", html);
    }

    [Fact]
    public void A_rush_is_listed_and_capped()
    {
        var many = Enumerable.Range(0, EventStaffRoom.ListedAtMost + 3)
            .Select(_ => Booking()).ToList();

        var html = EventStaffRoom.Arrivals(Event(), many, summary: true, "https://x.test/board");

        Assert.NotNull(html);
        Assert.Contains($"{many.Count} more bookings have arrived", html);
        Assert.Contains("…and 3 more.", html);
        Assert.Equal(EventStaffRoom.ListedAtMost, html!.Split("<li>").Length - 1);
    }

    /// <summary>
    /// The whole reason the letters carry no names, applied to the thread.
    /// </summary>
    /// <remarks>
    /// The thread is read by everybody the booking board would accept, which is a wider and less
    /// deliberate audience than somebody opening the board itself — it arrives unasked, on a bell.
    /// So it says what and when, never who.
    /// </remarks>
    [Fact]
    public void A_post_never_carries_the_guests_name()
    {
        var html = EventStaffRoom.Arrivals(
            Event(), [Booking(2, guest: "Marguerite Ashdown")], false, "https://x.test/board");

        Assert.NotNull(html);
        Assert.DoesNotContain("Marguerite", html);
        Assert.DoesNotContain("Ashdown", html);
    }

    [Fact]
    public void An_events_name_is_escaped_rather_than_trusted()
    {
        var ev = Event("<script>alert('x')</script> Weekend");

        var opening = EventStaffRoom.Opening(ev);

        Assert.DoesNotContain("<script>", opening);
        Assert.Contains("&lt;script&gt;", opening);
    }

    [Fact]
    public void The_thread_is_named_for_the_event_so_there_is_one_of_them()
    {
        var ev = Event("Thomas House Weekend");
        Assert.Equal("Bookings — Thomas House Weekend", EventStaffRoom.SubjectFor(ev));
    }

    /// <summary>
    /// A shared thread counts a member's own booking as news; their own inbox does not.
    /// </summary>
    [Fact]
    public void A_shared_thread_does_not_skip_the_booker()
    {
        var me = Guid.NewGuid();
        var mine = Booking();
        mine.LeadAppUserId = me;

        var forMe = EventBookingAlerts.Decide(null, null, [mine], excludeLeadAppUserId: me, Now);
        var forTheRoom = EventBookingAlerts.Decide(null, null, [mine], excludeLeadAppUserId: null, Now);

        Assert.Equal(EventBookingAlerts.Send.Nothing, forMe.Send);
        Assert.Equal(EventBookingAlerts.Send.Now, forTheRoom.Send);
    }
}
