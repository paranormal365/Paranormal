using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What is served, and what the kitchen has to cook differently (item 235 phase 5).
/// </summary>
/// <remarks>
/// <para><b>Two screens, one fixture</b>, because they are two halves of the same job: the menus
/// page says what is being cooked and the kitchen's sheet says who cannot eat it. Both are read by
/// the same person on the same afternoon.</para>
///
/// <para><b>What is worth asserting.</b> On the menus: that a sitting a host typed is still there
/// after a reload — a replace-the-set save that dropped the dishes would look perfect until
/// somebody came back to it. On the sheet: that two people who typed the same words count as two
/// and two people who typed <i>different</i> words for the same thing stay two lines, because a
/// tally that quietly merged "no nuts" with "nut allergy" is a tally that will one day merge two
/// requirements that are not the same and poison somebody.</para>
///
/// <para><b>It builds its own party</b> through the API rather than hoping the seed has one with
/// allergies in it, and puts the house back afterwards.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventMenusTests : BenTestBase
{
    /// <summary>The seeded weekend in the rooms — two nights, so a sitting has a night to be under.</summary>
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    /// <summary>The seeded 260-seat evening, where a party can be put together with one call.</summary>
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;

    [SetUp]
    public async Task ArrangeAPartyWithAllergies()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        var guest = await SignedInAsync(ClientEmail, ClientPassword);

        await ClearTheHouseAsync(admin);
        await LetGoAsync(guest);
        await ClearTheMenusAsync(admin);

        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/booking-mode",
            new() { DataObject = new { mode = 1 } });
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/publish",
            new() { DataObject = new { } });

        var layout = await admin.GetAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/layout");
        var seats = (await layout.JsonAsync())!.Value.GetProperty("units").EnumerateArray()
            .Take(4).Select(u => u.GetProperty("id").GetString()).ToList();

        var ev = await admin.GetAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}");
        var night = (await ev.JsonAsync())!.Value.GetProperty("nights").EnumerateArray()
            .First().GetProperty("id").GetString();

        // FOUR PEOPLE, FOUR NOTES, AND TWO OF THEM WORD FOR WORD THE SAME. That pair is the tally's
        // whole claim; the "no nuts" / "nut allergy" pair is the claim it does NOT make.
        var held = await guest.PostAsync(
            $"/api/public/hosted-events/{SeatsEventId}/holds",
            new()
            {
                DataObject = new
                {
                    nights = seats.Select(s => new
                    {
                        hostedEventNightId = night,
                        hostedEventLayoutUnitId = s,
                    }).ToArray(),
                    partySize = 4,
                    guests = new[]
                    {
                        new { displayName = "Ada Fielding", dietaryNotes = "no nuts" },
                        new { displayName = "Bertie Fielding", dietaryNotes = "nut allergy" },
                        new { displayName = "Clara Fielding", dietaryNotes = "vegan" },
                        new { displayName = "Dot Fielding", dietaryNotes = "vegan" },
                    },
                },
            });
        Assert.That(held.Status, Is.EqualTo(200),
            $"could not seat a party with allergies: {await held.TextAsync()}");

        await admin.DisposeAsync();
        await guest.DisposeAsync();

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutTheHouseBack()
    {
        var admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await ClearTheHouseAsync(admin);
        await ClearTheMenusAsync(admin);
        await admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    // ── the menus ────────────────────────────────────────────────────────────

    [Test]
    public async Task A_sitting_a_host_typed_is_still_there_after_a_reload()
    {
        // The one thing a replace-the-set save can get wrong invisibly: it looks saved, and the
        // dishes are gone the next time somebody opens it.
        await OpenTheMenusAsync();

        var breakfast = Page.Locator("button[id^='add-breakfast-']").First;
        await ClickUntilAsync(breakfast, Page.Locator("textarea[id^='sitting-dishes-']"));

        await Page.Locator("textarea[id^='sitting-dishes-']").First
            .FillAsync("Starter: Grapefruit\nEggs and bacon\nToast");

        await ClickUntilAsync(Page.Locator("#menus-save"), Page.Locator("#menus-note"));
        await Expect(Page.Locator("#menus-note")).ToContainTextAsync("sitting");

        await OpenTheMenusAsync();

        await Expect(Page.Locator("input[id^='sitting-title-']").First).ToHaveValueAsync("Breakfast");

        // ToHaveValue and not ToContainText: what a host typed into a textarea is its VALUE, and
        // its text content is whatever the markup shipped with — empty here. The first version of
        // this test asserted the text and failed against a page that was perfectly correct.
        //
        // The course written before the colon is asserted too: it survived the round trip as a
        // course rather than becoming part of the dish's name.
        await Expect(Page.Locator("textarea[id^='sitting-dishes-']").First)
            .ToHaveValueAsync(new System.Text.RegularExpressions.Regex(
                "Starter: Grapefruit\\nEggs and bacon\\nToast"));
    }

    [Test]
    public async Task A_night_with_nothing_on_it_says_so_rather_than_looking_broken()
    {
        await OpenTheMenusAsync();

        await Expect(Page.GetByText("Nothing served this").First).ToBeVisibleAsync();
    }

    // ── the kitchen's sheet ──────────────────────────────────────────────────

    [Test]
    public async Task Two_people_who_said_the_same_thing_count_as_two_and_two_wordings_stay_two_lines()
    {
        await OpenTheKitchenAsync();

        // The party is holding, not confirmed, so the default sheet is not about them. Turning the
        // switch on is what a host ordering ahead of an unsettled weekend actually does.
        await Page.Locator("#kitchen-include-unconfirmed").CheckAsync();
        await Expect(Page.Locator("#kitchen-expected")).ToHaveTextAsync("4", new() { Timeout = 20_000 });

        var tally = Page.Locator(".kitchen-tally");
        await Expect(tally).ToContainTextAsync("vegan");

        // Two vegans is one line saying two. Two wordings of a nut allergy is two lines, on
        // purpose: guessing they are one thing is how a tally eventually merges two that are not.
        await Expect(tally.Locator("li", new() { HasTextString = "vegan" })).ToContainTextAsync("2");
        await Expect(tally.Locator("li", new() { HasTextString = "no nuts" })).ToBeVisibleAsync();
        await Expect(tally.Locator("li", new() { HasTextString = "nut allergy" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task The_sheet_says_how_many_people_it_is_out_of_and_how_many_nobody_named()
    {
        // "Four notes" means nothing on its own. The denominator is the point of the three numbers,
        // and the people nobody named are the ones a kitchen gets caught by.
        await OpenTheKitchenAsync();
        await Page.Locator("#kitchen-include-unconfirmed").CheckAsync();

        await Expect(Page.Locator("#kitchen-expected")).ToHaveTextAsync("4", new() { Timeout = 20_000 });
        await Expect(Page.Locator("#kitchen-with-notes")).ToHaveTextAsync("4");
        await Expect(Page.Locator("#kitchen-unnamed")).ToHaveTextAsync("0");
    }

    [Test]
    public async Task One_night_can_be_read_on_its_own_and_printed_for_each()
    {
        await OpenTheKitchenAsync();
        await Page.Locator("#kitchen-include-unconfirmed").CheckAsync();
        await Expect(Page.Locator("#kitchen-expected")).ToHaveTextAsync("4", new() { Timeout = 20_000 });

        // A cook works one service at a time, and the sheet for each of them prints on its own page.
        var everyNight = Page.Locator("#kitchen-every-night");
        await ClickUntilAsync(everyNight, Page.Locator("#kitchen-back-to-one"));
        await Expect(Page.Locator(".kitchen-page").First).ToBeVisibleAsync();
    }

    // ── the three widths ─────────────────────────────────────────────────────

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task Neither_page_slides_sideways_however_narrow_it_is(int width, int height)
    {
        await OpenTheMenusAsync(width, height);
        Assert.That(await SlidesSidewaysAsync(), Is.False, $"the menus slide sideways at {width}px");

        await OpenTheKitchenAsync(width, height);
        Assert.That(await SlidesSidewaysAsync(), Is.False, $"the kitchen slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheMenusAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/menus");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#menus-save")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task OpenTheKitchenAsync(int width = 1280, int height = 800)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/dietary");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#kitchen-scope")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private Task<bool> SlidesSidewaysAsync()
        => Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");

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
            if (booking.GetProperty("status").GetInt32() is not (0 or 1 or 4)) continue;

            var id = booking.GetProperty("id").GetString();
            await admin.PostAsync(
                $"/api/organizations/{_orgId}/events/{SeatsEventId}/bookings/{id}/cancel",
                new() { DataObject = new { decisionNote = "Clearing up after a test." } });
        }
    }

    /// <summary>Leaves the weekend with nothing being served, which is how the seed leaves it.</summary>
    private async Task ClearTheMenusAsync(IAPIRequestContext admin)
        => await admin.PutAsync(
            $"/api/organizations/{_orgId}/events/{RoomsEventId}/menus",
            new() { DataObject = new { menus = Array.Empty<object>() } });

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
