using System.Text.Json;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Jim's overnight conference, walked on the website (event-scenario-gaps, 10/21/2026): his list of
/// people and the tickets he sends, selling at the door with a QR code, starting the sessions, the
/// evidence window and the results letter, the evidence on the event page, and the menu's credit.
/// </summary>
/// <remarks>
/// <para>On the seeded séance weekend (an Ask event). Everything a test turns on it turns off again: the
/// door's ticket code, the invitations it sent (they hold nothing), and the request a guest makes at the
/// door is withdrawn by the guest.</para>
///
/// <para>Each new piece is looked at on every screen type (<see cref="Oct16TestBase.Widths"/>) in both
/// themes, and the controls a finger hits on the door are checked as touch targets.</para>
/// </remarks>
[TestFixture]
[Category("EventScenario")]
[Category("HostedEvents")]
[NonParallelizable]
public class EventScenarioTests : Oct16TestBase
{
    private IAPIRequestContext _admin = null!;
    private string _orgId = "";

    [SetUp]
    public async Task SignIn()
    {
        _orgId = await OrgIdBySlugAsync(GroupSlug);
        _admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task StopSellingAtTheDoor()
        => await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/door-tickets",
                                 new() { DataObject = new { on = false } });

    private string EventPage(string tail = "") => $"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}{tail}";

    // ── Jim's list, and a ticket he sends ────────────────────────────────────

    [Test]
    public async Task A_pasted_list_is_invited_and_the_typos_are_handed_back()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync(EventPage("/bookings"));
        await Page.Locator("#board-invite-guest").ClickAsync(new() { Timeout = 30_000 });

        var stamp = Guid.NewGuid().ToString("N")[..8];
        await Page.Locator("#invite-guest-email").FillAsync($"scenario-a-{stamp}@example.test, scenario-b-{stamp}@example.test\nnot-an-address");
        await Page.Locator("#invite-guest-message").FillAsync("Join us overnight. $45, paid to me before you come.");
        await Page.Locator("#invite-guest-send").ClickAsync();

        var sent = Page.Locator("[data-testid=invite-guest-sent]");
        await Expect(sent).ToContainTextAsync("Sent to 2 people.", new() { Timeout = 30_000 });
        await Expect(sent).ToContainTextAsync("Not email addresses, so skipped: not-an-address.");
        await Expect(sent).ToContainTextAsync("Nothing is held until they click.");
        // The typo stays in the box to fix and send.
        await Expect(Page.Locator("#invite-guest-email")).ToHaveValueAsync("not-an-address");
    }

    [Test]
    public async Task Sending_a_ticket_says_it_confirms_when_opened()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync(EventPage("/bookings"));
        await Page.Locator("#board-book-on-behalf").ClickAsync(new() { Timeout = 30_000 });

        await Page.Locator("#behalf-email").FillAsync($"scenario-ticket-{Guid.NewGuid():N}@example.test");
        await Page.Locator("#behalf-note").FillAsync("Paid by phone, thank you.");
        await Page.Locator("#behalf-create").ClickAsync();

        // It used to be refused every time ("Choose who this booking is for…").
        await Expect(Page.Locator("[data-testid=behalf-error]")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid=behalf-sent]")).ToContainTextAsync("confirmed, when they open it", new() { Timeout = 30_000 });
    }

    // ── tickets at the door ──────────────────────────────────────────────────

    /// <summary>Turns the door's code on, or says why the seeded event can't sell at the door.</summary>
    private async Task<(string Url, string Token)> DoorSellingAsync()
    {
        var on = await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/door-tickets",
                                       new() { DataObject = new { on = true } });
        if (on.Status == 400 && (await on.TextAsync()).Contains("sells no day passes"))
            Assert.Ignore("The seeded weekend sells no day passes, so there is nothing to sell at the door.");
        var json = await OkJsonAsync(on, "turning on tickets at the door");
        var url = json.GetProperty("url").GetString()!;
        return (url, url[(url.LastIndexOf('/') + 1)..]);
    }

    [Test]
    public async Task The_door_gets_a_qr_code_to_print_and_a_poster_that_fits_every_screen()
    {
        await DoorSellingAsync();
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var problems = new List<string>();

        foreach (var theme in new[] { "light", "dark" })
        {
            await UseThemeAsync(theme);
            foreach (var (width, height) in Widths)
            {
                await Page.SetViewportSizeAsync(width, height);

                await Page.GotoAsync(EventPage("/door"));
                var card = Page.Locator("#door-tickets");
                problems.AddRange(await LookAtAsync(card, "scenario-door-tickets", theme));
                await Expect(card.Locator(".qr-plate")).ToBeVisibleAsync(new() { Timeout = 15_000 });

                await Page.GotoAsync(EventPage("/door-tickets/poster"));
                problems.AddRange(await LookAtAsync(Page.Locator("#door-poster"), "scenario-door-poster", theme));
            }
        }

        NothingWrong(problems);
    }

    [Test]
    public async Task Somebody_at_the_door_asks_on_their_phone_and_the_door_confirms_them_once_paid()
    {
        var (url, token) = await DoorSellingAsync();
        var guest = await ApiAsAsync(ClientEmail, ClientPassword);
        // A guest who already has a place here is told so instead; start from none.
        await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");

        await LoginAsync(ClientEmail, ClientPassword);
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync(url);
        await Expect(Page.Locator("#door-ticket")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#door-ticket-price")).ToContainTextAsync("Nothing is paid through this site");

        var problems = new List<string>();
        problems.AddRange(await TouchTargetAsync(Page.Locator("#door-ticket-ask"), "door-ticket-ask", "light"));
        await Page.Locator("#door-ticket-ask").ClickAsync();
        await Expect(Page.Locator("#door-ticket-asked")).ToContainTextAsync("Show this screen at the door", new() { Timeout = 30_000 });

        // The door sees them waiting, and confirms them once they have paid.
        var waiting = await OkJsonAsync(await _admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/door-tickets"), "reading the door");
        var request = waiting.GetProperty("requests").EnumerateArray().Last(r => !r.GetProperty("isConfirmed").GetBoolean());
        var bookingId = request.GetProperty("bookingId").GetString();

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync(EventPage("/door"));
        var confirm = Page.Locator($"#door-ticket-confirm-{bookingId}");
        problems.AddRange(await TouchTargetAsync(confirm, "door-ticket-confirm", "light"));
        await confirm.ClickAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#door-tickets-note")).ToContainTextAsync("is confirmed", new() { Timeout = 30_000 });

        // Their phone now shows a place.
        var seen = await OkJsonAsync(await guest.GetAsync($"/api/public/door-tickets/{token}"), "reading the door's code as the guest");
        Assert.That(IsEnum(seen.GetProperty("myStatus"), 1, "Confirmed"), Is.True, $"The guest's place reads {seen.GetProperty("myStatus")}.");

        await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
        NothingWrong(problems);
    }

    // ── time to investigate ──────────────────────────────────────────────────

    [Test]
    public async Task Starting_the_sessions_from_the_website_always_says_what_happened()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync(EventPage("/door"));
        await Page.Locator("#start-sessions-button").ClickAsync(new() { Timeout = 30_000 });

        // Sent, or refused in the server's words ("It can be launched from three hours before it
        // starts."): never nothing.
        var said = Page.Locator("#start-sessions-said, #start-sessions-error");
        await Expect(said).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That((await said.InnerTextAsync()).Trim(), Is.Not.Empty);
    }

    // ── after the event ──────────────────────────────────────────────────────

    [Test]
    public async Task The_after_page_offers_the_window_the_missed_you_letter_and_the_results_on_every_screen()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var problems = new List<string>();

        foreach (var theme in new[] { "light", "dark" })
        {
            await UseThemeAsync(theme);
            foreach (var (width, height) in Widths)
            {
                await Page.SetViewportSizeAsync(width, height);
                await Page.GotoAsync(EventPage("/after"));
                await Expect(Page.Locator("#after-send-missed")).ToBeVisibleAsync(new() { Timeout = 30_000 });
                problems.AddRange(await LookAtAsync(Page.Locator("#after-thank-you"), "scenario-after-letters", theme));
                problems.AddRange(await LookAtAsync(Page.Locator("#after-results"), "scenario-after-results", theme));
            }
        }

        // The results letter can't go before the event is over, and the page says so rather than greying out silently.
        var send = Page.Locator("#after-send-results");
        if (await send.IsDisabledAsync())
            await Expect(Page.Locator("#after-results-wait")).ToContainTextAsync("once the event is over");

        NothingWrong(problems);
    }

    [Test]
    public async Task A_results_address_that_is_not_a_page_here_or_https_is_refused_in_words()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync(EventPage("/after"));
        await Expect(Page.Locator("#after-results-url")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Page.Locator("#after-results-url").FillAsync("http://not-secure.example/results");
        await Page.Locator("#after-save").ClickAsync();
        await Expect(Page.Locator("#after-error")).ToContainTextAsync("starting with https://", new() { Timeout = 30_000 });

        await Page.Locator("#after-results-url").FillAsync("");
        await Page.Locator("#after-save").ClickAsync();
        await Expect(Page.Locator("#after-note")).ToContainTextAsync("Saved.", new() { Timeout = 30_000 });
    }

    // ── the guests' side ─────────────────────────────────────────────────────

    [Test]
    public async Task The_event_page_has_the_place_the_letters_link_to()
    {
        var ev = await OkJsonAsync(await _admin.GetAsync($"/api/public/hosted-events/{RoomsEventId}"), "reading the event");
        await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{ev.GetProperty("urlName").GetString()}#event-evidence");
        await Expect(Page.Locator("#event-evidence")).ToHaveCountAsync(1, new() { Timeout = 30_000 });
    }

    [Test]
    public async Task A_published_menu_ends_with_the_small_credit_and_the_site_mark()
    {
        var ev = await OkJsonAsync(await _admin.GetAsync($"/api/public/hosted-events/{RoomsEventId}"), "reading the event");
        var menus = await OkJsonAsync(await _admin.GetAsync($"/api/public/hosted-events/{RoomsEventId}/published-menus"), "reading the menus");
        if (menus.GetArrayLength() == 0) Assert.Ignore("The seeded weekend has no published menu today.");

        await Page.GotoAsync($"{BaseUrl}/o/{GroupSlug}/events/{ev.GetProperty("urlName").GetString()}");
        var credit = Page.Locator(".menu-doc__credit").First;
        await Expect(credit).ToContainTextAsync("ISHAUNTED LLC", new() { Timeout = 30_000 });
        await Expect(credit.Locator(".menu-doc__credit-mark")).ToBeVisibleAsync();

        // The planner's own title stays the planner's.
        foreach (var menu in menus.EnumerateArray())
            Assert.That(menu.GetProperty("title").GetString(), Is.EqualTo(menu.GetProperty("shownHeading").GetString()));
    }
}
