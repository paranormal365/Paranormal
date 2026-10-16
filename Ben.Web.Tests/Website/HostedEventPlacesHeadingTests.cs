using System.Reflection;
using System.Text.RegularExpressions;
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
/// A signed-out visitor on a hosted event's page is asked "Coming along?" once, whichever way the
/// event gives its places out.
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> On 10/16/2026 the Thomas House weekend asked a signed-out visitor
/// "Coming along?" twice, on a phone and on a desktop: <see cref="HostedEventPlaces"/> printed it,
/// and then the page's email form, passed in as <c>SignedOut</c>, printed it again. The form now
/// learns whether a heading is already above it and draws its own only when there is none.</para>
///
/// <para>The form here is a stand-in that draws the heading exactly when it is told to, the way
/// the page's own does, so what is counted is the component keeping its side of that agreement.
/// Rendered through the framework's <see cref="HtmlRenderer"/>; the plan the component would load
/// after its first render is put in place before it.</para>
/// </remarks>
public sealed class HostedEventPlacesHeadingTests
{
    private const string Ask = "Coming along?";

    /// <summary><see cref="HostedEventPlaces"/> with a plan already read, as it has after its first render.</summary>
    private sealed class PlacesWithPlan : HostedEventPlaces
    {
        [Parameter] public PublicHostedEventPlanRecord? Seed { get; set; }

        protected override void OnParametersSet()
        {
            typeof(HostedEventPlaces)
                .GetField("_plan", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(this, Seed);
            base.OnParametersSet();
        }
    }

    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/o/x/events/y");
    }

    private static readonly HostedEventNightRecord Night = new(
        Guid.NewGuid(), new DateTime(2026, 10, 30), "Friday 10/30/2026", null, null, null, null, 0);

    private static PublicHostedEventRecord AnEvent(int? dayPasses = null, bool taking = true) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Paranormal365", "paranormal365",
        "Thomas House Seance Weekend", "thomas-house-seance-weekend", null, null,
        "America/Chicago", new DateTime(2026, 10, 30), new DateTime(2026, 10, 31),
        DatesAreSeparate: true, DateNoun: "night", VenueName: "The Thomas House Hotel",
        City: "Red Boiling Springs", State: "TN", ExactAddress: null, IsExactAddressHidden: false,
        Latitude: null, Longitude: null, DayPassCapacity: dayPasses, ContactLine: null,
        CoverUploadFileId: null, IsCancelled: false, CancelledReason: null,
        CollectsEvidence: false, Nights: [Night], IsTakingBookings: taking);

    private static PublicHostedEventPlanRecord APlan(bool picking, string? closed = null) => new(
        Guid.NewGuid(), HostedEventLayoutKind.Rooms, HostedEventBookingMode.Pick,
        IsPicking: picking, ClosedSentence: closed, HoldMinutes: 15, MaxPartySize: 8,
        Nights: [Night],
        Units: [new PublicHostedEventPlanUnitRecord(Guid.NewGuid(), "Room 1", null, 2, null, 0, 0, 0)],
        Cells: []);

    /// <summary>Renders it signed out, and says what it asked of the form each time it drew it.</summary>
    private static async Task<(string Html, List<bool> FormAsked)> RenderAsync(
        PublicHostedEventRecord hosted, PublicHostedEventPlanRecord? plan)
    {
        var asked = new List<bool>();
        RenderFragment<bool> form = drawsHeading => builder =>
        {
            asked.Add(drawsHeading);
            if (drawsHeading) builder.AddMarkupContent(0, $"<div class=\"event-act__ask\">{Ask}</div>");
            builder.AddMarkupContent(1, "<p>the email form</p>");
        };

        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager, FakeNav>();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);
        services.AddSingleton(new Mock<IBenUserState>().Object);
        services.AddSingleton(new Mock<Microsoft.JSInterop.IJSRuntime>().Object);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PlacesWithPlan>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(HostedEventPlaces.Hosted)] = hosted,
                    [nameof(HostedEventPlaces.IsAuthenticated)] = false,
                    [nameof(HostedEventPlaces.SignedOut)] = form,
                    [nameof(PlacesWithPlan.Seed)] = plan,
                }));

            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });

        return (html, asked);
    }

    private static int Count(string html, string text) => Regex.Matches(html, Regex.Escape(text)).Count;

    [Fact]
    public async Task Asking_in_words_the_form_brings_the_one_heading()
    {
        var (html, asked) = await RenderAsync(AnEvent(), plan: null);

        Assert.Equal(1, Count(html, Ask));
        Assert.Equal([true], asked);
    }

    [Fact]
    public async Task Picking_off_the_plan_the_heading_is_above_the_plan_and_the_day_form_has_none()
    {
        var (html, asked) = await RenderAsync(AnEvent(dayPasses: 10), APlan(picking: true));

        Assert.Equal(1, Count(html, Ask));
        Assert.True(html.IndexOf(Ask, StringComparison.Ordinal) < html.IndexOf("id=\"hosted-picker\"", StringComparison.Ordinal),
            "The question belongs above the plan.");
        Assert.Equal(1, Count(html, "Just for the day?"));
        Assert.Equal([false], asked);
    }

    [Fact]
    public async Task Picking_with_no_day_passes_asks_once_and_draws_no_form()
    {
        var (html, asked) = await RenderAsync(AnEvent(dayPasses: 0), APlan(picking: true));

        Assert.Equal(1, Count(html, Ask));
        Assert.DoesNotContain("Just for the day?", html);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Closed_says_why_under_one_heading()
    {
        var (html, asked) = await RenderAsync(AnEvent(), APlan(picking: false, closed: "Bookings closed on Friday."));

        Assert.Equal(1, Count(html, Ask));
        Assert.Contains("Bookings closed on Friday.", html);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Not_taking_bookings_asks_nothing()
    {
        var (html, asked) = await RenderAsync(AnEvent(taking: false), plan: null);

        Assert.Equal(0, Count(html, Ask));
        Assert.Contains("id=\"hosted-closed\"", html);
        Assert.Empty(asked);
    }
}
