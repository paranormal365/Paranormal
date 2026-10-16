using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Website.Library.Kit;
using Ben.Web.Website.Library.Organization.Public;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// "What I'm going to" lists everything Home counts, not only hosted bookings (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Home's card says "and 2 more" and links here, and this page used to list hosted bookings
/// alone: the RSVPs, the tour seats and the investigations the card counted were nowhere on it.
/// The bookings keep their own rows (passes, party panels) above; the rest follow by date.</para>
///
/// <para>Rendered through <see cref="HtmlRenderer"/>, because the claim is what a person sees.
/// Each was run against the page as it was before — the intro describing bookings only, and no
/// section for anything else — and failed.</para>
/// </remarks>
public sealed class MyEventsPageTests
{
    private sealed class FakeNav : NavigationManager
    {
        public FakeNav() => Initialize("https://unit.test/", "https://unit.test/my-events");
    }

    private const string Chicago = "America/Chicago";
    private static readonly DateTime Now = new(2026, 10, 20, 17, 0, 0, DateTimeKind.Utc);

    private static MyNextEventItem Item(MyNextEventKind kind, string title, DateTime start,
        MyNextEventStanding standing = MyNextEventStanding.Going) => new(
        kind, Guid.NewGuid(), title, "Ghost Squad", start, start.AddHours(3), Chicago,
        $"/o/ghost-squad/events/{title.Replace(' ', '-').ToLowerInvariant()}", standing, start.AddDays(-10));

    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        var userState = new Mock<IBenUserState>();
        userState.SetupGet(u => u.BrowserTimeZone).Returns(TimeZoneInfo.Utc);
        userState.SetupGet(u => u.ViewerTimeZone).Returns(TimeZoneInfo.Utc);
        userState.SetupGet(u => u.AuthReady).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager, FakeNav>();
        services.AddSingleton(userState.Object);
        services.AddSingleton(new Mock<IJSRuntime>().Object);
        services.AddSingleton<TimeView>();
        services.AddSingleton(new Mock<IBenAdminClient>().Object);

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    private static Task<string> OthersAsync(IReadOnlyList<MyNextEventItem>? items, string? refusal = null)
        => RenderAsync<MyOtherEvents>(new()
        {
            [nameof(MyOtherEvents.Items)] = items,
            [nameof(MyOtherEvents.Refusal)] = refusal,
            [nameof(MyOtherEvents.NowUtc)] = Now,
        });

    [Fact]
    public async Task The_intro_says_what_is_on_the_page()
    {
        // The sentence wraps in the markup; read it the way a browser lays it out.
        var html = System.Text.RegularExpressions.Regex.Replace(await RenderAsync<MyEvents>([]), @"\s+", " ");

        Assert.Contains("the events and tours you said you'd come to, and the investigations you're on the team of", html);
        Assert.DoesNotContain("Events you've asked to come to, or been given a place at.", html);
    }

    [Fact]
    public async Task Rsvps_tour_seats_and_investigations_are_listed_by_date_with_their_words()
    {
        var html = await OthersAsync(
        [
            Item(MyNextEventKind.Investigation, "The Old Mill", Now.AddDays(5), MyNextEventStanding.Invited),
            Item(MyNextEventKind.Event, "Ghost Walk", Now.AddDays(1)),
            Item(MyNextEventKind.TourDate, "Printers Alley Walk", Now.AddDays(3), MyNextEventStanding.AskedFor),
        ]);

        Assert.Contains("id=\"my-events-others\"", html);
        var walk = html.IndexOf("Ghost Walk", StringComparison.Ordinal);
        var tour = html.IndexOf("Printers Alley Walk", StringComparison.Ordinal);
        var visit = html.IndexOf("The Old Mill", StringComparison.Ordinal);
        Assert.True(walk < tour && tour < visit, "soonest first");

        Assert.Contains("You're coming", html);
        Assert.Contains("Asked for", html);
        Assert.Contains("You're invited", html);
        Assert.Contains("Tour ·", html);
        Assert.Contains("Investigation ·", html);
        Assert.Contains("href=\"/o/ghost-squad/events/ghost-walk\"", html);
    }

    [Fact]
    public async Task Each_row_has_the_compact_countdown()
    {
        var html = await OthersAsync([Item(MyNextEventKind.Event, "Ghost Walk", Now.AddDays(2))]);

        Assert.Contains("data-testid=\"my-other-countdown\"", html);
        Assert.Contains("ev-cd--compact", html);
        Assert.Contains("aria-label=\"Ghost Walk. Starts", html);
    }

    /// <summary>The bookings already have their own rows, with their passes; they are not listed twice.</summary>
    [Fact]
    public async Task Hosted_bookings_are_left_to_their_own_rows()
    {
        var html = await OthersAsync([Item(MyNextEventKind.HostedEvent, "Seance Weekend", Now.AddDays(4))]);

        Assert.DoesNotContain("Seance Weekend", html);
        Assert.DoesNotContain("my-events-others", html);
    }

    [Fact]
    public async Task What_ended_lately_is_under_recently()
    {
        var html = await OthersAsync(
        [
            Item(MyNextEventKind.Event, "Last week", Now.AddDays(-7)),
            Item(MyNextEventKind.Event, "Next week", Now.AddDays(7)),
        ]);

        Assert.Contains("Recently", html);
        Assert.True(html.IndexOf("Next week", StringComparison.Ordinal) < html.IndexOf("Last week", StringComparison.Ordinal));
        Assert.Contains("id=\"my-events-recent\"", html);
    }

    [Fact]
    public async Task A_failed_load_says_so_and_offers_to_try_again()
    {
        var html = await OthersAsync(null, "Couldn't load your other events, tours and investigations just now.");

        Assert.Contains("Couldn't load your other events, tours and investigations just now.", html);
        Assert.Contains("Try again", html);
    }

    [Fact]
    public async Task Nothing_else_draws_nothing()
        => Assert.DoesNotContain("my-other", await OthersAsync([]));
}
