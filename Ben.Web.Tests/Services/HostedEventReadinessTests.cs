using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What stands between a draft and going live, as the page and the button both read it
/// (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>One list read twice is the property under test.</b> A rule the server enforces and the
/// screen cannot see is worse than no rule: the person meets it as a refusal after pressing the
/// button, with no idea which of six things was wrong. So the checklist and the refusal come from
/// the same call, and these tests assert the sentences as well as the verdicts — a checklist item
/// that says one thing and a refusal that says another reads as two separate problems.</para>
/// </remarks>
public sealed class HostedEventReadinessTests
{
    private static HostedEvent Ready()
    {
        var hosted = new HostedEvent
        {
            Id = Guid.NewGuid(),
            Name = "Thomas House Weekend",
            UrlName = "thomas-house-weekend",
            StartsOn = new DateTime(2026, 10, 30),
            EndsOn = new DateTime(2026, 10, 31),
            ContactLine = "Call the hotel on (615) 555-0142 to settle up.",
            VenueArrangement = HostedEventVenueArrangement.Self,
        };
        hosted.Nights.Add(new HostedEventNight { Id = Guid.NewGuid(), Date = hosted.StartsOn });
        hosted.LayoutUnits.Add(new HostedEventLayoutUnit { Id = Guid.NewGuid(), Label = "A1" });
        return hosted;
    }

    private static string FirstRefusal(HostedEvent hosted)
        => HostedEventReadiness.Describe(hosted).First(i => !i.Done).Sentence;

    [Fact]
    public void A_complete_event_has_nothing_left_to_do()
    {
        Assert.True(HostedEventReadiness.IsReady(Ready()));
        Assert.All(HostedEventReadiness.Describe(Ready()), i => Assert.True(i.Done));
    }

    [Fact]
    public void An_event_with_no_dates_is_not_something_anybody_can_come_to()
    {
        var hosted = Ready();
        hosted.Nights.Clear();

        Assert.False(HostedEventReadiness.IsReady(hosted));
        Assert.Contains("at least one night", FirstRefusal(hosted));
    }

    [Fact]
    public void A_run_of_performances_is_asked_for_dates_and_not_nights()
    {
        // The same word the rest of the site uses for this event, because a theatre running four
        // evenings does not have "nights" and being told it needs one reads as a different rule.
        var hosted = Ready();
        hosted.DatesAreSeparate = true;
        hosted.Nights.Clear();

        Assert.Contains("at least one date", FirstRefusal(hosted));
    }

    [Fact]
    public void An_event_with_nothing_to_book_and_no_day_passes_is_refused()
    {
        var hosted = Ready();
        hosted.LayoutUnits.Clear();
        hosted.DayPassCapacity = 0;

        Assert.Contains("no rooms on its plan and sells no day passes", FirstRefusal(hosted));
    }

    [Fact]
    public void A_theatre_is_told_about_seats_and_a_hotel_about_rooms()
    {
        var theatre = Ready();
        theatre.LayoutKind = HostedEventLayoutKind.Seats;
        theatre.LayoutUnits.Clear();
        theatre.DayPassCapacity = 0;

        Assert.Contains("no seats on its plan", FirstRefusal(theatre));
        Assert.Contains("Add a block of seats", FirstRefusal(theatre));
    }

    [Fact]
    public void Day_passes_alone_are_enough_to_book_against()
    {
        // An event with no plan at all is legal: a walk, a talk, a hunt where everybody just turns
        // up. Requiring a plan would have made the commonest small event impossible to publish.
        var hosted = Ready();
        hosted.LayoutUnits.Clear();
        hosted.DayPassCapacity = 40;

        Assert.True(HostedEventReadiness.IsReady(hosted));
    }

    [Fact]
    public void A_capacity_nobody_has_stated_is_not_a_capacity_of_zero()
    {
        // Null is "we have not limited them" and zero is "we do not sell them". Reading the first
        // as the second would refuse to publish every event that never filled the box in.
        var hosted = Ready();
        hosted.LayoutUnits.Clear();
        hosted.DayPassCapacity = null;

        Assert.True(HostedEventReadiness.IsReady(hosted));
    }

    [Fact]
    public void An_event_with_no_way_to_reach_the_venue_is_refused_and_says_why()
    {
        // The site never takes a guest's money, so the contact line is the only thing on the page
        // that says how anybody pays. Publishing without it is publishing a dead end.
        var hosted = Ready();
        hosted.ContactLine = "   ";

        Assert.Contains("never takes a guest's money", FirstRefusal(hosted));
    }

    // ── the venue, which is the one with three answers ───────────────────────

    [Fact]
    public void Somebody_using_their_own_venue_is_asked_nothing_about_it()
    {
        var hosted = Ready();
        hosted.VenueArrangement = HostedEventVenueArrangement.Self;

        Assert.True(HostedEventReadiness.IsReady(hosted));
    }

    [Fact]
    public void An_arrangement_made_off_the_site_needs_a_name_and_a_date()
    {
        var hosted = Ready();
        hosted.VenueArrangement = HostedEventVenueArrangement.External;

        Assert.Contains("who at the venue agreed", FirstRefusal(hosted));

        hosted.VenueContactName = "Mrs Cole";
        Assert.False(HostedEventReadiness.IsReady(hosted));   // still no date

        hosted.VenueAgreedOnUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(HostedEventReadiness.IsReady(hosted));
    }

    [Fact]
    public void Asking_a_venue_on_this_site_is_refused_in_words_until_it_is_built()
    {
        // Offered and refused, rather than hidden. Somebody whose venue is another group on here
        // should be told that the answer is "arrange it directly for now", not left wondering why
        // the option they need is missing.
        var hosted = Ready();
        hosted.VenueArrangement = HostedEventVenueArrangement.PlatformGrant;

        Assert.False(HostedEventReadiness.IsReady(hosted));
        Assert.Contains("not built yet", FirstRefusal(hosted));
        Assert.Contains("arrange it with them directly", FirstRefusal(hosted));
    }

    // ── the shape the page relies on ─────────────────────────────────────────

    [Fact]
    public void Every_item_says_where_to_go_and_fix_it()
    {
        // A checklist that says what is wrong and not where is a checklist somebody has to search
        // the page for. Each item carries an anchor or a path, and the website makes it a link.
        Assert.All(HostedEventReadiness.Describe(Ready()), item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Label));
            Assert.False(string.IsNullOrWhiteSpace(item.Href));
        });
    }

    [Fact]
    public void Every_unfinished_item_carries_the_sentence_the_button_refuses_with()
    {
        var hosted = Ready();
        hosted.Nights.Clear();
        hosted.ContactLine = null;
        hosted.LayoutUnits.Clear();
        hosted.DayPassCapacity = 0;
        hosted.VenueArrangement = HostedEventVenueArrangement.External;

        var undone = HostedEventReadiness.Describe(hosted).Where(i => !i.Done).ToList();

        Assert.Equal(4, undone.Count);
        Assert.All(undone, i => Assert.False(string.IsNullOrWhiteSpace(i.Sentence)));
    }
}
