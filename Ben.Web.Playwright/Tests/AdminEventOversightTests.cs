using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The SuperAdmin's oversight of hosted events, end to end (item 235 phase 17b): the list, removing an event, the
/// organizer's appeal from the event's page, the answer, and the events dashboard.
/// </summary>
/// <remarks>
/// <b>It removes an event of its own.</b> A draft made through the API for this run, never a seeded one, which other
/// fixtures book against. It is archived again at the end.
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class AdminEventOversightTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";

    private IAPIRequestContext _api = null!;
    private IAPIRequestContext _admin = null!;
    private string _orgId = string.Empty;
    private string _eventId = string.Empty;
    private string _eventName = string.Empty;

    [SetUp]
    public async Task MakeADraftToRemove()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");

        var login = await _api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        Assert.That(login.Ok, Is.True, await login.TextAsync());
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        _admin = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        var seeded = (await (await _admin.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}")).JsonAsync())!.Value;
        _eventName = $"Oversight check {Guid.NewGuid():N}"[..28];
        var starts = DateTime.UtcNow.Date.AddDays(60);
        var made = await _admin.PostAsync($"/api/organizations/{_orgId}/events", new()
        {
            DataObject = new
            {
                name = _eventName,
                placeId = seeded.GetProperty("placeId").GetString(),
                timeZoneId = "America/Chicago",
                startsOn = starts.ToString("yyyy-MM-dd"),
                endsOn = starts.ToString("yyyy-MM-dd"),
                contactLine = "Call us.",
            },
        });
        Assert.That(made.Ok, Is.True, $"could not make a draft: {await made.TextAsync()}");
        _eventId = (await made.JsonAsync())!.Value.GetProperty("id").GetString()!;

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutItAway()
    {
        if (_eventId.Length > 0)
        {
            // Bring it back if the test left it removed, then archive it so it is off every list.
            var list = (await (await _admin.GetAsync("/api/admin/hosted-events")).JsonAsync())!.Value;
            foreach (var appeal in list.GetProperty("appeals").EnumerateArray())
                if (appeal.GetProperty("hostedEventId").GetString() == _eventId && appeal.GetProperty("appealState").GetInt32() == 1)
                    await _admin.PostAsync($"/api/admin/hosted-events/appeals/{appeal.GetProperty("removalId").GetString()}/decide",
                        new() { DataObject = new { uphold = true, note = "Tidying up after a test." } });
            await _admin.PostAsync($"/api/organizations/{_orgId}/events/{_eventId}/archive", new() { DataObject = new { } });
        }
        await _admin.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Test]
    public async Task A_SuperAdmin_removes_an_event_the_organizer_appeals_and_the_appeal_brings_it_back_as_a_draft()
    {
        // The list, searched, with the remove icon on the row.
        await Page.GotoAsync($"{BaseUrl}/admin/events");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#appeals")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#events-search").FillAsync(_eventName);
        await Expect(Page.Locator(".admin-events-grid")).ToContainTextAsync(_eventName);
        await ClickUntilAsync(Page.Locator($"#remove-{_eventId}"), Page.Locator("#remove-effect"));

        // What it will do, said before it is done.
        await Expect(Page.Locator("#remove-effect")).ToContainTextAsync("No event credit was spent on it");
        await Expect(Page.Locator("#remove-effect")).ToContainTextAsync("Nobody has a place");
        await Page.Locator("#remove-note").FillAsync("Checked by the oversight test.");
        await Page.Locator("#remove-confirm").ClickAsync();
        await Expect(Page.Locator("#remove-done")).ToContainTextAsync("is removed", new() { Timeout = 20_000 });

        // The organizer's side: the event's page opens on the removal, with the appeal.
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{_eventId}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#event-removed")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#appeal-message").FillAsync("We have the owner's written permission for this night.");
        await Page.Locator("#appeal-send").ClickAsync();
        await Expect(Page.Locator("#appeal-waiting")).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Back on the list, the appeal waits at the top and is upheld.
        await Page.GotoAsync($"{BaseUrl}/admin/events");
        await WaitUntilLoadedAsync();
        var appeal = Page.Locator("#appeals li", new() { HasTextString = _eventName });
        await Expect(appeal).ToContainTextAsync("owner's written permission", new() { Timeout = 30_000 });
        await ClickUntilAsync(appeal.Locator("button", new() { HasTextString = "Uphold" }), Page.Locator("#events-note"));
        await Expect(Page.Locator("#events-note")).ToContainTextAsync("back as a draft");
    }

    [Test]
    public async Task The_dashboard_has_an_events_tab_with_its_numbers()
    {
        // Straight to the tab first: it renders before sign-in has resolved, and once remembered a refusal as its answer.
        await Page.GotoAsync($"{BaseUrl}/admin/dashboard?tab=events");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#events-dashboard")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Page.GotoAsync($"{BaseUrl}/admin/dashboard");
        await WaitUntilLoadedAsync();

        await ClickUntilAsync(Page.Locator("#dashboard-tab-events"), Page.Locator("#events-dashboard"));
        await Expect(Page.Locator("#events-dashboard")).ToContainTextAsync("Events on the site");
        await Expect(Page.Locator("#events-dashboard")).ToContainTextAsync("Appeals waiting");
        Assert.That(Page.Url, Does.Contain("tab=events"));

        await ClickUntilAsync(Page.Locator("#dashboard-tab-site"), Page.GetByText("Sign-ins and registrations"));
    }
}
