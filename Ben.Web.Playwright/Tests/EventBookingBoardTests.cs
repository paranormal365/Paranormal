using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The board a venue actually runs a weekend from (item 235 phase 5).
/// </summary>
/// <remarks>
/// <para><b>What is worth asserting here.</b> Not that a list renders — that the QUEUE comes first
/// and is ordered by what is about to stop being decidable, that a hold says how long is left, and
/// that the whole thing is usable at 375 pixels, because a host answering a request from behind a
/// desk with somebody in front of them is the case this screen exists for.</para>
///
/// <para><b>It builds the party it decides about</b>, through the API, rather than hoping the seed
/// has one in the right state. A fixture that depends on which day the database was built is one
/// that passes today and fails in a fortnight.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventBookingBoardTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;

    [SetUp]
    public async Task ArrangeAPartyWaitingOnTheVenue()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

        // EMPTY THE HOUSE FIRST, AS THE VENUE.
        //
        // A guest can only withdraw what nobody has decided; a CONFIRMED booking is the venue's to
        // release, which is right — they have catered against it. So a run that confirms somebody
        // leaves a seat held, and the next run cannot pick it. The fixture puts the house back the
        // way a host would.
        await ClearTheHouseAsync(admin);
        await LetGoAsync(guest);

        // The evening picks rather than asks, and is on the public site, whatever state a
        // database built on another day left it in.
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
                    partySize = 2,
                    // The organizer has to be able to reach whoever holds (slice 11d).
                    firstName = "Test", lastName = "Guest", phone = "615-555-0100",
                },
            });
        Assert.That(held.Status, Is.EqualTo(200),
            $"could not put a party in the queue: {await held.TextAsync()}");

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

    /// <summary>Releases every live booking on the evening, as the venue would.</summary>
    private async Task ClearTheHouseAsync(IAPIRequestContext admin)
    {
        var board = await admin.GetAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings");
        if (!board.Ok) return;

        foreach (var booking in (await board.JsonAsync())!.Value
                     .GetProperty("bookings").EnumerateArray())
        {
            // Requested 0, Confirmed 1, Held 4 — the three that hold or are waiting.
            var status = booking.GetProperty("status").GetInt32();
            if (status is not (0 or 1 or 4)) continue;

            var id = booking.GetProperty("id").GetString();
            await admin.PostAsync(
                $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{id}/cancel",
                new() { DataObject = new { decisionNote = "Clearing up after a test." } });
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

    private async Task OpenTheBoardAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/bookings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#board-queue")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    // ── the queue ────────────────────────────────────────────────────────────

    [Test]
    public async Task The_board_opens_on_what_is_waiting_rather_than_on_everybody()
    {
        await OpenTheBoardAsync();

        var queue = Page.Locator("#board-queue");
        await Expect(queue).ToContainTextAsync("Waiting on you");

        // Above the fold, and above the list of everybody. A board that opens on a list of two
        // hundred names is one somebody has to search before they can work.
        await Expect(queue).ToBeInViewportAsync();
    }

    [Test]
    public async Task A_party_holding_places_says_how_long_is_left()
    {
        // The whole reason the queue is ordered by deadline: a hold that lapses is a decision the
        // clock took instead of the host, and they should see it coming.
        await OpenTheBoardAsync();

        await Expect(Page.Locator("#board-queue")).ToContainTextAsync("Their hold runs out");
    }

    [Test]
    public async Task A_hold_can_be_given_longer_without_deciding_anything()
    {
        // What a host reaches for when they are not ready. Without it the only choices are
        // confirming a party they have not thought about and letting the clock decide.
        await OpenTheBoardAsync();

        var extend = Page.Locator("#board-queue button", new() { HasTextString = "Give them longer" }).First;
        await Expect(extend).ToBeVisibleAsync();
        await ClickUntilAsync(extend, Page.Locator("#board-note"));

        await Expect(Page.Locator("#board-note")).ToContainTextAsync("longer to hear from you");
    }

    // ── one party ────────────────────────────────────────────────────────────

    [Test]
    public async Task Opening_a_party_asks_where_they_are_actually_staying()
    {
        await OpenTheBoardAsync();

        var confirm = Page.Locator("#board-queue button", new() { HasTextString = "Confirm…" }).First;
        await ClickUntilAsync(confirm, Page.Locator("#sheet-confirm"));

        // The question confirming asks. The pickers start on what the guest chose and the host
        // changes them to wherever suits.
        await Expect(Page.GetByText("Where they are actually staying")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sheet-confirm")).ToBeVisibleAsync();
        await Expect(Page.Locator("#sheet-turn-down")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Confirming_takes_the_party_out_of_the_queue_and_sends_their_pass()
    {
        await OpenTheBoardAsync();

        var confirm = Page.Locator("#board-queue button", new() { HasTextString = "Confirm…" }).First;
        await ClickUntilAsync(confirm, Page.Locator("#sheet-confirm"));

        await Page.Locator("#sheet-confirm").ClickAsync();

        // Either outcome, so a refusal is REPORTED rather than timing out on a note that was never
        // going to appear. A test that says "the element was not found" when the server gave a
        // perfectly good reason is a test that wastes the next hour.
        var note = Page.Locator("#board-note");
        var refused = Page.Locator("#sheet-error");
        await Expect(note.Or(refused).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        if (await refused.CountAsync() > 0)
            Assert.Fail($"confirming was refused: {await refused.InnerTextAsync()}");

        await Expect(Page.Locator("#board-note")).ToContainTextAsync("confirmed");
        await Expect(Page.Locator("#board-note")).ToContainTextAsync("pass");

        // And the queue is empty again, because the decision has been made.
        await Expect(Page.Locator("#board-queue")).ToContainTextAsync("Nothing waiting on you");
    }

    // ── the house ────────────────────────────────────────────────────────────

    [Test]
    public async Task The_house_shows_one_night_and_marks_what_is_only_being_held()
    {
        await OpenTheBoardAsync();

        await Expect(Page.Locator("#board-plan")).ToBeVisibleAsync();
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Hatched rather than solid: "somebody is deciding about this" and "this is settled" are
        // different facts to a host looking at their own house.
        await Expect(Page.Locator(".plan__unit[data-state='pending']").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_seat_the_venue_holds_back_is_drawn_as_not_on_offer()
    {
        // Seeded: two seats behind the pillar, blocked for the whole run.
        await OpenTheBoardAsync();
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await Expect(Page.Locator(".plan__unit[data-state='blocked']").First).ToBeVisibleAsync();
    }

    // ── the three widths ─────────────────────────────────────────────────────

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_page_never_scrolls_sideways_however_narrow_it_is(int width, int height)
    {
        await OpenTheBoardAsync(width, height);

        var overflows = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(overflows, Is.False,
            $"the page slides sideways at {width}px");
    }

    [Test]
    public async Task On_a_phone_the_decision_is_still_the_first_thing_and_still_reachable()
    {
        // A host answering a request from behind a desk with somebody in front of them is the case
        // this screen exists for, and it is rarely happening on a laptop.
        await OpenTheBoardAsync(375, 812);

        await Expect(Page.Locator("#board-queue")).ToBeInViewportAsync();

        var confirm = Page.Locator("#board-queue button", new() { HasTextString = "Confirm…" }).First;
        await Expect(confirm).ToBeInViewportAsync();

        // Big enough to hit without hitting Turn down by mistake.
        var box = await confirm.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Height, Is.GreaterThanOrEqualTo(43.5f),
            "the button that decides somebody's weekend is too small to press safely");
    }
    // ── writing to everybody, and the numbers (phase 17a) ────────────────────

    [Test]
    public async Task Writing_to_the_guests_says_who_it_reaches_before_it_goes_and_keeps_what_was_sent()
    {
        await OpenTheBoardAsync();

        await ClickUntilAsync(Page.Locator("#letter-start"), Page.Locator("#letter-subject"));

        // Only a party holding places so far — none confirmed — so the count moves when the host
        // says to include the people still waiting.
        await Expect(Page.Locator("#letter-audience")).ToContainTextAsync("Nobody matches", new() { Timeout = 15_000 });
        await Page.Locator("#letter-unconfirmed").CheckAsync();
        await Expect(Page.Locator("#letter-audience")).ToContainTextAsync("1 party", new() { Timeout = 15_000 });

        var subject = $"Doors at eight ({Guid.NewGuid():N})"[..30];
        await Page.Locator("#letter-subject").FillAsync(subject);
        await Page.Locator("#letter-body").FillAsync("Come to the side door on Main Street.\nBring a coat.");

        await ClickUntilAsync(Page.Locator("#letter-review"), Page.Locator("#letter-confirm"));
        await Expect(Page.Locator("#letter-confirm")).ToContainTextAsync("1 party");
        await Page.Locator("#letter-send").ClickAsync();

        var sent = Page.Locator("#letter-note");
        var refused = Page.Locator("#letter-error");
        await Expect(sent.Or(refused).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        if (await refused.CountAsync() > 0)
        {
            // The ten-a-day limit is per event, and this database is shared by every run today.
            // Reaching it is the rule working, and it must be said in words, not as a dead button.
            await Expect(refused).ToContainTextAsync("in the last day");
            return;
        }

        await Expect(sent).ToContainTextAsync("1 party");
        await Expect(Page.Locator("#letters-sent")).ToContainTextAsync(subject);
    }

    [Test]
    public async Task The_event_page_shows_the_numbers_at_a_glance()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}");
        await WaitUntilLoadedAsync();

        var glance = Page.Locator("#event-glance");
        await Expect(glance).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#glance-waiting")).ToContainTextAsync("1 is holding places");
        await Expect(Page.Locator("#glance-places")).ToContainTextAsync("places left of");
        await Expect(Page.Locator("#glance-waiting")).ToHaveAttributeAsync("href", $"/organizations/{_orgId}/events/{SeatsEventId}/bookings");
    }

    [Test]
    public async Task The_public_page_says_how_to_get_in_and_around()
    {
        // Seeded on the evening (phase 17a): what a guest in a wheelchair needs before they book.
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/an-evening-of-evidence");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#event-access-notes")).ToContainTextAsync("step-free", new() { Timeout = 30_000 });
    }
}
