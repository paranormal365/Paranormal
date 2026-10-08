using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What the venue remembers: a new plan starts from the one used there before (item 235 phase 12).
/// </summary>
[TestFixture]
[Category("HostedEvents")]
public class VenueMemoryTests : BenTestBase
{
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";
    private string _orgId = string.Empty;
    private string? _draftId;
    private IAPIRequestContext _admin = null!;

    [SetUp]
    public async Task MakeAnEmptyDraftAtTheSameVenue()
    {
        _orgId = await OrgIdBySlugAsync("paranormal365");
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        await api.DisposeAsync();
        _admin = await Playwright.APIRequest.NewContextAsync(new()
        {
            BaseURL = ApiUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
        });

        // A copy with nothing ticked is an empty draft at the same building.
        var copy = await _admin.PostAsync($"/api/organizations/{_orgId}/events/{SeatsEventId}/copy", new()
        {
            DataObject = new
            {
                name = $"Memory {Guid.NewGuid():N}"[..20], startsOn = DateTime.UtcNow.Date.AddYears(2),
                plan = false, menus = false, programme = false, bands = false, helpers = false, adverts = false, files = false,
            },
        });
        Assert.That(copy.Ok, Is.True, await copy.TextAsync());
        _draftId = (await copy.JsonAsync())!.Value.GetProperty("hostedEventId").GetString();
    }

    [TearDown]
    public async Task PutItAway()
    {
        if (_draftId is not null)
            await _admin.PostAsync($"/api/organizations/{_orgId}/events/{_draftId}/archive", new() { DataObject = new { } });
        await _admin.DisposeAsync();
    }

    [Test]
    public async Task An_empty_plan_starts_from_the_one_used_at_this_venue_before()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{_draftId}/layout");
        await WaitUntilLoadedAsync();

        var earlier = Page.Locator("#layout-earlier");
        await Expect(earlier).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Whichever plan is offered first; which one depends on what earlier runs left at this venue.
        var start = earlier.Locator("button").First;
        await ClickUntilAsync(start, Page.Locator("#layout-note"));
        await Expect(Page.Locator("#layout-note")).ToContainTextAsync("Started from");
        await Expect(earlier).ToBeHiddenAsync();

        await ClickUntilAsync(Page.Locator("#plan-save"), Page.Locator("#layout-note", new() { HasTextRegex = new Regex("Saved") }));
        await Expect(Page.Locator("#layout-note")).ToContainTextAsync("on the plan");
    }
}
