using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// One group asks another for its venue, is told yes, and has the yes taken back (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Two groups, walked in one browser by two people.</b> A group registered fresh for this run
/// (Sarah's) holds a draft weekend at the Thomas House, which the seeded host has been confirmed as
/// running. The organizer's side is walked as Sarah, the venue's as the host's administrator.</para>
///
/// <para><b>What the plan asked to see:</b> the organizer cannot publish until the venue says yes;
/// the yes shows on the event page; taking it back says so on the event page with the venue's
/// reason. The group is purged afterwards, which also walks the purge over a request and a grant on
/// the real database.</para>
/// </remarks>
[TestFixture]
[Category("HostedEvents")]
public class VenueHostingTests : BenTestBase
{
    private const string ThomasHouse = "40000002-0000-0000-0000-000000000001";
    private const string OrgName = "Playwright Venue Guests";

    private IAPIRequestContext _api = null!;
    private string? _orgId;
    private string _eventId = "";
    private string _eventName = "";
    private string _venueOrgId = "";

    [SetUp]
    public async Task AWeekendAtSomebodyElsesHotel()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        _venueOrgId = await OrgIdBySlugAsync("paranormal365");

        var authed = await HeadersAsync(UserEmail, UserPassword);
        if (authed is null) Assert.Ignore("The seeded owner seat cannot sign in on this deployment.");

        var slug = $"pw-venue-{Guid.NewGuid():N}"[..20];
        var register = await _api.PostAsync("/api/security/organizations/register", new()
        {
            Headers = authed,
            DataObject = new { name = OrgName, urlName = slug, kind = 0 },
        });
        Assert.That(register.Ok, Is.True, await register.TextAsync());
        _orgId = (await register.JsonAsync())!.Value.GetProperty("organizationId").GetString();

        _eventName = $"Lock-In {Guid.NewGuid():N}"[..16];
        var start = DateTime.UtcNow.Date.AddDays(45);
        var ev = await _api.PostAsync($"/api/organizations/{_orgId}/events", new()
        {
            Headers = authed,
            DataObject = new
            {
                name = _eventName,
                placeId = ThomasHouse,
                startsOn = start,
                endsOn = start.AddDays(1),
                timeZoneId = "America/Chicago",
                contactLine = "Call the society to settle up.",
                dayPassCapacity = 30,
            },
        });
        Assert.That(ev.Ok, Is.True, await ev.TextAsync());
        _eventId = (await ev.JsonAsync())!.Value.GetProperty("id").GetString()!;
    }

    [TearDown]
    public async Task TakeTheGroupAwayAgain()
    {
        if (_orgId is not null && await HeadersAsync(SuperAdminEmail, SuperAdminPassword) is { } admin)
        {
            var purged = await _api.DeleteAsync($"/api/admin/organizations/{_orgId}/purge",
                new() { Headers = admin, DataObject = new { confirmName = OrgName } });
            Assert.That(purged.Ok, Is.True, $"the purge refused a group with a venue request and grant: {await purged.TextAsync()}");
        }
        await _api.DisposeAsync();
    }

    [Test]
    public async Task The_venue_says_yes_and_then_takes_it_back()
    {
        // ── the organizer asks ───────────────────────────────────────────────
        await LoginAsync(UserEmail, UserPassword);
        await OpenTheEventAsync();

        var onSite = Page.Locator("#event-venue-on-site");
        await Expect(onSite).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(onSite).ToContainTextAsync("runs");

        await Page.Locator("#event-venue-message").FillAsync("Thirty guests, two nights.");
        await ClickUntilAsync(Page.Locator("#event-venue-ask"), Page.Locator("#event-venue-note"));
        await Expect(Page.Locator("#event-venue-note")).ToContainTextAsync("They have been told");
        await Expect(Page.Locator("#event-venue-waiting")).ToContainTextAsync("haven't answered yet");

        // ── the venue answers ────────────────────────────────────────────────
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_venueOrgId}/venue-requests");
        await WaitUntilLoadedAsync();

        var request = Page.Locator(".venue-request", new() { HasTextString = _eventName });
        await Expect(request).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(request).ToContainTextAsync("Thirty guests, two nights.");

        await ClickUntilAsync(request.Locator("button", new() { HasTextString = "Say yes" }), Page.Locator("#venue-note"));
        await Expect(Page.Locator("#venue-note")).ToContainTextAsync("They have been told");

        // ── the organizer sees the yes ───────────────────────────────────────
        await LoginAsync(UserEmail, UserPassword);
        await OpenTheEventAsync();
        await Expect(Page.Locator("#event-venue-yes")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // ── the venue takes it back ──────────────────────────────────────────
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_venueOrgId}/venue-requests");
        await WaitUntilLoadedAsync();

        var grant = Page.Locator(".venue-grant", new() { HasTextString = _eventName });
        await Expect(grant).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await grant.Locator("button", new() { HasTextString = "Withdraw" }).ClickAsync();
        await grant.Locator("input[id^=withdraw-reason]").FillAsync("The roof is being replaced that month.");
        await ClickUntilAsync(grant.Locator("button", new() { HasTextString = "Withdraw your yes" }), Page.Locator("#venue-note"));
        await Expect(Page.Locator("#venue-note")).ToContainTextAsync("Withdrawn");

        // ── and the organizer is told why ────────────────────────────────────
        await LoginAsync(UserEmail, UserPassword);
        await OpenTheEventAsync();
        await Expect(Page.Locator("#event-venue-problem")).ToContainTextAsync("The roof is being replaced that month.",
            new() { Timeout = 30_000 });
    }

    [Test]
    public async Task The_place_names_its_venue_and_the_venue_page_tells_its_story()
    {
        await Page.GotoAsync($"{BaseUrl}/places/{ThomasHouse}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#place-venue-line")).ToContainTextAsync("Run as a venue by", new() { Timeout = 30_000 });

        await Page.Locator("#place-venue-line a", new() { HasTextString = "Venue page" }).ClickAsync();
        await Expect(Page.Locator("#venue-title")).ToContainTextAsync("Thomas House", new() { Timeout = 30_000 });
        await Expect(Page.Locator("#venue-history")).ToContainTextAsync("1890");
    }

    [Test]
    [TestCase(1280, 800)]
    [TestCase(768, 1024)]
    [TestCase(375, 812)]
    public async Task The_requests_page_fits_however_narrow_it_is(int width, int height)
    {
        var authed = await HeadersAsync(UserEmail, UserPassword);
        await _api.PostAsync($"/api/organizations/{_orgId}/events/{_eventId}/venue/ask",
            new() { Headers = authed, DataObject = new { message = "Fits on a phone?" } });

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_venueOrgId}/venue-requests");
        await WaitUntilLoadedAsync();

        var yes = Page.Locator(".venue-request", new() { HasTextString = _eventName })
            .Locator("button", new() { HasTextString = "Say yes" });
        await Expect(yes).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await yes.ScrollIntoViewIfNeededAsync();
        await Expect(yes).ToBeInViewportAsync(new() { Ratio = 1f });

        var slides = await Page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(slides, Is.False, $"the requests page slides sideways at {width}px");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private async Task OpenTheEventAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{_eventId}");
        await WaitUntilLoadedAsync();
        await Page.Locator("#event-venue").ScrollIntoViewIfNeededAsync(new() { Timeout = 30_000 });
    }

    private async Task<Dictionary<string, string>?> HeadersAsync(string email, string password)
    {
        var login = await _api.PostAsync("/login", new() { DataObject = new { email, password } });
        if (!login.Ok) return null;
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" };
    }
}
