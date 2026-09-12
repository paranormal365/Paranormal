using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The kitchen's sheet for a hosted event: who needs something different, how many said the same
/// thing, and how many people nobody has named (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para>No database. The counting is the part worth being certain about and it is arithmetic over
/// rows already in hand, so a real database here would test Entity Framework rather than the
/// rule.</para>
///
/// <para><b>The claim under test throughout is that the sheet never overstates what is known.</b>
/// A cook who reads it must be able to tell the difference between "nobody in this party has an
/// allergy" and "nobody in this party has been named", because acting on the first when the truth
/// is the second is how somebody gets fed the wrong thing.</para>
/// </remarks>
public sealed class EventDietaryTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly DateTime Friday = new(2026, 10, 30);
    private static readonly DateTime Saturday = new(2026, 10, 31);

    private static HostedEventBooking Booking(
        string leadName, int partySize, HostedEventBookingStatus status,
        (string name, string? notes)[] guests,
        params DateTime[] nights)
    {
        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = EventId,
            LeadAppUserId = Guid.NewGuid(),
            LeadAppUser = new AppUser { Id = Guid.NewGuid(), DisplayName = leadName },
            PartySize = partySize,
            Status = status,
            Kind = nights.Length > 0
                ? HostedEventBookingKind.Overnight
                : HostedEventBookingKind.DayPass,
            DateCreated = DateTime.UtcNow,
        };

        var order = 0;
        foreach (var (name, notes) in guests)
        {
            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                DisplayName = name, DietaryNotes = notes, SortOrder = order++,
            });
        }

        foreach (var night in nights)
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(), HostedEventBookingId = booking.Id,
                HostedEventNight = new HostedEventNight { Id = Guid.NewGuid(), Date = night },
            });
        }

        return booking;
    }

    // ── who is counted ───────────────────────────────────────────────────────

    [Fact]
    public void A_party_the_venue_has_not_agreed_to_is_not_cooked_for()
    {
        var sheet = EventDietary.Summarise(EventId, [
            Booking("Confirmed Party", 2, HostedEventBookingStatus.Confirmed,
                    [("Ada", "coeliac"), ("Bertie", null)], Friday),
            Booking("Still Asking", 2, HostedEventBookingStatus.Requested,
                    [("Clara", "vegan"), ("Dev", null)], Friday),
        ], includeRequests: false);

        Assert.Equal(2, sheet.PeopleExpected);
        Assert.Equal(1, sheet.PeopleWithNotes);
        Assert.DoesNotContain(sheet.Lines, l => l.GuestName == "Clara");
    }

    [Fact]
    public void A_host_ordering_ahead_can_fold_the_requests_in_and_is_told_that_is_what_they_did()
    {
        HostedEventBooking[] bookings = [
            Booking("Confirmed Party", 2, HostedEventBookingStatus.Confirmed,
                    [("Ada", "coeliac"), ("Bertie", null)], Friday),
            Booking("Still Asking", 2, HostedEventBookingStatus.Requested,
                    [("Clara", "vegan"), ("Dev", null)], Friday),
        ];

        var settled = EventDietary.Summarise(EventId, bookings, includeRequests: false);
        var provisional = EventDietary.Summarise(EventId, bookings, includeRequests: true);

        Assert.False(settled.IncludesRequests);
        Assert.True(provisional.IncludesRequests);
        Assert.Equal(4, provisional.PeopleExpected);
        Assert.Equal(2, provisional.PeopleWithNotes);
        // And a cook can see which line is not settled yet.
        Assert.Contains(provisional.Lines,
            l => l.GuestName == "Clara" && l.Status == HostedEventBookingStatus.Requested);
    }

    [Fact]
    public void A_turned_down_party_never_appears_however_it_is_asked_for()
    {
        HostedEventBooking[] bookings = [
            Booking("Turned Down", 3, HostedEventBookingStatus.TurnedDown,
                    [("Edith", "nut allergy")], Friday),
            Booking("Cancelled", 3, HostedEventBookingStatus.Cancelled,
                    [("Frank", "nut allergy")], Friday),
        ];

        foreach (var includeRequests in new[] { false, true })
        {
            var sheet = EventDietary.Summarise(EventId, bookings, includeRequests);
            Assert.Equal(0, sheet.PeopleExpected);
            Assert.Empty(sheet.Lines);
        }
    }

    // ── what the kitchen does not know ───────────────────────────────────────

    [Fact]
    public void People_in_a_party_that_nobody_named_are_counted_and_said_so()
    {
        // A party of four who listed two names. The kitchen is cooking for two strangers.
        var sheet = EventDietary.Summarise(EventId, [
            Booking("A Family", 4, HostedEventBookingStatus.Confirmed,
                    [("Ada", "coeliac"), ("Bertie", null)], Friday),
        ], includeRequests: false);

        Assert.Equal(4, sheet.PeopleExpected);
        Assert.Equal(1, sheet.PeopleWithNotes);
        Assert.Equal(2, sheet.PeopleUnnamed);
    }

    [Fact]
    public void Naming_more_people_than_the_party_holds_is_a_typo_and_never_a_negative()
    {
        var sheet = EventDietary.Summarise(EventId, [
            Booking("Miscounted", 1, HostedEventBookingStatus.Confirmed,
                    [("Ada", null), ("Bertie", null), ("Clara", null)], Friday),
        ], includeRequests: false);

        Assert.Equal(0, sheet.PeopleUnnamed);
    }

    [Fact]
    public void A_blank_note_is_not_a_requirement()
    {
        // Whitespace in a text box is somebody tabbing through a form, not a dietary need.
        var sheet = EventDietary.Summarise(EventId, [
            Booking("A Party", 2, HostedEventBookingStatus.Confirmed,
                    [("Ada", "   "), ("Bertie", "")], Friday),
        ], includeRequests: false);

        Assert.Equal(0, sheet.PeopleWithNotes);
        Assert.Empty(sheet.Lines);
    }

    // ── the tally ────────────────────────────────────────────────────────────

    [Fact]
    public void Two_people_who_typed_the_same_words_count_as_two()
    {
        var sheet = EventDietary.Summarise(EventId, [
            Booking("One Party", 2, HostedEventBookingStatus.Confirmed,
                    [("Ada", "Vegan"), ("Bertie", "vegan")], Friday),
            Booking("Another", 2, HostedEventBookingStatus.Confirmed,
                    [("Clara", "  vegan  "), ("Dev", "coeliac")], Friday),
        ], includeRequests: false);

        var vegan = Assert.Single(sheet.Tally, t => t.Notes.Trim().Equals(
            "vegan", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, vegan.People);
        // Ordered by how many, so the biggest job is the first line a cook reads.
        Assert.Equal(3, sheet.Tally[0].People);
    }

    [Fact]
    public void Two_ways_of_saying_one_thing_are_never_merged()
    {
        // "No nuts" and "nut allergy" are the same requirement to a cook and two strings here.
        // Guessing they are one thing would eventually merge two that are not, and a cook acting
        // on a wrong merge poisons somebody.
        var sheet = EventDietary.Summarise(EventId, [
            Booking("A Party", 2, HostedEventBookingStatus.Confirmed,
                    [("Ada", "no nuts"), ("Bertie", "nut allergy")], Friday),
        ], includeRequests: false);

        Assert.Equal(2, sheet.Tally.Count);
        Assert.All(sheet.Tally, t => Assert.Equal(1, t.People));
    }

    // ── which nights ─────────────────────────────────────────────────────────

    [Fact]
    public void A_line_carries_the_nights_that_party_is_here_so_saturdays_cook_knows()
    {
        var sheet = EventDietary.Summarise(EventId, [
            Booking("Weekenders", 1, HostedEventBookingStatus.Confirmed,
                    [("Ada", "coeliac")], Saturday, Friday),
            Booking("Day Trippers", 1, HostedEventBookingStatus.Confirmed,
                    [("Bertie", "vegan")]),
        ], includeRequests: false);

        var weekend = Assert.Single(sheet.Lines, l => l.GuestName == "Ada");
        Assert.Equal([Friday, Saturday], weekend.Nights);

        // A day pass sleeps nowhere, and an empty list says that without inventing a night.
        var dayTrip = Assert.Single(sheet.Lines, l => l.GuestName == "Bertie");
        Assert.Empty(dayTrip.Nights);
    }

    [Fact]
    public void An_event_nobody_has_booked_is_an_empty_sheet_rather_than_no_answer()
    {
        var sheet = EventDietary.Summarise(EventId, [], includeRequests: true);

        Assert.Equal(0, sheet.PeopleExpected);
        Assert.Empty(sheet.Tally);
        Assert.Empty(sheet.Lines);
    }
}
