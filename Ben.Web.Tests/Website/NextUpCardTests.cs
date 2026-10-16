using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Ben.Web.Website.Library.Kit;
using Ben.Web.Website.Library.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Home's countdown card, as somebody sees it (10/16/2026).
/// </summary>
/// <remarks>
/// Rendered through the framework's own <see cref="HtmlRenderer"/>, because the claims are what a
/// person sees: no card when there is nothing, the words for where they stand, "and 2 more", a
/// refusal that is a sentence rather than an empty space, and a countdown a screen reader hears
/// once rather than every second.
/// </remarks>
public sealed class NextUpCardTests
{
    private const string Chicago = "America/Chicago";

    // Saturday 10/24/2026 7:00 PM CDT.
    private static readonly DateTime Saturday7pm = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    private static MyNextEventItem Item(string title, DateTime start,
        MyNextEventStanding standing = MyNextEventStanding.Going, MyNextEventKind kind = MyNextEventKind.Event,
        string href = "/o/ghost-squad/events/ghost-walk") => new(
        kind, Guid.NewGuid(), title, "Ghost Squad", start, start.AddHours(3), Chicago, href, standing,
        start.AddDays(-10));

    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        var userState = new Mock<IBenUserState>();
        userState.SetupGet(u => u.BrowserTimeZone).Returns(TimeZoneInfo.Utc);
        userState.SetupGet(u => u.ViewerTimeZone).Returns(TimeZoneInfo.Utc);
        userState.SetupGet(u => u.AuthReady).Returns(Task.CompletedTask);

        var services = new ServiceCollection();
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

    private static Task<string> CardAsync(MyNextEventsResponse? next, DateTime now, string? refusal = null)
        => RenderAsync<NextUpCard>(new()
        {
            [nameof(NextUpCard.Next)] = next,
            [nameof(NextUpCard.Refusal)] = refusal,
            [nameof(NextUpCard.NowUtc)] = now,
        });

    [Fact]
    public async Task Nothing_upcoming_draws_no_card_at_all()
    {
        var html = await CardAsync(new MyNextEventsResponse([], 0), Saturday7pm.AddDays(-3));
        Assert.DoesNotContain("home-event-countdown", html);
        Assert.DoesNotContain("next-up", html);
    }

    [Fact]
    public async Task Several_coming_up_shows_the_soonest_and_and_2_more()
    {
        var html = await CardAsync(new MyNextEventsResponse(
            [Item("Ghost Walk", Saturday7pm), Item("Seance Weekend", Saturday7pm.AddDays(7)), Item("Vigil", Saturday7pm.AddDays(14))],
            3), Saturday7pm.AddDays(-2));

        Assert.Contains("Ghost Walk", html);
        Assert.DoesNotContain("Seance Weekend", html);
        Assert.Contains("and 2 more", html);
        Assert.Contains("href=\"/my-events\"", html);
        Assert.Contains("You're coming · Event", html);
        Assert.Contains("Starts in 2 days", html);
    }

    [Fact]
    public async Task One_asked_for_says_so()
    {
        var html = await CardAsync(new MyNextEventsResponse(
            [Item("Seance Weekend", Saturday7pm, MyNextEventStanding.AskedFor, MyNextEventKind.HostedEvent)], 1),
            Saturday7pm.AddDays(-2));

        Assert.Contains("Asked for · Event", html);
        Assert.DoesNotContain("You're coming", html);
        Assert.DoesNotContain("more", html);
    }

    [Fact]
    public async Task A_confirmed_booking_opens_its_pass()
    {
        var html = await CardAsync(new MyNextEventsResponse(
            [Item("Seance Weekend", Saturday7pm, kind: MyNextEventKind.HostedEvent, href: "/my-events/abc/pass")], 1),
            Saturday7pm.AddDays(-2));

        Assert.Contains("href=\"/my-events/abc/pass\"", html);
    }

    /// <summary>Silence on a failure would read as "nothing coming up" when the site never looked.</summary>
    [Fact]
    public async Task A_failed_load_says_so_and_offers_to_try_again()
    {
        var html = await CardAsync(null, Saturday7pm, "Couldn't check what you're going to next.");

        Assert.Contains("Couldn't check what you're going to next.", html);
        Assert.Contains("Try again", html);
        Assert.DoesNotContain("home-event-countdown", html);
    }

    [Fact]
    public async Task On_the_day_it_says_tonight_and_while_it_runs_happening_now()
    {
        var next = new MyNextEventsResponse([Item("Ghost Walk", Saturday7pm)], 1);

        var tonight = await CardAsync(next, Saturday7pm.AddHours(-10));
        Assert.Contains("data-phase=\"tonight\"", tonight);
        Assert.Contains(">Tonight<", tonight);

        var running = await CardAsync(next, Saturday7pm.AddMinutes(20));
        Assert.Contains("data-phase=\"live\"", running);
        Assert.Contains("Happening now", running);
    }

    [Fact]
    public async Task After_it_ends_the_card_moves_on_to_the_next()
    {
        var next = new MyNextEventsResponse([Item("Ghost Walk", Saturday7pm), Item("Vigil", Saturday7pm.AddDays(7))], 2);

        var html = await CardAsync(next, Saturday7pm.AddHours(4));
        Assert.Contains("Vigil", html);
        Assert.DoesNotContain("Ghost Walk", html);
        Assert.DoesNotContain("more", html);
    }

    /// <summary>A screen reader hears the start in words, once; the ticking is not a live region.</summary>
    [Fact]
    public async Task The_countdown_is_labeled_with_the_start_in_words_and_is_not_a_live_region()
    {
        var html = await RenderAsync<EventCountdown>(new()
        {
            [nameof(EventCountdown.StartUtc)] = Saturday7pm,
            [nameof(EventCountdown.EndUtc)] = Saturday7pm.AddHours(3),
            [nameof(EventCountdown.ZoneId)] = Chicago,
            [nameof(EventCountdown.Name)] = "Ghost Walk",
            [nameof(EventCountdown.NowUtc)] = Saturday7pm.AddDays(-2),
        });

        Assert.Contains("role=\"img\"", html);
        Assert.Contains("aria-label=\"Ghost Walk. Starts Saturday, 10/24/2026 at 7:00 PM CDT\"", html);
        Assert.DoesNotContain("aria-live", html);
        Assert.Contains("data-cd-steps=", html);
    }

    [Fact]
    public async Task The_bar_is_drawn_at_the_right_width_before_any_script_runs()
    {
        var html = await RenderAsync<EventCountdown>(new()
        {
            [nameof(EventCountdown.StartUtc)] = Saturday7pm,
            [nameof(EventCountdown.ZoneId)] = Chicago,
            [nameof(EventCountdown.ListedUtc)] = Saturday7pm.AddDays(-10),
            [nameof(EventCountdown.NowUtc)] = Saturday7pm.AddDays(-5),
        });

        Assert.Contains("width:50%", html);
        Assert.Contains("Starts in 5 days", html);
        Assert.Contains("5d 00h 00m", html);
    }
}
