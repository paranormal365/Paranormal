using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A guest getting a place at a hosted event (item 235 phase 6).
/// </summary>
/// <remarks>
/// <para><b>The public page is where this feature meets somebody who has never heard of it.</b>
/// Everything before now was the venue's side; this is the half that decides whether a weekend
/// sells, and the states it has to tell apart — asked for, held with a clock on it, confirmed,
/// turned down — are the difference between turning up and being turned away at a door.</para>
///
/// <para><b>Signed out is a case and not an error.</b> Somebody deciding whether to come is
/// exactly the person with no account: they see the house, they cannot touch it, and they are
/// told which of those two facts is which.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventGuestTests : BenTestBase
{
    /// <summary>The seeded weekend in the rooms — an Ask event, where places are asked for.</summary>
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    /// <summary>The seeded 260-seat evening — a Pick event, where they are chosen off a plan.</summary>
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _roomsUrl = string.Empty;
    private string _seatsUrl = string.Empty;

    [SetUp]
    public async Task PutBothEventsOnTheSite()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

        await LetGoAsync(guest);
        await ClearTheHouseAsync(admin, SeatsEventId);
        await ClearTheHouseAsync(admin, RoomsEventId);

        // Whatever a database built on another day — or an earlier run of this fixture — left
        // behind: the weekend asks, the evening picks, and both are on the public site.
        await PutTheWeekendBackAsync(admin);
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/booking-mode",
            new() { DataObject = new { mode = 0 } });
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });

        foreach (var id in new[] { RoomsEventId, SeatsEventId })
        {
            var published = await admin.PostAsync(
                $"/api/organizations/{_orgId}/events/{id}/publish", new() { DataObject = new { } });
            Assert.That(published.Ok, Is.True,
                $"the event could not be put on the site: {await published.TextAsync()}");
        }

        _roomsUrl = await PublicUrlAsync(admin, RoomsEventId);
        _seatsUrl = await PublicUrlAsync(admin, SeatsEventId);

        await admin.DisposeAsync();
        await guest.DisposeAsync();
    }

    [TearDown]
    public async Task PutTheHouseBack()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheHouseAsync(admin, SeatsEventId);
        await ClearTheHouseAsync(admin, RoomsEventId);
        await admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    // ── signed out ───────────────────────────────────────────────────────────

    [Test]
    public async Task A_stranger_can_choose_seats_and_is_asked_who_they_are_before_anything_is_sent()
    {
        // The case this endpoint is anonymous for. Since slice 11d a stranger may pick too: the
        // places wait fifteen minutes for an emailed link, so the button says it sends one, and
        // the organizer's three questions come with the sentence saying why.
        await Page.GotoAsync(_seatsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("#hosted-places")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var free = Page.Locator(".plan__unit[data-state='free']");
        await Expect(free.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await ClickUntilAsync(free.First, Page.Locator("#picker-contact"));

        await Expect(Page.Locator("#picker-email")).ToBeVisibleAsync();
        await Expect(Page.Locator("#picker-disclosure")).ToContainTextAsync("for this event only");
        await Expect(Page.Locator("#picker-hold")).ToContainTextAsync("Email me a link");
        await Expect(Page.Locator("#picker-hold")).ToBeDisabledAsync();
    }

    [Test]
    public async Task A_weekend_whose_bookings_have_shut_says_so_rather_than_showing_a_dead_form()
    {
        // Bookings closed, called off and "this has happened" are three different facts, and a
        // page with no button and no sentence is one somebody keeps refreshing. Which sentence
        // belongs to which state is settled by PublicHostedEventPlanTests; what is proved here is
        // that the page reaches for it at all.
        //
        // It shuts the weekend and SetUp opens it again, which is why the restore writes the
        // seeder's whole record rather than one field: this door is a full upsert, so a partial
        // write is a partial event.
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}",
            new() { DataObject = TheSeededWeekend(bookingsCloseAtUtc: DateTime.UtcNow.AddDays(-1)) });

        await Page.GotoAsync(_roomsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("#hosted-closed")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#hosted-closed")).ToContainTextAsync("closed");

        await PutTheWeekendBackAsync(admin);
        await admin.DisposeAsync();
    }

    // ── asking, as a signed-in guest ─────────────────────────────────────────

    [Test]
    public async Task Asking_for_a_room_says_plainly_that_nothing_is_held_yet()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync(_roomsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#hosted-ask")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#hosted-ask")).ToContainTextAsync("Nothing is held");

        // Two of three nights is an ordinary booking, so the nights are ticks rather than a range.
        var firstNight = Page.Locator("#hosted-ask input[type=checkbox]").First;
        await firstNight.CheckAsync();

        await FillBookingContactAsync("ask");
        await ClickUntilAsync(Page.Locator("#ask-send"), Page.Locator("#hosted-booking"));

        await Expect(Page.Locator("#booking-state")).ToContainTextAsync("Asked for");
        await Expect(Page.Locator("#hosted-booking")).ToContainTextAsync("Nothing is held until");
    }

    [Test]
    public async Task A_request_can_be_taken_back_and_the_form_comes_back_with_it()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await AskForARoomAsync();

        await ClickUntilAsync(Page.Locator("#booking-let-go"), Page.Locator("#booking-note"));
        await Expect(Page.Locator("#booking-note")).ToContainTextAsync("let go");
    }

    // ── the three widths ─────────────────────────────────────────────────────

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_page_never_slides_sideways_however_narrow_it_is(int width, int height)
    {
        // A four-hundred-seat house is wider than a phone. The GRID takes that scrolling, in its
        // own container; the page must not.
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(_seatsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator("#hosted-places")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the page slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task AskForARoomAsync()
    {
        await Page.GotoAsync(_roomsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#hosted-ask")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Page.Locator("#hosted-ask input[type=checkbox]").First.CheckAsync();
        await FillBookingContactAsync("ask");
        await ClickUntilAsync(Page.Locator("#ask-send"), Page.Locator("#hosted-booking"));
    }

    /// <summary>The address on the poster: /o/{org}/events/{slug}.</summary>
    private async Task<string> PublicUrlAsync(IAPIRequestContext admin, string eventId)
    {
        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{eventId}");
        var json = (await ev.JsonAsync())!.Value;
        var slug = json.GetProperty("urlName").GetString();

        return $"{BaseUrl}/o/paranormal365/events/{slug}";
    }

    /// <summary>
    /// The séance weekend exactly as <c>HostedEventDemoSeeder</c> writes it.
    /// </summary>
    /// <remarks>
    /// <para><b>Written out in full because this door is a full upsert.</b> The first version of
    /// the closed-bookings test sent four fields and a date, which silently emptied the tagline,
    /// the description, the day-pass price and the booking mode — and left them empty for every
    /// test that ran afterwards, in a database the harness reuses. A fixture that mutates seeded
    /// data has to be able to put it back exactly.</para>
    ///
    /// <para>The dates are read from the event rather than computed: the seeder places the weekend
    /// relative to the day the database was built, and guessing would move it.</para>
    /// </remarks>
    private object TheSeededWeekend(DateTime? bookingsCloseAtUtc = null) => new
    {
        name = "Thomas House Séance Weekend",
        urlName = "thomas-house-seance-weekend",
        placeId = _roomsPlaceId,
        timeZoneId = "America/Chicago",
        startsOn = _roomsStartsOn,
        endsOn = _roomsEndsOn,
        tagline = "Two nights in the most haunted hotel in Tennessee.",
        description =
            "Stay the weekend, hunt both nights, and eat with us in between. Rooms are "
            + "limited and go by request.",
        defaultStartLocal = "19:00:00",
        defaultEndLocal = "02:00:00",
        dayPassCapacity = 20,
        dayPassPrice = 45m,
        contactLine = "Call the hotel on (615) 555-0142 to settle up.",
        bookingsCloseAtUtc,
        bookingMode = 0,
    };

    private string _roomsPlaceId = string.Empty;
    private string _roomsStartsOn = string.Empty;
    private string _roomsEndsOn = string.Empty;

    /// <summary>Restores the weekend, whatever an earlier test or an earlier run did to it.</summary>
    private async Task PutTheWeekendBackAsync(IAPIRequestContext admin)
    {
        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}");
        Assert.That(ev.Ok, Is.True, await ev.TextAsync());

        var json = (await ev.JsonAsync())!.Value;
        _roomsPlaceId = json.GetProperty("placeId").GetString()!;
        _roomsStartsOn = json.GetProperty("startsOn").GetString()!;
        _roomsEndsOn = json.GetProperty("endsOn").GetString()!;

        var put = await admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}",
            new() { DataObject = TheSeededWeekend() });
        Assert.That(put.Ok, Is.True, $"the weekend could not be restored: {await put.TextAsync()}");
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

    private async Task ClearTheHouseAsync(IAPIRequestContext admin, string eventId)
    {
        var board = await admin.GetAsync($"/api/organizations/{_orgId}/events/{eventId}/bookings");
        if (!board.Ok) return;

        foreach (var booking in (await board.JsonAsync())!.Value
                     .GetProperty("bookings").EnumerateArray())
        {
            // Requested 0, Confirmed 1, Held 4 — the three that hold or are waiting.
            if (booking.GetProperty("status").GetInt32() is not (0 or 1 or 4)) continue;

            var id = booking.GetProperty("id").GetString();
            await admin.PostAsync(
                $"/api/organizations/{_orgId}/events/{eventId}/bookings/{id}/cancel",
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
