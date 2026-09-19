using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Website.Library.Organization.Public;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// What a guest is told about their own booking, in each state it can be in
/// (item 235 phase 6).
/// </summary>
/// <remarks>
/// <para><b>The words are the feature.</b> "Asked for" and "You're coming" are the difference
/// between turning up and being turned away at a door, and a hold that ran out is emphatically not
/// a refusal — a guest who reads one as the other stops trying. The browser tests walk the three
/// states a guest can reach by clicking; these pin the three they cannot, which are the ones where
/// the wrong sentence does the most harm.</para>
///
/// <para>Rendered for real through the framework's own <see cref="HtmlRenderer"/>, because the
/// claim is what a person sees and a source scan cannot tell which branch wins.</para>
/// </remarks>
public sealed class EventBookingCardTests
{
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/o/x/events/y");
    }

    private static PublicHostedEventRecord AnEvent() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Paranormal365", "paranormal365",
        "An Evening of Evidence", "an-evening-of-evidence", null, null,
        "America/Chicago", new DateTime(2026, 10, 30), new DateTime(2026, 10, 30),
        DatesAreSeparate: true, DateNoun: "date", VenueName: "The Thomas House Hotel",
        City: "Red Boiling Springs", State: "TN", ExactAddress: null, IsExactAddressHidden: false,
        Latitude: null, Longitude: null, DayPassCapacity: null, ContactLine: null,
        CoverUploadFileId: null, IsCancelled: false, CancelledReason: null,
        CollectsEvidence: false, Nights: []);

    private static MyHostedEventBookingRecord ABooking(
        HostedEventBookingStatus status, DateTime? holdExpires = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), "An Evening of Evidence", "an-evening-of-evidence",
        "Paranormal365", "paranormal365", "The Thomas House Hotel",
        new DateTime(2026, 10, 30), new DateTime(2026, 10, 30),
        PartySize: 2, Kind: HostedEventBookingKind.Overnight, Status: status,
        DecisionNote: null, GuestAcknowledgedUtc: null, CancellationRequestedUtc: null,
        Note: null, Nights: [], Guests: [], HoldExpiresUtc: holdExpires);

    private static async Task<string> RenderAsync(MyHostedEventBookingRecord booking)
    {
        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager, FakeNav>();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<EventBookingCard>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(EventBookingCard.Booking)] = booking,
                    [nameof(EventBookingCard.Hosted)] = AnEvent(),
                }));

            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task A_request_says_plainly_that_nothing_is_held()
    {
        // The sentence that stops somebody packing a suitcase for a place nobody agreed to.
        var html = await RenderAsync(ABooking(HostedEventBookingStatus.Requested));

        Assert.Contains("Asked for", html);
        Assert.Contains("Nothing is held until", html);
        Assert.DoesNotContain("Your pass", html);
    }

    [Fact]
    public async Task A_hold_shows_the_clock_running()
    {
        var html = await RenderAsync(ABooking(
            HostedEventBookingStatus.Held, DateTime.UtcNow.AddHours(30)));

        Assert.Contains("Held for you", html);
        Assert.Contains("Yours for", html);
    }

    [Fact]
    public async Task A_hold_that_has_run_out_is_not_a_refusal()
    {
        // The one a guest is likeliest to misread. "Not this time" is a decision somebody made
        // about them; this is the opposite — nobody decided anything, and they may choose again.
        var html = await RenderAsync(ABooking(HostedEventBookingStatus.Expired));

        Assert.Contains("Hold ran out", html);
        Assert.Contains("choose again", html);
        Assert.DoesNotContain("couldn't take", html);
    }

    [Fact]
    public async Task A_refusal_carries_no_pass_and_offers_another_go()
    {
        var html = await RenderAsync(ABooking(HostedEventBookingStatus.TurnedDown));

        Assert.Contains("Not this time", html);
        Assert.Contains("Ask again", html);
        Assert.DoesNotContain("Your pass", html);
    }

    [Fact]
    public async Task A_confirmed_booking_offers_the_pass_and_asks_rather_than_releases()
    {
        // A confirmed booking is not withdrawn by the guest: the venue has catered, staffed and
        // possibly turned somebody else away against it, so the words say what actually happens.
        var html = await RenderAsync(ABooking(HostedEventBookingStatus.Confirmed));

        Assert.Contains("You're coming", html);
        Assert.Contains("Your pass", html);
        Assert.Contains("I can't make it", html);
        Assert.DoesNotContain("Never mind", html);
    }
}
