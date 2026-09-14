using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A guest choosing their own seats off the plan (item 235 phase 6).
/// </summary>
/// <remarks>
/// <para><b>This is the screen the whole soft-hold design exists for.</b> Phase 4 proved two API
/// clients cannot hold one seat; what could not be proved without a screen is that a person can
/// choose several, see what they add up to, commit them, and be told how long they have — and
/// that a second person watching the same house sees those squares stop being available.</para>
///
/// <para><b>At 375 pixels as well</b>, because a guest picking seats on a phone in a pub is the
/// ordinary case and the summary bar exists entirely for it: without it the button that commits is
/// two hundred squares below the first one they tap.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventSeatPickerTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _publicUrl = string.Empty;

    [SetUp]
    public async Task EmptyTheHouse()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);
        var other = await SignedInAsync(MemberEmail, MemberPassword);

        await LetGoAsync(guest);
        await LetGoAsync(other);
        await ClearTheHouseAsync(admin);

        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/publish",
            new() { DataObject = new { } });

        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}");
        var slug = (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString();
        _publicUrl = $"{BaseUrl}/o/paranormal365/events/{slug}";

        await admin.DisposeAsync();
        await guest.DisposeAsync();
        await other.DisposeAsync();

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

    // ── choosing ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Three_seats_is_a_party_of_three_and_the_bar_says_so_before_anything_is_held()
    {
        // The bug this counts its way out of: a guest who held one seat for four people had a
        // booking the venue could never confirm and could never be told why.
        await OpenThePickerAsync();

        await PickAsync(0, 1, 2);

        await Expect(Page.Locator("#picker-bar")).ToContainTextAsync("3 seats");
        await Expect(Page.Locator("#picker-bar")).ToContainTextAsync("a party of 3");

        // Nothing is held yet: every tap before the button is a choice they can change.
        Assert.That(await Page.Locator("#hosted-booking").CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Holding_them_says_how_long_they_are_yours()
    {
        // A hold that lapses is a decision the clock takes instead of the venue, so the deadline
        // is shown before somebody commits and counted down afterwards.
        await OpenThePickerAsync();
        await PickAsync(0, 1);

        await Expect(Page.Locator("#picker-bar")).ToContainTextAsync("Held for");

        await FillBookingContactAsync("picker");
        await ClickUntilAsync(Page.Locator("#picker-hold"), Page.Locator("#hosted-booking"));

        await Expect(Page.Locator("#booking-state")).ToContainTextAsync("Held for you");
        await Expect(Page.Locator("#booking-countdown")).ToContainTextAsync("Yours for");
    }

    [Test]
    public async Task What_one_guest_holds_the_next_one_cannot_pick()
    {
        // The rule the whole design turns on, seen from the outside: a held square is out of
        // everybody else's reach, and it is drawn as being decided rather than as sold.
        await OpenThePickerAsync();
        await PickAsync(0);
        await FillBookingContactAsync("picker");
        await ClickUntilAsync(Page.Locator("#picker-hold"), Page.Locator("#hosted-booking"));
        await Expect(Page.Locator("#booking-state")).ToContainTextAsync("Held for you");

        // The same house, read by somebody else entirely.
        await LoginAsync(MemberEmail, MemberPassword);
        await OpenThePickerAsync();

        await Expect(Page.Locator(".plan__unit[data-state='pending']").First)
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    public async Task Letting_a_hold_go_puts_the_seats_back_and_offers_the_plan_again()
    {
        await OpenThePickerAsync();
        await PickAsync(0);
        await FillBookingContactAsync("picker");
        await ClickUntilAsync(Page.Locator("#picker-hold"), Page.Locator("#hosted-booking"));

        await ClickUntilAsync(Page.Locator("#booking-let-go"), Page.Locator("#booking-note"));

        await Expect(Page.Locator("#booking-note")).ToContainTextAsync("let go");
    }

    // ── on a phone ───────────────────────────────────────────────────────────

    [Test]
    public async Task On_a_phone_the_summary_and_the_button_are_reachable_without_scrolling_back()
    {
        // The bar exists entirely for this case: two hundred squares between the first tap and the
        // button that commits.
        await OpenThePickerAsync(375, 812);
        await PickAsync(0, 1);

        await Expect(Page.Locator("#picker-bar")).ToBeInViewportAsync();

        var hold = Page.Locator("#picker-hold");
        await Expect(hold).ToBeInViewportAsync();

        var box = await hold.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Height, Is.GreaterThanOrEqualTo(43.5f),
            "the button that commits somebody's evening is too small to press safely");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenThePickerAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(_publicUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#hosted-places")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>Taps free squares by their position in the grid.</summary>
    /// <remarks>
    /// By index rather than by name, because which seats are free depends on what the run before
    /// left behind, and a fixture that insists on A1 is a fixture that fails on a Tuesday. The
    /// claim under test is about counting and committing, not about which seat.
    /// </remarks>
    private async Task PickAsync(params int[] which)
    {
        var free = Page.Locator(".plan__unit[data-state='free']");
        await Expect(free.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        foreach (var index in which)
        {
            var seat = free.Nth(index);
            await seat.ScrollIntoViewIfNeededAsync();
            await ClickUntilAsync(seat, Page.Locator("#picker-bar"));
        }
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
