using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The three place and venue search boxes after fix/oct16-gaps (gaps 8 and 9, 10/16/2026): Enter
/// searches, each box has an id and a label tied to it, and all three say the same true thing
/// before anything is typed. Also the settings index's "Your venue" link (gap 12).
/// </summary>
/// <remarks>
/// <para><b>The boxes.</b> Calendar → New event → Public → "Where (a place)"
/// (<c>#calendar-place-search</c>); Group → Events → Add an event → Venue
/// (<c>#new-event-place</c>); and an event's page → Move it somewhere else
/// (<c>#venue-search</c>). Before, only the Search button searched, two boxes had no id and no
/// label pointed at them, and the three placeholders differed.</para>
///
/// <para><b>"Nothing else submits"</b> is checked where it could happen: the calendar editor stays
/// open with nothing saved (the group's calendar has as many entries after as before), the events
/// page makes no event (as many hosted events after as before) and stays put, and the venue
/// picker stays open with nothing picked.</para>
///
/// <para>Nothing is saved: each form is left with Cancel or by leaving the page.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("PlaceSearch")]
[NonParallelizable]
public class Oct16SearchBoxTests : Oct16TestBase
{
    private const string Placeholder = "Part of the name, or the town";

    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";

    [SetUp]
    public async Task SignInTheApi()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);
    }

    private async Task<int> CountAsync(string url, string what)
        => (await OkJsonAsync(await _sarah.GetAsync(url), what)).GetArrayLength();

    /// <summary>Types with the keyboard and presses Enter. The Search button is never touched.</summary>
    private static async Task TypeAndPressEnterAsync(ILocator box, string text)
    {
        await box.ClickAsync();
        await box.FillAsync("");
        await box.PressSequentiallyAsync(text, new() { Delay = 20 });
        await box.PressAsync("Enter");
    }

    private async Task OpenAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();
    }

    // ── The calendar's public event ─────────────────────────────────────────

    private async Task<ILocator> OpenTheCalendarBoxAsync()
    {
        await OpenAsync($"/organizations/{_orgId}/calendar");
        var dialog = Page.Locator(".modal.show").First;
        await ClickUntilAsync(Page.Locator("#calendar-new-event"), dialog.Locator("#orgscheduler-title-d7a1"));
        var isPublic = dialog.Locator("#ev-public");
        if (!await isPublic.IsCheckedAsync()) await isPublic.CheckAsync();
        var box = Page.Locator("#calendar-place-search");
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 10_000 });
        return box;
    }

    [Test]
    public async Task Enter_in_the_calendars_place_box_searches_and_saves_nothing()
    {
        await SkipIfFeatureOffAsync("features.events");
        await LoginAsync(UserEmail, UserPassword);
        var entriesBefore = await CountAsync($"/api/organizations/{_orgId}/calendar", "reading the calendar");

        var box = await OpenTheCalendarBoxAsync();
        var dialog = Page.Locator(".modal.show").First;
        var results = Page.Locator("div:has(> label[for='calendar-place-search']) .list-group .list-group-item");

        await TypeAndPressEnterAsync(box, "Bell Witch");
        await Expect(results.Filter(new() { HasTextString = "Bell Witch Cave" }).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(1);
        await Expect(dialog.Locator("#orgscheduler-title-d7a1")).ToHaveValueAsync("");
        await Expect(dialog.Locator(".alert-danger")).ToHaveCountAsync(0);
        Assert.That(await CountAsync($"/api/organizations/{_orgId}/calendar", "reading the calendar"), Is.EqualTo(entriesBefore),
            "Enter in the place box saved something on the calendar.");

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
    }

    // ── Group → Events → Add an event ───────────────────────────────────────

    [Test]
    public async Task Enter_in_the_add_an_event_venue_box_searches_and_makes_no_event()
    {
        await SkipIfFeatureOffAsync("features.events");
        await LoginAsync(UserEmail, UserPassword);
        var eventsBefore = await CountAsync($"/api/organizations/{_orgId}/events", "reading the hosted events");

        await OpenAsync($"/organizations/{_orgId}/events");
        var box = Page.Locator("#new-event-place");
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var before = Page.Url;
        var results = Page.Locator("div:has(> label[for='new-event-place']) .list-group .list-group-item");

        await TypeAndPressEnterAsync(box, "Bell Witch");
        await Expect(results.Filter(new() { HasTextString = "Bell Witch Cave" }).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.That(Page.Url, Is.EqualTo(before), "Enter in the venue box left the page.");
        await Expect(box).ToBeVisibleAsync();
        Assert.That(await CountAsync($"/api/organizations/{_orgId}/events", "reading the hosted events"), Is.EqualTo(eventsBefore),
            "Enter in the venue box made an event.");
    }

    // ── An event's page → Move it somewhere else ────────────────────────────

    private async Task<ILocator> OpenTheVenueBoxAsync()
    {
        await OpenAsync($"/organizations/{_orgId}/events/{RoomsEventId}");
        await ClickUntilAsync(Page.Locator("#change-venue"), Page.Locator("#venue-picker"));
        var box = Page.Locator("#venue-search");
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 10_000 });
        return box;
    }

    [Test]
    public async Task Enter_in_the_event_pages_venue_box_searches_and_picks_nothing()
    {
        await SkipIfFeatureOffAsync("features.events");
        await LoginAsync(UserEmail, UserPassword);
        var box = await OpenTheVenueBoxAsync();
        var before = Page.Url;
        var picker = Page.Locator("#venue-picker");

        await TypeAndPressEnterAsync(box, "Bell Witch");
        await Expect(picker.Locator(".list-group .list-group-item", new() { HasTextString = "Bell Witch Cave" }).First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.That(Page.Url, Is.EqualTo(before), "Enter in the venue box left the page.");
        await Expect(picker).ToBeVisibleAsync();
        await Expect(picker).Not.ToContainTextAsync("Moving it to");
        await Expect(box).ToBeVisibleAsync();

        await Page.Locator("#cancel-venue-change").ClickAsync();
        await Expect(picker).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    // ── Labels and placeholders ─────────────────────────────────────────────

    /// <summary>Each box's label focuses it, and all three say the same thing before typing.</summary>
    [Test]
    public async Task Each_boxs_label_focuses_it_and_every_box_says_part_of_the_name_or_the_town()
    {
        await SkipIfFeatureOffAsync("features.events");
        await LoginAsync(UserEmail, UserPassword);

        var calendar = await OpenTheCalendarBoxAsync();
        await Expect(calendar).ToHaveAttributeAsync("placeholder", Placeholder);
        await Page.Locator("label[for='calendar-place-search']").ClickAsync();
        await Expect(calendar).ToBeFocusedAsync();
        await Page.Locator(".modal.show").First.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        await OpenAsync($"/organizations/{_orgId}/events");
        var events = Page.Locator("#new-event-place");
        await Expect(events).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(events).ToHaveAttributeAsync("placeholder", Placeholder);
        await Page.Locator("label[for='new-event-place']").ClickAsync();
        await Expect(events).ToBeFocusedAsync();

        var venue = await OpenTheVenueBoxAsync();
        await Expect(venue).ToHaveAttributeAsync("placeholder", Placeholder);
        await Page.Locator("label[for='venue-search']").ClickAsync();
        await Expect(venue).ToBeFocusedAsync();
        await Page.Locator("#cancel-venue-change").ClickAsync();
    }

    // ── Gap 12: the settings index ──────────────────────────────────────────

    [Test]
    public async Task The_settings_index_has_Your_venue_and_it_keeps_the_tab()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync($"/organizations/{_orgId}?tab=settings");

        var index = Page.Locator("nav[aria-label='Settings sections']");
        await Expect(index).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var venue = index.GetByRole(AriaRole.Link, new() { Name = "Your venue", Exact = true });
        await Expect(venue).ToBeVisibleAsync();
        Assert.That(await venue.GetAttributeAsync("href"), Does.Contain("?tab=settings#settings-venue"),
            "The index link should name this page, its tab and the venue section.");

        var before = Page.Url;
        await venue.ClickAsync();
        await Page.WaitForURLAsync(new Regex("#settings-venue$"), new() { Timeout = 10_000 });
        Assert.That(new Uri(Page.Url).GetLeftPart(UriPartial.Query), Is.EqualTo(new Uri(before).GetLeftPart(UriPartial.Query)),
            $"\"Your venue\" left the page: from {before} to {Page.Url}.");
        Assert.That(Page.Url, Does.Contain("tab=settings"), "Following \"Your venue\" lost the Settings tab.");
        await Expect(Page.Locator("#settings-venue")).ToBeInViewportAsync(new() { Timeout = 10_000 });
    }
}
