using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Seating confirmed parties at tables, sitting by sitting (item 235 phase 13).
/// </summary>
/// <remarks>Against the seeded rooms weekend. The tables this run makes, and the booking it may make, are removed.</remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventDiningTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private string _orgId = "";
    private IAPIRequestContext _admin = null!;
    private string? _madeBookingId;

    [SetUp]
    public async Task EnsureAPartyAndNoTables()
    {
        _orgId = await OrgIdBySlugAsync("paranormal365");
        _admin = await SignedInAsync(SuperAdminEmail, SuperAdminPassword);
        await using var guest = await SignedInAsync(ClientEmail, ClientPassword);

        var mine = await guest.GetAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
        var confirmed = mine.Ok && (await mine.TextAsync()).Length > 2 && (await mine.JsonAsync())!.Value.GetProperty("status").GetInt32() == 1;
        if (!confirmed)
        {
            if (mine.Ok && (await mine.TextAsync()).Length > 2)
                await guest.DeleteAsync($"/api/public/hosted-events/{RoomsEventId}/my-booking");
            var me = (await (await guest.GetAsync("/api/me")).JsonAsync())!.Value;
            var guestId = me.TryGetProperty("userId", out var uid) ? uid.GetString() : me.GetProperty("id").GetString();
            var made = await _admin.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/on-behalf",
                new() { DataObject = new { leadAppUserId = guestId, kind = 1, partySize = 2, confirmImmediately = true } });
            Assert.That(made.Ok, Is.True, await made.TextAsync());
            _madeBookingId = (await made.JsonAsync())!.Value.GetProperty("id").GetString();
        }

        await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/dining/tables", new() { DataObject = new { tables = Array.Empty<object>() } });
    }

    [TearDown]
    public async Task PutItBack()
    {
        await _admin.PutAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/dining/tables", new() { DataObject = new { tables = Array.Empty<object>() } });
        if (_madeBookingId is not null)
            await _admin.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/bookings/{_madeBookingId}/cancel",
                new() { DataObject = new { decisionNote = "Clearing up after a test." } });
        await _admin.DisposeAsync();
    }

    [TestCase(1280, 800)]
    [TestCase(375, 812)]
    public async Task A_party_tapped_then_a_table_tapped_is_seated_there(int width, int height)
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/dining");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#dining-tables-editor, #dining-no-sittings").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await Page.Locator("#dining-no-sittings").CountAsync() > 0)
            Assert.Ignore("The seeded weekend has no menus on this database.");

        await Page.Locator("#dining-add-six").ClickAsync();
        await ClickUntilAsync(Page.Locator("#dining-save-tables"), Page.Locator("#dining-tables"));

        var party = Page.Locator(".dining-party:not([disabled])").First;
        await Expect(party).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ClickUntilAsync(party, Page.Locator("#dining-choosing"));

        var table = Page.Locator(".dining-table").First;
        await table.ScrollIntoViewIfNeededAsync();
        await ClickUntilAsync(table.Locator(".card-header"), Page.Locator("#dining-note"));
        await Expect(Page.Locator("#dining-note")).ToContainTextAsync("Table 1");
        await Expect(table.Locator(".card-header .badge")).Not.ToHaveTextAsync("0/8");

        var wide = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.That(wide, Is.False, "the page scrolled sideways");
    }

    private async Task<IAPIRequestContext> SignedInAsync(string email, string password)
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email, password } });
        Assert.That(login.Ok, Is.True, $"{email} could not sign in");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });
    }
}
