using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What a guest holds up at the door, and the list it hangs off (item 235 phase 6).
/// </summary>
/// <remarks>
/// <para><b>The pass has to work when the technology does not.</b> A cracked screen, a camera that
/// will not focus in the dark, a phone at four percent — every one of those is an ordinary
/// evening, so what is asserted here is that the words are on the pass as well as the code: who,
/// how many, which nights, and something short enough to read out across a desk.</para>
///
/// <para><b>And that a withdrawn pass says so.</b> The worst version of this screen is the one
/// that goes blank: a guest holding a dead code reads a blank as a fault, travels anyway, and
/// finds out at the door.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventPassTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _bookingId = string.Empty;

    [SetUp]
    public async Task ConfirmAPartySoAPassExists()
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
        var night = (await ev.JsonAsync())!.Value.GetProperty("nights").EnumerateArray()
            .First().GetProperty("id").GetString();

        var held = await guest.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds",
            new()
            {
                DataObject = new
                {
                    nights = new[] { new { hostedEventNightId = night, hostedEventLayoutUnitId = seat } },
                    partySize = 1,
                    // The organizer has to be able to reach whoever holds (slice 11d).
                    firstName = "Test", lastName = "Guest", phone = "615-555-0100",
                },
            });
        Assert.That(held.Status, Is.EqualTo(200), await held.TextAsync());
        _bookingId = (await held.JsonAsync())!.Value.GetProperty("id").GetString()!;

        // Confirming issues the pass. That is deliberate on the server's part — a guest told yes
        // and given nothing to show has to be looked up by name at the door.
        var confirmed = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{_bookingId}/confirm",
            new()
            {
                DataObject = new
                {
                    nights = new[] { new { hostedEventNightId = night, hostedEventLayoutUnitId = seat } },
                    decisionNote = "See you on the night.",
                },
            });
        Assert.That(confirmed.Ok, Is.True, await confirmed.TextAsync());

        await admin.DisposeAsync();
        await guest.DisposeAsync();

        await LoginAsync(ClientEmail, ClientPassword);
    }

    [TearDown]
    public async Task PutTheHouseBack()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheHouseAsync(admin);
        await admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    // ── the list ─────────────────────────────────────────────────────────────

    [Test]
    public async Task What_I_am_going_to_lists_the_booking_and_offers_the_pass()
    {
        // The page the bell points at. It used to point at /events — what is ON — which left
        // somebody hunting for their own booking in everybody's evenings.
        await Page.GotoAsync($"{BaseUrl}/my-events");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#my-events")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#my-events")).ToContainTextAsync("You're coming");

        await Page.Locator("#my-events a", new() { HasTextString = "Pass" }).First.ClickAsync();
        await Expect(Page.Locator("#pass-card")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    // ── the pass ─────────────────────────────────────────────────────────────

    [Test]
    public async Task The_pass_says_in_words_everything_the_code_says()
    {
        await OpenThePassAsync();

        await Expect(Page.Locator("#pass-admits")).ToContainTextAsync("Admits 1 person");
        await Expect(Page.Locator("#pass-nights")).ToBeVisibleAsync();

        // The short code, which is what a door types when the camera gives up.
        await Expect(Page.Locator("#pass-code")).ToBeVisibleAsync();
        var code = await Page.Locator("#pass-code").InnerTextAsync();
        Assert.That(code.Trim(), Has.Length.EqualTo(6),
            "the code somebody reads across a desk should be six characters");

        // And the code itself is drawn, not fetched: a pass in a field with one bar must not
        // depend on a second request.
        await Expect(Page.Locator("#pass-card svg, #pass-card canvas").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_withdrawn_pass_is_drawn_with_the_reason_rather_than_going_blank()
    {
        // The worst version of this screen is the blank one: a guest reads it as a fault, travels
        // anyway, and finds out at the door.
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var revoked = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{_bookingId}/pass/revoke",
            new() { DataObject = new { reason = "The seat was double-sold — come to the desk." } });
        Assert.That(revoked.Ok, Is.True, await revoked.TextAsync());
        await admin.DisposeAsync();

        await OpenThePassAsync();

        await Expect(Page.Locator("#pass-revoked")).ToBeVisibleAsync();
        await Expect(Page.Locator("#pass-revoked")).ToContainTextAsync("come to the desk");

        // Still drawn, because a guest showing an old code should be told it was replaced rather
        // than be told nothing at all.
        await Expect(Page.Locator("#pass-code")).ToBeVisibleAsync();
    }

    // ── the three widths ─────────────────────────────────────────────────────

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_pass_never_slides_sideways_however_narrow_it_is(int width, int height)
    {
        // It is read on a phone more often than anywhere else.
        await OpenThePassAsync(width, height);

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the pass slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenThePassAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/my-events/{SeatsEventId}/pass");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#pass-card")).ToBeVisibleAsync(new() { Timeout = 30_000 });
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
