using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The colours a venue's guests wear, from the page that sets them to the door that shows them
/// (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"lets their employees know by glance what a person is registered
/// for … or color displayed on the qr code reader next to name of guest."</i></para>
///
/// <para><b>The claim worth a browser is the round trip</b>: a band typed on one page is the chip
/// beside the name on another, worked out from the rule rather than tagged by hand. Which party
/// wears which colour is settled by <c>EventBandsTests</c>; this proves the screens agree.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventBandsPageTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _nightId = string.Empty;

    [SetUp]
    public async Task ConfirmAPartyAndClearTheBands()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

        await ClearTheHouseAsync(admin);
        await LetGoAsync(guest);
        await admin.PutAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/bands",
            new() { DataObject = new { bands = Array.Empty<object>() } });

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
            // The organizer has to be able to reach whoever holds (slice 11d).
            firstName = "Test", lastName = "Guest", phone = "615-555-0100",
        };

        var held = await guest.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds", new() { DataObject = body });
        Assert.That(held.Status, Is.EqualTo(200), await held.TextAsync());

        var bookingId = (await held.JsonAsync())!.Value.GetProperty("id").GetString();
        var confirmed = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{bookingId}/confirm",
            new() { DataObject = new { nights = body.nights } });
        Assert.That(confirmed.Ok, Is.True, await confirmed.TextAsync());

        await admin.DisposeAsync();
        await guest.DisposeAsync();

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutItBack()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheHouseAsync(admin);
        await admin.PutAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/bands",
            new() { DataObject = new { bands = Array.Empty<object>() } });
        await admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_band_set_on_the_page_is_the_chip_beside_the_name_at_the_door()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/bands");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#bands-save")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ClickUntilAsync(Page.Locator("#bands-add"), Page.Locator("input[id^='band-colour-']"));
        await Page.Locator("input[id^='band-colour-']").First.FillAsync("Purple");
        await Page.Locator("input[id^='band-meaning-']").First.FillAsync("The whole evening");
        await Page.Locator("select[id^='band-rule-']").First.SelectOptionAsync("0");

        await ClickUntilAsync(Page.Locator("#bands-save"), Page.Locator("#bands-note"));
        await Expect(Page.Locator("#bands-note")).ToContainTextAsync("door will show them");

        // The door, where a steward actually reads it. Worked out from "every night", not tagged.
        await Page.GotoAsync(
            $"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/door?night={_nightId}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#door-expected")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#door-expected .band-chip").First).ToContainTextAsync("Purple");
    }

    [Test]
    public async Task A_band_with_no_meaning_is_refused_because_a_colour_alone_says_nothing()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/bands");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#bands-save")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await ClickUntilAsync(Page.Locator("#bands-add"), Page.Locator("input[id^='band-colour-']"));
        await Page.Locator("input[id^='band-colour-']").First.FillAsync("Orange");

        await ClickUntilAsync(Page.Locator("#bands-save"), Page.Locator("#bands-error"));
        await Expect(Page.Locator("#bands-error")).ToContainTextAsync("what each band means");
    }

    [Test]
    [TestCase(1280, 800)]
    [TestCase(375, 812)]
    public async Task The_page_never_slides_sideways_however_narrow_it_is(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/bands");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#bands-save")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the bands page slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

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
