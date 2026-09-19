using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A venue makes one of its rooms bookable, from a screen, at three widths (item 235 phase 1).
/// </summary>
/// <remarks>
/// <para>Sleeps, Bookable and Beds had been on the API since phase 2.1 and nothing on the site could
/// set any of them: a booking plan could only ever offer a room somebody had made bookable by hand
/// in the database. This walks the rooms page doing it, and reads the row back afterwards, because
/// a form that posts the fields and a list that then shows a dash for Sleeps would pass every
/// server test and still leave the venue believing the number never took.</para>
///
/// <para><b>Three fixtures, not three test cases</b>, because the viewport is a property of the
/// browser context and <c>ContextOptions()</c> is asked for it before any <c>[SetUp]</c> runs — a
/// <c>[TestCase]</c> can only resize a page that already exists. The three widths are decision 13's:
/// every hosted screen declares what it does at a phone, a tablet and a desktop, and the phone
/// fixture carries the two assertions the other two cannot fail — the primary control is in the
/// viewport without scrolling, and the page never scrolls sideways. This is the org side's first
/// deliberate compact reflow; the table becomes stacked cards below 576px.</para>
///
/// <para>The business is registered through the same endpoint the Start a Group wizard uses, its
/// venue is made the way an event makes one it has never listed, and the business is purged
/// afterwards, taking its rooms with it. The venue itself is a shared <c>Place</c> and is not
/// deleted — there is no endpoint that deletes one, on purpose — so it is entered with the same
/// name, address and coordinates every run and the matcher hands back the existing row rather
/// than making another. One "Playwright Rooms House" ever, not one per run.</para>
/// </remarks>
[TestFixture(375, 812)]
[TestFixture(768, 1024)]
[TestFixture(1280, 800)]
[Category("PlaceRooms")]
[NonParallelizable]
public class PlaceRoomsBookableTests : BenTestBase
{
    private const string OrgName = "Playwright Rooms House Co";

    private readonly int _width;
    private readonly int _height;

    private IAPIRequestContext _api = null!;
    private string? _orgId;
    private string _placeId = null!;

    public PlaceRoomsBookableTests(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>The whole reason for the fixture parameters: the browser opens at this size.</summary>
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = _width, Height = _height },
    };

    private bool IsPhone => _width < 576;

    /// <summary>Sarah owns a venue; the venue has one event, which is how it got onto the map.</summary>
    [SetUp]
    public async Task AVenueWithNoRoomsYet()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });

        var login = await _api.PostAsync("/login", new()
        { DataObject = new { email = UserEmail, password = UserPassword } });
        if (!login.Ok) Assert.Ignore("The seeded owner seat cannot sign in on this deployment.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()!;
        var authed = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

        var slug = $"pw-rooms-{Guid.NewGuid():N}"[..20];
        var register = await _api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = authed,
            DataObject = new { name = OrgName, urlName = slug, kind = 0 },  // InvestigationGroup
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());
        _orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        // A place is never created on its own; it arrives with the thing that happens there. A
        // draft event costs nothing and is purged with the group, and its NewVenue is the one door
        // through which a group lists a building nobody has listed before. Coordinates are given so
        // the geocoder has nothing to look up, and the matcher can find the row again next run.
        var start = DateTime.UtcNow.Date.AddDays(30);
        var ev = await _api.PostAsync($"/api/organizations/{_orgId}/events", new()
        {
            Headers = authed,
            DataObject = new
            {
                name = "Rooms test weekend",
                placeId = Guid.Empty,
                startsOn = start,
                endsOn = start.AddDays(1),
                timeZoneId = "America/Chicago",
                newVenue = new
                {
                    name = "Playwright Rooms House",
                    streetAddress1 = "217 Rooms Test Lane",
                    city = "Nashville", state = "TN", zipCode = "37201", country = "US",
                    latitude = 36.1627m, longitude = -86.7816m,
                },
            },
        });
        Assert.That(ev.Ok, Is.True, await ev.TextAsync());
        _placeId = (await ev.JsonAsync())!.Value.GetProperty("placeId").GetString()!;
    }

    [TearDown]
    public async Task TakeTheBusinessAwayAgain()
    {
        if (_orgId is not null)
        {
            var admin = await _api.PostAsync("/login", new()
            { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
            if (admin.Ok)
            {
                var token = (await admin.JsonAsync())!.Value.GetProperty("accessToken").GetString();
                var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };

                // PURGE, not the ordinary delete: the group has an event and rooms hanging off it,
                // and the ordinary delete refuses in words for exactly that reason. The purge asks
                // for the group's name typed back, which is the guard against purging the wrong one.
                var purged = await _api.DeleteAsync(
                    $"/api/admin/organizations/{_orgId}/purge",
                    new() { Headers = headers, DataObject = new { confirmName = OrgName } });
                if (!purged.Ok)
                    await _api.DeleteAsync($"/api/organizations/{_orgId}", new() { Headers = headers });
            }
        }
        await _api.DisposeAsync();
    }

    [Test]
    [Description("Room 217 is named with a capacity and made bookable, and the list says so.")]
    public async Task A_room_is_made_bookable_and_the_row_says_so()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/places/{_placeId}/rooms");
        await WaitUntilLoadedAsync();

        // The add form is the page's primary control. Named, so a wrong address fails here rather
        // than as "the switch is missing" three assertions later.
        var name = Page.Locator("#room-name");
        await Expect(name).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Page.Locator("[data-testid=rooms-empty]")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var bookable = Page.Locator("#room-bookable");
        if (IsPhone)
        {
            // Decision 13, first half: on a phone the control that makes a room bookable is on
            // screen when the page opens, before anything has been scrolled or tapped. Asserted
            // BEFORE the form is touched — Playwright scrolls to whatever it fills, which would
            // make the same assertion afterwards true of any layout at all.
            await Expect(bookable).ToBeInViewportAsync();
        }

        await name.FillAsync("Room 217");
        await Page.Locator("#room-sleeps").FillAsync("2");
        await bookable.CheckAsync();
        await Page.Locator("#room-beds").FillAsync("one king");
        await Page.Locator("#room-add").ClickAsync();

        // Read back from the LIST, not from the form: the row is what the server returned after
        // the save, so a value the form sent and the server dropped shows up here as a dash.
        var row = Page.Locator("[data-testid=room-row]").Filter(new() { HasText = "Room 217" });
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(row.Locator("td[data-label=Sleeps]")).ToContainTextAsync("2");
        await Expect(row.Locator("td[data-label=Bookable] [data-testid=room-bookable]")).ToHaveTextAsync("Bookable");
        await Expect(row.Locator("td[data-label=Beds]")).ToContainTextAsync("one king");

        // And the form emptied itself, so the next room does not inherit this one's beds.
        await Expect(name).ToHaveValueAsync("");
        await Expect(bookable).Not.ToBeCheckedAsync();

        if (IsPhone)
        {
            // Decision 13, second half: no sideways page scroll, with a real row on the page. An
            // eight-column table inside .table-responsive would pass the empty-state version of
            // this and fail it the moment a room existed, which is why it is asserted last.
            var widths = await Page.EvaluateAsync<int[]>(
                "[document.documentElement.scrollWidth, document.documentElement.clientWidth]");
            Assert.That(widths[0], Is.LessThanOrEqualTo(widths[1]),
                $"The page scrolls sideways at {_width}px: scrollWidth {widths[0]} > clientWidth {widths[1]}.");

            // The row is a stacked card, not a table row — the reflow actually applied rather than
            // the table merely being narrow enough to fit by luck.
            var display = await row.EvaluateAsync<string>("el => getComputedStyle(el).display");
            Assert.That(display, Is.EqualTo("block"),
                "Below 576px each room is a stacked card; the row still lays out as a table row.");
        }
        else
        {
            // Wide: the heading row is the labels, and it is on screen.
            await Expect(Page.Locator("[data-testid=rooms-table] thead")).ToBeVisibleAsync();
        }

        if (Environment.GetEnvironmentVariable("BEN_ROOMS_SHOT") is { Length: > 0 } shotDir)
        {
            Directory.CreateDirectory(shotDir);
            await Page.ScreenshotAsync(new()
            { Path = Path.Combine(shotDir, $"place-rooms-bookable-{_width}.png"), FullPage = true });
        }
    }
}
