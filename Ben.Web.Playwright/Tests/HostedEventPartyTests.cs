using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A guest naming each person in their party and saying what they can't eat (10/13/2026).
/// </summary>
/// <remarks>
/// <para><b>The kitchen's sheet, the dining tables and the door all read these notes</b>, and until
/// now only the API and the iPhone app could write them: no form on the website asked, and the help
/// told guests to ask the venue to note it for them. These tests are the website's half.</para>
///
/// <para><b>What is worth asserting.</b> That what a guest types reaches the venue — read back from
/// the venue's own board or the kitchen's sheet, not only from the guest's screen, because a form
/// that shows the guest their own typing proves nothing about whether it was sent. And that
/// changing the names afterwards leaves a confirmed booking confirmed: the places the venue agreed
/// to haven't changed, and a party that lost its "You're coming" for adding an allergy would stop
/// telling the venue about allergies.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class HostedEventPartyTests : BenTestBase
{
    /// <summary>The seeded weekend in the rooms, asked for in words.</summary>
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    /// <summary>The seeded 260-seat evening, picked off the plan.</summary>
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string _roomsUrl = string.Empty;
    private string _seatsUrl = string.Empty;

    [SetUp]
    public async Task OpenBothEvents()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

        await LetGoAsync(guest);
        await ClearTheHouseAsync(admin, SeatsEventId);
        await ClearTheHouseAsync(admin, RoomsEventId);

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

    // ── asking ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Asking_for_a_place_names_each_person_and_what_they_cannot_eat()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await OpenTheAskFormAsync();

        await Page.Locator("#hosted-ask input[type=checkbox]").First.CheckAsync();
        await FillBookingContactAsync("ask");

        // Two people is two rows; the second only exists once the party is two.
        await ClickUntilPartyOfTwoAsync("ask");

        await Page.Locator("#ask-guest-name-0").FillAsync("Ruth Okafor");
        await Page.Locator("#ask-guest-notes-0").FillAsync("vegetarian");
        await Page.Locator("#ask-guest-name-1").FillAsync("Sam Okafor");
        await Page.Locator("#ask-guest-notes-1").FillAsync("no shellfish");
        await Expect(Page.Locator("#ask-party-disclosure")).ToContainTextAsync("never shown publicly");

        await ClickUntilAsync(Page.Locator("#ask-send"), Page.Locator("#hosted-booking"));

        await Expect(Page.Locator("#booking-party-list")).ToContainTextAsync("Sam Okafor");
        await Expect(Page.Locator("#booking-party-list")).ToContainTextAsync("no shellfish");

        // The venue's side, which is the point: the guest's screen showing their own typing
        // proves nothing about whether it was sent.
        var notes = await VenueSeesAsync(RoomsEventId);
        Assert.That(notes, Does.Contain("Ruth Okafor: vegetarian"));
        Assert.That(notes, Does.Contain("Sam Okafor: no shellfish"));
    }

    [Test]
    public async Task A_dietary_note_with_no_name_keeps_the_form_from_sending()
    {
        // The server keeps no row without a name, so a note typed against nobody would be lost
        // without a word. The form says so instead.
        await LoginAsync(ClientEmail, ClientPassword);
        await OpenTheAskFormAsync();

        await Page.Locator("#hosted-ask input[type=checkbox]").First.CheckAsync();
        await FillBookingContactAsync("ask");
        await ClickUntilPartyOfTwoAsync("ask");

        await Page.Locator("#ask-guest-notes-1").FillAsync("nut allergy");

        await Expect(Page.Locator("#ask-party-needs-a-name")).ToBeVisibleAsync();
        await Expect(Page.Locator("#ask-send")).ToBeDisabledAsync();

        await Page.Locator("#ask-guest-name-1").FillAsync("Sam Okafor");
        await Expect(Page.Locator("#ask-party-needs-a-name")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#ask-send")).ToBeEnabledAsync();
    }

    // ── holding ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Holding_seats_names_the_party_and_the_kitchen_sees_it()
    {
        await LoginAsync(ClientEmail, ClientPassword);
        await Page.GotoAsync(_seatsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator(".plan__grid")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Two seats is a party of two, so two rows.
        var free = Page.Locator(".plan__unit[data-state='free']");
        await Expect(free.First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        foreach (var index in new[] { 0, 1 })
        {
            await free.Nth(index).ScrollIntoViewIfNeededAsync();
            await ClickUntilAsync(free.Nth(index), Page.Locator("#picker-bar"));
        }
        await Expect(Page.Locator("#picker-guest-name-1")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await FillBookingContactAsync("picker");
        await Page.Locator("#picker-guest-name-0").FillAsync("Ruth Okafor");
        await Page.Locator("#picker-guest-name-1").FillAsync("Sam Okafor");
        await Page.Locator("#picker-guest-notes-1").FillAsync("no gluten");

        await ClickUntilAsync(Page.Locator("#picker-hold"), Page.Locator("#hosted-booking"));
        await Expect(Page.Locator("#booking-state")).ToContainTextAsync("Held for you");
        await Expect(Page.Locator("#booking-party-list")).ToContainTextAsync("no gluten");

        // The kitchen's own sheet, read as the venue. The party is holding, not confirmed, so the
        // switch a host uses to cook ahead of an unsettled weekend is turned on.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/dietary");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#kitchen-scope")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#kitchen-include-unconfirmed").CheckAsync();

        await Expect(Page.Locator(".kitchen-tally")).ToContainTextAsync("no gluten",
            new() { Timeout = 20_000 });
    }

    // ── changing them afterwards ─────────────────────────────────────────────

    [Test]
    public async Task A_confirmed_party_changes_its_names_from_what_i_am_going_to_and_stays_confirmed()
    {
        await HoldAndConfirmOneSeatAsync();
        await LoginAsync(ClientEmail, ClientPassword);

        await Page.GotoAsync($"{BaseUrl}/my-events");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#my-events")).ToContainTextAsync("You're coming", new() { Timeout = 30_000 });

        // Each booking's fields carry its event's id, so two bookings on the list never share one.
        var mine = $"#my-event-{Guid.Parse(SeatsEventId):N}";
        var row = Page.Locator("#my-events li.my-event", new() { Has = Page.Locator($"{mine}-party-panel") });

        await ClickUntilAsync(Page.Locator($"{mine}-party-change"), Page.Locator($"{mine}-guest-notes-0"));

        await Page.Locator($"{mine}-guest-name-0").FillAsync("Ruth Okafor");
        await Page.Locator($"{mine}-guest-notes-0").FillAsync("vegan");
        await ClickUntilAsync(Page.Locator($"{mine}-party-save"), Page.Locator($"{mine}-party-saved"));

        await Expect(Page.Locator($"{mine}-party-list")).ToContainTextAsync("Ruth Okafor");
        await Expect(Page.Locator($"{mine}-party-list")).ToContainTextAsync("vegan");

        // Names and notes aren't what the venue agreed to, so the yes stands.
        await Expect(row).ToContainTextAsync("You're coming");
        Assert.That(await MyBookingStatusAsync(SeatsEventId), Is.EqualTo(1),
            "adding a dietary note sent a confirmed booking back to the venue");

        // And it was saved, not just drawn.
        await Page.ReloadAsync();
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator($"{mine}-party-list"))
            .ToContainTextAsync("vegan", new() { Timeout = 30_000 });
    }

    [Test]
    public async Task The_pass_changes_them_too_and_stays_a_pass()
    {
        await HoldAndConfirmOneSeatAsync();
        await LoginAsync(ClientEmail, ClientPassword);

        await Page.GotoAsync($"{BaseUrl}/my-events/{SeatsEventId}/pass");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#pass-card")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#pass-party-none")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await ClickUntilAsync(Page.Locator("#pass-party-change"), Page.Locator("#pass-guest-notes-0"));
        await Page.Locator("#pass-guest-name-0").FillAsync("Ruth Okafor");
        await Page.Locator("#pass-guest-notes-0").FillAsync("gluten free");
        await ClickUntilAsync(Page.Locator("#pass-party-save"), Page.Locator("#pass-party-saved"));

        await Expect(Page.Locator("#pass-party-list")).ToContainTextAsync("gluten free");
        await Expect(Page.Locator("#pass-revoked")).ToHaveCountAsync(0);

        var notes = await VenueSeesAsync(SeatsEventId);
        Assert.That(notes, Does.Contain("Ruth Okafor: gluten free"));
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheAskFormAsync()
    {
        await Page.GotoAsync(_roomsUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#hosted-ask")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Sets the ask form's party to two. The box answers on change, so it is left with Tab, and
    /// tried again if the circuit wasn't listening yet.
    /// </summary>
    private async Task ClickUntilPartyOfTwoAsync(string prefix)
    {
        var second = Page.Locator($"#{prefix}-guest-name-1");
        for (var attempt = 0; attempt < 4 && await second.CountAsync() == 0; attempt++)
        {
            await Page.Locator($"#{prefix}-party").FillAsync("2");
            await Page.Locator($"#{prefix}-party").PressAsync("Tab");
            try { await second.WaitForAsync(new() { Timeout = 3_000 }); }
            catch (TimeoutException) { }
        }
        await Expect(second).ToBeVisibleAsync();
    }

    /// <summary>A party of one with a pass, made the way a venue would, with nobody named.</summary>
    private async Task HoldAndConfirmOneSeatAsync()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

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
                    firstName = "Test", lastName = "Guest", phone = "615-555-0100",
                },
            });
        Assert.That(held.Status, Is.EqualTo(200), await held.TextAsync());
        var bookingId = (await held.JsonAsync())!.Value.GetProperty("id").GetString();

        var confirmed = await admin.PostAsync(
            $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{bookingId}/confirm",
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
    }

    /// <summary>
    /// Every "name: note" on the venue's board for an event, among the live bookings.
    /// </summary>
    private async Task<IReadOnlyList<string>> VenueSeesAsync(string eventId)
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var board = await admin.GetAsync($"/api/organizations/{_orgId}/events/{eventId}/bookings");
        Assert.That(board.Ok, Is.True, await board.TextAsync());

        var seen = new List<string>();
        foreach (var booking in (await board.JsonAsync())!.Value.GetProperty("bookings").EnumerateArray())
        {
            if (booking.GetProperty("status").GetInt32() is not (0 or 1 or 4)) continue;
            foreach (var guest in booking.GetProperty("guests").EnumerateArray())
            {
                var notes = guest.GetProperty("dietaryNotes");
                if (notes.ValueKind == System.Text.Json.JsonValueKind.String)
                    seen.Add($"{guest.GetProperty("displayName").GetString()}: {notes.GetString()}");
            }
        }

        await admin.DisposeAsync();
        return seen;
    }

    private async Task<int> MyBookingStatusAsync(string eventId)
    {
        var guest = await SignedInAsync(ClientEmail, ClientPassword);
        var mine = await guest.GetAsync($"/api/public/hosted-events/{eventId}/my-booking");
        Assert.That(mine.Ok, Is.True, await mine.TextAsync());
        var status = (await mine.JsonAsync())!.Value.GetProperty("status").GetInt32();
        await guest.DisposeAsync();
        return status;
    }

    /// <summary>The address on the poster: /o/{org}/events/{slug}.</summary>
    private async Task<string> PublicUrlAsync(IAPIRequestContext admin, string eventId)
    {
        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{eventId}");
        var slug = (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString();
        return $"{BaseUrl}/o/paranormal365/events/{slug}";
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

    /// <summary>Releases every live booking on an event, as the venue would.</summary>
    private async Task ClearTheHouseAsync(IAPIRequestContext admin, string eventId)
    {
        var board = await admin.GetAsync($"/api/organizations/{_orgId}/events/{eventId}/bookings");
        if (!board.Ok) return;

        foreach (var booking in (await board.JsonAsync())!.Value.GetProperty("bookings").EnumerateArray())
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
