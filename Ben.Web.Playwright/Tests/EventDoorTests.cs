using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The door, on a phone, in the dark (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>What is worth walking here is the failure case.</b> The camera is the fast path and it
/// is the one thing a browser test cannot exercise — so what these prove is that everything a
/// steward reaches for when the camera is no use actually works at 375 pixels: the code box, the
/// name search, a target big enough for a thumb, and the count that answers "is there room?".</para>
///
/// <para><b>It confirms its own party</b> through the venue's door rather than hoping the seed has
/// one: a fixture that depends on which day the database was built is one that passes today and
/// fails in a fortnight.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventDoorTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _nightId = string.Empty;

    [SetUp]
    public async Task ConfirmAPartyForTonight()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

        await ClearTheHouseAsync(admin);
        await LetGoAsync(guest);

        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/publish",
            new() { DataObject = new { } });

        var layout = await admin.GetAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/layout");
        var seat = (await layout.JsonAsync())!.Value.GetProperty("units").EnumerateArray()
            .First().GetProperty("id").GetString();

        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}");
        _nightId = (await ev.JsonAsync())!.Value.GetProperty("nights").EnumerateArray()
            .First().GetProperty("id").GetString()!;

        var body = new
        {
            nights = new[] { new { hostedEventNightId = _nightId, hostedEventLayoutUnitId = seat } },
            partySize = 1,
        };

        var held = await guest.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body });
        Assert.That(held.Status, Is.EqualTo(200), await held.TextAsync());

        var bookingId = (await held.JsonAsync())!.Value.GetProperty("id").GetString();

        var confirmed = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{bookingId}/confirm",
            new() { DataObject = new { nights = body.nights, decisionNote = "See you there." } });
        Assert.That(confirmed.Ok, Is.True, await confirmed.TextAsync());

        await admin.DisposeAsync();
        await guest.DisposeAsync();

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutTheHouseBack()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheHouseAsync(admin);
        await admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    // ── the night ────────────────────────────────────────────────────────────

    [Test]
    public async Task The_door_opens_on_tonight_with_the_count_at_the_top()
    {
        // The question a steward is asked most is "is there room?", and it is answered before
        // anything else on the screen.
        await OpenTheDoorAsync();

        await Expect(Page.Locator("#door-count")).ToBeVisibleAsync();
        await Expect(Page.Locator("#door-count")).ToContainTextAsync("expected");
        await Expect(Page.Locator("#door-room")).ToBeVisibleAsync();
        await Expect(Page.Locator("#door-expected")).ToContainTextAsync("Daniel");
    }

    [Test]
    public async Task Somebody_is_marked_in_and_the_count_moves()
    {
        await OpenTheDoorAsync();

        var arrive = Page.Locator("#door-expected button", new() { HasTextString = "They're here" }).First;
        await ClickUntilAsync(arrive, Page.Locator("#door-note"));

        await Expect(Page.Locator("#door-note")).ToContainTextAsync("is in");
        await Expect(Page.Locator("#door-expected")).ToContainTextAsync("In since");

        // And it can be taken back, because a doorway is where the wrong row gets pressed.
        var undo = Page.Locator("#door-expected button", new() { HasTextString = "Not them" }).First;
        await ClickUntilAsync(undo, Page.GetByText("not in after all"));
        await Expect(Page.Locator("#door-note")).ToContainTextAsync("not in after all");
    }

    [Test]
    public async Task A_name_search_narrows_the_list_because_a_camera_is_not_always_any_use()
    {
        await OpenTheDoorAsync();

        await Page.Locator("#door-search").FillAsync("zzz-nobody");
        await Expect(Page.Locator("#door-expected li")).ToHaveCountAsync(0);

        await Page.Locator("#door-search").FillAsync("Dan");
        await Expect(Page.Locator("#door-expected li").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_code_that_means_nothing_is_refused_in_words_a_steward_can_read_out()
    {
        // "Invalid" does not tell somebody whether to send a guest to the desk, wait, or turn them
        // away — and the person on the door is rarely the person who took the booking.
        await OpenTheDoorAsync();

        await Page.Locator("#door-code").FillAsync("NOTACODE");
        await ClickUntilAsync(Page.Locator("#door-code-go"), Page.Locator("#door-scan"));

        await Expect(Page.Locator("#door-scan")).ToBeVisibleAsync();
        // The server's own sentence, which ends with what to do instead — that is the part worth
        // asserting, rather than any particular word for the code itself.
        await Expect(Page.Locator("#door-scan")).ToContainTextAsync("look them up by name");
    }

    // ── walk-ups ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Somebody_who_turns_up_is_written_down_and_can_be_taken_back()
    {
        // Ben, 2026-09-13. No booking, no account, no pass — a head count, which is what a fire
        // officer asks for and what the kitchen is cooking against.
        await OpenTheDoorAsync();

        await Page.Locator("#walk-up-name").FillAsync("Two at the door");
        await ClickUntilAsync(Page.Locator("#walk-up-add"), Page.Locator("#walk-up-list"));

        await Expect(Page.Locator("#walk-up-list")).ToContainTextAsync("Two at the door");
        await Expect(Page.Locator("#door-note")).ToContainTextAsync("more in");

        var undo = Page.Locator("#walk-up-list button", new() { HasTextString = "Undo" }).First;
        await ClickUntilAsync(undo, Page.GetByText("Taken back"));
        await Expect(Page.Locator("#door-note")).ToContainTextAsync("Taken back");
    }

    // ── on a phone ───────────────────────────────────────────────────────────

    [Test]
    public async Task On_a_phone_the_count_and_the_camera_are_the_first_things_and_the_buttons_are_big()
    {
        // The case the whole screen is designed for: one thumb, in the dark, with somebody
        // standing in front of it.
        await OpenTheDoorAsync(375, 812);

        await Expect(Page.Locator("#door-count")).ToBeInViewportAsync();
        await Expect(Page.Locator("#scanner-toggle")).ToBeInViewportAsync();

        var arrive = Page.Locator("#door-expected button", new() { HasTextString = "They're here" }).First;
        await arrive.ScrollIntoViewIfNeededAsync();

        var box = await arrive.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Height, Is.GreaterThanOrEqualTo(55f),
            "the button that admits somebody is too small to hit in the dark");
    }

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_door_never_slides_sideways_however_narrow_it_is(int width, int height)
    {
        await OpenTheDoorAsync(width, height);

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the door slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheDoorAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(
            $"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/door?night={_nightId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#door-count")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task<IAPIRequestContext> SignedInAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in: {await login.TextAsync()}");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();

        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }

    private async Task ClearTheHouseAsync(IAPIRequestContext admin)
    {
        var board = await admin.GetAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings");
        if (!board.Ok) return;

        foreach (var booking in (await board.JsonAsync())!.Value
                     .GetProperty("bookings").EnumerateArray())
        {
            if (booking.GetProperty("status").GetInt32() is not (0 or 1 or 4)) continue;

            var id = booking.GetProperty("id").GetString();
            await admin.PostAsync(
                $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{id}/cancel",
                new() { DataObject = new { decisionNote = "Clearing up after a test." } });
        }
    }

    private static async Task LetGoAsync(IAPIRequestContext who)
    {
        var mine = await who.GetAsync("/api/public/hosted-events/mine");
        if (!mine.Ok) return;

        foreach (var booking in (await mine.JsonAsync())!.Value.EnumerateArray())
        {
            if (booking.GetProperty("hostedEventId").GetString() is { } id)
                await who.DeleteAsync($"/api/public/hosted-events/{id}/my-booking");
        }
    }
}
