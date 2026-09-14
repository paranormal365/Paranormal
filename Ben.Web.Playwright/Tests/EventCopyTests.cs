using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// After the event: starting the next one from it, and taking the bookings away as a spreadsheet
/// (item 235 phase 12).
/// </summary>
[TestFixture]
[Category("HostedEvents")]
public class EventCopyTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private const string SeatsEventId = "40000002-0000-0000-0000-000000000003";

    private IAPIRequestContext _api = null!;
    private string _orgId = string.Empty;
    private string? _madeId;

    [SetUp]
    public async Task SignIn()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _orgId = await OrgIdBySlugAsync("paranormal365");
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
    }

    [TearDown]
    public async Task PutTheDraftAway()
    {
        if (_madeId is not null)
        {
            var login = await _api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
            var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
            await using var admin = await Playwright.APIRequest.NewContextAsync(new()
            {
                BaseURL = ApiUrl,
                ExtraHTTPHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            });
            await admin.PostAsync($"/api/organizations/{_orgId}/events/{_madeId}/archive", new() { DataObject = new { } });
        }

        await _api.DisposeAsync();
    }

    [TestCase(1280, 800)]
    [TestCase(375, 812)]
    public async Task Copying_an_event_makes_a_draft_and_says_what_to_check(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/copy");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#copy-parts")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#copy-venue")).ToBeVisibleAsync();

        // What happened never comes across, and the page says so rather than leaving it to be wondered.
        await Expect(Page.Locator("#copy-parts")).ToContainTextAsync("Bookings, passes, arrivals");

        await Page.Locator("#copy-name").FillAsync($"Copied weekend {Guid.NewGuid():N}"[..30]);
        var go = Page.Locator("#copy-go");
        await go.ScrollIntoViewIfNeededAsync();
        await Expect(go).ToBeInViewportAsync();

        await ClickUntilAsync(go, Page.Locator("#event-copied"));
        await Expect(Page.Locator("#event-copied")).ToContainTextAsync("Copied as a draft");

        var match = System.Text.RegularExpressions.Regex.Match(Page.Url, @"/events/([0-9a-f\-]{36})");
        Assert.That(match.Success, Is.True, Page.Url);
        _madeId = match.Groups[1].Value;
        Assert.That(_madeId, Is.Not.EqualTo(RoomsEventId));

        var wide = await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.That(wide, Is.False, "the page scrolled sideways");
    }

    [Test]
    public async Task The_board_hands_over_every_booking_as_a_spreadsheet()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{SeatsEventId}/bookings");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#board-export")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var download = await Page.RunAndWaitForDownloadAsync(
            () => Page.Locator("#board-export").ClickAsync(), new() { Timeout = 30_000 });

        Assert.That(download.SuggestedFilename, Does.EndWith(".csv"));
        var path = await download.PathAsync();
        var text = await File.ReadAllTextAsync(path!);
        Assert.That(text, Does.StartWith("Status,First name,Last name"));
    }
}
