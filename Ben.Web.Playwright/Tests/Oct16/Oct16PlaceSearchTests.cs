using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// The three place/venue boxes an event is placed with now search (fix/event-place-search,
/// 10/16/2026): part of a name or the town finds it, on the first press, and a private residence is
/// never offered.
/// </summary>
/// <remarks>
/// <para><b>What production showed.</b> Calendar → New event → Public → "Where (a place)": "Bell
/// Witch" found nothing (the duplicate matcher wants the whole name). Group → Events → Add an event
/// → Venue: the first press showed "nothing found", the second showed the cave — the "nothing" line
/// was drawn the moment Search was pressed, before any answer.</para>
///
/// <para><b>The first press is watched, not assumed.</b> <see cref="SearchOnceAsync"/> presses
/// once and samples the box until the answer arrives; if the "nothing matches" line is on screen at
/// any sample before the results, that is the old bug and the test fails.</para>
///
/// <para><b>A residence beside a landmark.</b> Two places share a word and a town: "Oct16 Lantern
/// Hall" (a public location) and "Oct16 Lantern Cottage" (somebody's home, made through a visit with
/// no case — the only door that can make one without a client case). Both are made once per
/// database and reused. They are placed in North Dakota with coordinates given, so nothing is
/// geocoded and nothing lands near the Nashville panels other tests count. A search for the shared
/// word, and for the town, must list the Hall and never the Cottage.</para>
///
/// <para>Nothing is saved: each form is left with Cancel or by leaving the page.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("PlaceSearch")]
[NonParallelizable]
public class Oct16PlaceSearchTests : Oct16TestBase
{
    private const string HallName = "Oct16 Lantern Hall";
    private const string CottageName = "Oct16 Lantern Cottage";
    private const string CottageVisit = "Oct16 Lantern Cottage visit";
    private const string Town = "Lanternburg";

    private IAPIRequestContext _sarah = null!;
    private string _orgId = "";

    [SetUp]
    public async Task ALandmarkAndAHomeThatShareAName()
    {
        await SkipIfFeatureOffAsync("features.events");
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _sarah = await ApiAsAsync(UserEmail, UserPassword);

        // The landmark: this door returns the existing one for the same address, so it is safe every run.
        await OkJsonAsync(await _sarah.PostAsync("/api/places/public-location", new()
        {
            DataObject = new
            {
                name = HallName, streetAddress1 = "1 Lantern Way", streetAddress2 = (string?)null,
                city = Town, state = "ND", zipCode = "58001", country = "US",
                latitude = 47.10m, longitude = -100.80m,
            },
        }), "making the public Lantern Hall");

        // The home, through a case-less visit — made once; nothing can delete a case-less visit.
        var visits = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations"), "reading the visits");
        var cottage = visits.EnumerateArray().FirstOrDefault(v => v.GetProperty("title").GetString() == CottageVisit);
        if (cottage.ValueKind != JsonValueKind.Object)
        {
            cottage = await OkJsonAsync(await _sarah.PostAsync($"/api/organizations/{_orgId}/investigations", new()
            {
                DataObject = new
                {
                    title = CottageVisit,
                    scheduledDateTime = DateTime.UtcNow.Date.AddDays(-400).AddHours(1),
                    newPlace = new
                    {
                        name = CottageName, streetAddress1 = "2 Lantern Lane", streetAddress2 = (string?)null,
                        city = Town, state = "ND", zipCode = "58001", country = "US",
                        latitude = 47.11m, longitude = -100.81m, kind = 1,   // PrivateResidence
                    },
                },
            }), "making the private Lantern Cottage");

            visits = await OkJsonAsync(await _sarah.GetAsync($"/api/organizations/{_orgId}/investigations"), "reading the visits");
            cottage = visits.EnumerateArray().First(v => v.GetProperty("title").GetString() == CottageVisit);
        }

        // The private-residence check means nothing unless the home really is one.
        Assert.That(cottage.GetProperty("placeName").GetString(), Is.EqualTo(CottageName));
        Assert.That(IsEnum(cottage.GetProperty("placeKind"), 1, "PrivateResidence"), Is.True,
            "Lantern Cottage is not recorded as a private residence, so the test below would prove nothing.");
    }

    // ── The calendar's public event ─────────────────────────────────────────

    [Test]
    public async Task The_calendars_public_event_box_finds_part_of_a_name_on_the_first_press_and_never_a_home()
    {
        await LoginAsync(UserEmail, UserPassword);
        var (box, search, results, nothing) = await OpenTheCalendarBoxAsync();

        var found = await SearchOnceAsync(box, search, "Bell Witch", results, nothing, "Bell Witch Cave");
        Assert.That(found, Has.Some.Contains("Bell Witch Cave"));

        await LandmarkNotHomeAsync(box, search, results, nothing);

        await Page.Locator(".modal.show").GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
    }

    // ── Group → Events → Add an event ───────────────────────────────────────

    [Test]
    public async Task The_add_an_event_venue_box_finds_part_of_a_name_on_the_first_press_and_never_a_home()
    {
        await LoginAsync(UserEmail, UserPassword);
        var (box, search, results, nothing) = await OpenTheEventsBoxAsync();

        var found = await SearchOnceAsync(box, search, "Bell Witch", results, nothing, "Bell Witch Cave");
        Assert.That(found, Has.Some.Contains("Bell Witch Cave"));

        await LandmarkNotHomeAsync(box, search, results, nothing);
    }

    // ── An event's page → Move it somewhere else ────────────────────────────

    [Test]
    public async Task The_event_pages_venue_box_finds_part_of_a_name_on_the_first_press_and_never_a_home()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        await ClickUntilAsync(Page.Locator("#change-venue"), Page.Locator("#venue-picker"));
        var picker = Page.Locator("#venue-picker");
        var box = picker.Locator(".input-group input").First;
        var results = picker.Locator(".list-group .list-group-item");
        var nothing = picker.GetByText("No listed venues match that", new() { Exact = false });

        var found = await SearchOnceAsync(box, Page.Locator("#search-venue"), "Bell Witch", results, nothing, "Bell Witch Cave");
        Assert.That(found, Has.Some.Contains("Bell Witch Cave"));

        await LandmarkNotHomeAsync(box, Page.Locator("#search-venue"), results, nothing);

        // Leave the event where it is — nothing was picked, and nothing is saved.
        await Page.Locator("#cancel-venue-change").ClickAsync();
        await Expect(Page.Locator("#venue-picker")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    // ── Looking at it ───────────────────────────────────────────────────────

    [Test]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_search_results_fit_the_screen(string theme)
    {
        await UseThemeAsync(theme);
        await LoginAsync(UserEmail, UserPassword);
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);

            var (box, search, results, nothing) = await OpenTheCalendarBoxAsync();
            await SearchOnceAsync(box, search, "Lantern", results, nothing, HallName);
            problems.AddRange(await LookAtAsync(results.First.Locator(".."), "place-search-calendar", theme, box, search));
            await Page.Locator(".modal.show").GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 10_000 });

            (box, search, results, nothing) = await OpenTheEventsBoxAsync();
            await SearchOnceAsync(box, search, "Lantern", results, nothing, HallName);
            problems.AddRange(await LookAtAsync(results.First.Locator(".."), "place-search-events", theme,
                box, search, Page.Locator("#new-venue-toggle")));
        }

        NothingWrong(problems);
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    /// <summary>Calendar → New event → Public, and the "Where (a place)" box's parts.</summary>
    /// <remarks>
    /// The box has no id (see the report), so it is found from its label, inside the open editor.
    /// Its placeholder is shared in spirit with two other boxes and is not used.
    /// </remarks>
    private async Task<(ILocator Box, ILocator Search, ILocator Results, ILocator Nothing)> OpenTheCalendarBoxAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/calendar");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        var dialog = Page.Locator(".modal.show").First;
        await ClickUntilAsync(Page.Locator("#calendar-new-event"), dialog.Locator("#orgscheduler-title-d7a1"));

        var isPublic = dialog.Locator("#ev-public");
        if (!await isPublic.IsCheckedAsync()) await isPublic.CheckAsync();

        var label = dialog.Locator("label", new() { HasTextRegex = new System.Text.RegularExpressions.Regex(@"^\s*Where \(a place\)\s*$") });
        await Expect(label).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var field = label.Locator("..");
        return (field.Locator("input.form-control").First,
                field.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }),
                field.Locator(".list-group .list-group-item"),
                field.GetByText("No public places match that", new() { Exact = false }));
    }

    /// <summary>Group → Events → Add an event, and the Venue box's parts.</summary>
    private async Task<(ILocator Box, ILocator Search, ILocator Results, ILocator Nothing)> OpenTheEventsBoxAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();
        await Expect(Page.Locator("#new-event-place")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var field = Page.Locator("div:has(> label[for='new-event-place'])");
        return (Page.Locator("#new-event-place"), Page.Locator("#search-event-place"),
                field.Locator(".list-group .list-group-item"),
                field.GetByText("No listed venues match that", new() { Exact = false }));
    }

    /// <summary>
    /// Types <paramref name="text"/> with the keyboard, presses Search ONCE, and watches until the
    /// answer arrives. Fails if "nothing matches" shows at any point before it, or if
    /// <paramref name="expect"/> never arrives. Answers the results' texts.
    /// </summary>
    private async Task<List<string>> SearchOnceAsync(
        ILocator box, ILocator search, string text, ILocator results, ILocator nothing, string expect)
    {
        await box.ClickAsync();
        await box.FillAsync("");
        await box.PressSequentiallyAsync(text, new() { Delay = 20 });
        // Enabled by the box's @oninput, so this proves the text reached the component.
        await Expect(search).ToBeEnabledAsync(new() { Timeout = 10_000 });

        await search.ClickAsync();

        var saidNothingFirst = false;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await results.Filter(new() { HasTextString = expect }).CountAsync() > 0) break;
            if (await nothing.CountAsync() > 0 && await nothing.First.IsVisibleAsync()) saidNothingFirst = true;
            await Task.Delay(50);
        }

        Assert.That(saidNothingFirst, Is.False,
            $"After one press for \"{text}\" the box said nothing matched before its answer arrived — the first-press bug.");
        await Expect(results.Filter(new() { HasTextString = expect }).First).ToBeVisibleAsync(new() { Timeout = 1_000 });
        await Expect(nothing).ToHaveCountAsync(0);
        return [.. await results.AllInnerTextsAsync()];
    }

    /// <summary>The shared word and the town each find the landmark and never the home.</summary>
    private async Task LandmarkNotHomeAsync(ILocator box, ILocator search, ILocator results, ILocator nothing)
    {
        foreach (var text in new[] { "Lantern", Town })
        {
            var found = await SearchOnceAsync(box, search, text, results, nothing, HallName);
            Assert.That(found, Has.None.Contains("Cottage"),
                $"Searching \"{text}\" offered the private Lantern Cottage: {string.Join(" | ", found)}");
        }
    }
}
