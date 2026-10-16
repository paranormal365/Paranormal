using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The public event page's "How long" tile says a long event the way a person would — nights or
/// days, never "352 hr" (fix/public-event-page, 10/16/2026).
/// </summary>
/// <remarks>
/// Two public dates are made on Paranormal365's calendar on Central time and deleted afterwards:
/// 6:00 PM to 6:00 AM fifteen days later (ends earlier in the day than it starts, so it runs over
/// nights: 15) and 9:00 AM to 5:00 PM four days later (later in the day, so it runs over days: 5).
/// The seeded séance weekend covers the hosted path, which counts its own dates.
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("PublicEvents")]
[NonParallelizable]
public class Oct16EventLengthTests : Oct16TestBase
{
    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";
    private readonly List<string> _dates = [];

    private static readonly Regex InHours = new(@"\d+\s*hr", RegexOptions.IgnoreCase);

    [SetUp]
    public async Task SignIn()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
    }

    [TearDown]
    public async Task TakeTheDatesAway()
    {
        foreach (var id in _dates) await _sarah.DeleteAsync($"/api/organizations/{_orgId}/calendar/{id}");
        _dates.Clear();
    }

    private ILocator HowLong => Page.Locator(".page-hero__fact", new() { HasTextString = "How long" }).Locator("dd");

    private async Task<string> HowLongItReadsAsync(DateTime startLocal, DateTime endLocal)
    {
        var (id, slug) = await PublicCalendarEventAsync(_sarah, _orgId, $"Oct16 length {Guid.NewGuid():N}"[..21],
            UtcFrom(startLocal, "America/Chicago"), UtcFrom(endLocal, "America/Chicago"));
        _dates.Add(id);

        await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{slug}");
        await Expect(HowLong).ToBeVisibleAsync(new() { Timeout = 30_000 });
        return (await HowLong.InnerTextAsync()).Trim();
    }

    [Test]
    public async Task Fifteen_evenings_into_mornings_read_as_15_nights()
    {
        var first = DateTime.UtcNow.Date.AddDays(20);
        var shown = await HowLongItReadsAsync(first.AddHours(18), first.AddDays(15).AddHours(6));

        Assert.That(shown, Is.EqualTo("15 nights"), $"The tile read \"{shown}\".");
        Assert.That(InHours.IsMatch(shown), Is.False, $"A fifteen-night event is still counted in hours: \"{shown}\".");
    }

    [Test]
    public async Task Five_daytimes_read_as_5_days()
    {
        var first = DateTime.UtcNow.Date.AddDays(20);
        var shown = await HowLongItReadsAsync(first.AddHours(9), first.AddDays(4).AddHours(17));

        Assert.That(shown, Is.EqualTo("5 days"), $"The tile read \"{shown}\".");
    }

    [Test]
    public async Task An_evening_still_reads_in_hours()
    {
        // The short form is still the useful one: the change must not turn an evening into "1 night".
        var first = DateTime.UtcNow.Date.AddDays(20);
        var shown = await HowLongItReadsAsync(first.AddHours(19), first.AddHours(23));

        Assert.That(shown, Is.EqualTo("4 hr"), $"The tile read \"{shown}\".");
    }

    /// <summary>
    /// On every screen type, each fact tile shows all of what it says: on a 375px phone the
    /// Starts tile once showed "Sun 10/25" and lost its "7:00 PM CDT" line (10/16/2026).
    /// </summary>
    [TestCase("dark")]
    [TestCase("light")]
    public async Task Every_fact_tile_shows_its_whole_value_and_time_on_every_screen(string theme)
    {
        await UseThemeAsync(theme);
        var first = DateTime.UtcNow.Date.AddDays(20);
        var (id, slug) = await PublicCalendarEventAsync(_sarah, _orgId, $"Oct16 tiles {Guid.NewGuid():N}"[..20],
            UtcFrom(first.AddHours(19), "America/Chicago"), UtcFrom(first.AddHours(23), "America/Chicago"));
        _dates.Add(id);

        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{slug}");
            var starts = Page.Locator(".page-hero__fact", new() { HasTextString = "Starts" });
            await Expect(starts).ToBeVisibleAsync(new() { Timeout = 30_000 });

            // The time line is there and readable, not only present in the markup.
            if (!(await starts.InnerTextAsync()).Contains("7:00 PM"))
                problems.Add($"Starts tile at {width}px ({theme}) doesn't show the start time: \"{(await starts.InnerTextAsync()).ReplaceLineEndings(" ")}\".");

            // Nothing in any tile is cut off by the tile: each child's box sits inside its tile's box.
            var clipped = await Page.EvaluateAsync<string[]>(@"() => {
                const out = [];
                for (const tile of document.querySelectorAll('.page-hero__fact')) {
                    const t = tile.getBoundingClientRect();
                    for (const el of tile.querySelectorAll('*')) {
                        if (!el.textContent.trim() || el.children.length) continue;
                        const r = el.getBoundingClientRect();
                        const hidden = getComputedStyle(el).visibility === 'hidden' || r.width === 0 || r.height === 0;
                        if (hidden || r.bottom > t.bottom + 0.5 || r.right > t.right + 0.5 || r.top < t.top - 0.5 || r.left < t.left - 0.5
                            || el.scrollWidth > el.clientWidth + 1)
                            out.push(el.textContent.trim().slice(0, 40));
                    }
                }
                return out;
            }");
            foreach (var text in clipped)
                problems.Add($"A fact tile at {width}px ({theme}) cuts off \"{text}\".");

            var (scrollWidth, innerWidth) = await PageWidthsAsync();
            if (scrollWidth > innerWidth)
                problems.Add($"Event page at {width}px ({theme}) slides sideways — scrollWidth {scrollWidth} > innerWidth {innerWidth}.");

            await KeepAPictureAsync("event-facts", theme);
        }

        NothingWrong(problems);
    }

    [Test]
    public async Task A_hosted_run_counts_its_own_nights()
    {
        var slug = await PublishSeededEventAsync(_orgId, RoomsEventId);
        await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{slug}");
        await Expect(HowLong).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var shown = (await HowLong.InnerTextAsync()).Trim();

        // "2 nights" or "2 nights, 7:00 PM–2:00 AM" — counted by the dates, with the hours when every date keeps them.
        Assert.That(shown, Does.Match(@"^\d+ nights?(, .+)?$"), $"The weekend's tile read \"{shown}\".");
        Assert.That(InHours.IsMatch(shown), Is.False, $"The weekend is still counted in hours: \"{shown}\".");
    }
}
