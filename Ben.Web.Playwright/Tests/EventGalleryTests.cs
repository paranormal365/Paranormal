using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The dressed event page: the host's gallery, the countdown, and the phone's ask button (item 235 phase 11).
/// </summary>
/// <remarks>Against the seeded rooms weekend; the picture this run adds is removed at the end.</remarks>
[TestFixture]
[Category("HostedEvents")]
public class EventGalleryTests : BenTestBase
{
    private const string RoomsEventId = "40000002-0000-0000-0000-000000000002";
    private string _orgId = "";
    private readonly string _caption = $"pw-gallery-{Guid.NewGuid():N}"[..19];

    [SetUp]
    public async Task Start()
    {
        _orgId = await OrgIdBySlugAsync("paranormal365");

        // The seed leaves this event in Draft on purpose, and the public endpoints below
        // answer only for a published one. This fixture used to inherit a publish from
        // whichever earlier run happened to do it (item 243); it does its own now.
        await PublishSeededEventAsync(_orgId, RoomsEventId);
    }

    [TearDown]
    public async Task RemoveThePicture()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var login = await api.PostAsync("/login", new() { DataObject = new { email = SuperAdminEmail, password = SuperAdminPassword } });
        if (!login.Ok) return;
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {(await login.JsonAsync())!.Value.GetProperty("accessToken").GetString()}" };
        var list = await api.GetAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/gallery", new() { Headers = headers });
        if (!list.Ok) return;
        foreach (var image in (await list.JsonAsync())!.Value.EnumerateArray())
            if (image.TryGetProperty("caption", out var c) && c.GetString() == _caption)
                await api.DeleteAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/gallery/{image.GetProperty("id").GetString()}", new() { Headers = headers });
    }

    [Test]
    public async Task A_picture_added_by_the_host_leads_the_event_page()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}/events/{RoomsEventId}/gallery");
        await WaitUntilLoadedAsync();

        await Expect(Page.Locator("#gallery-input")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.Locator("#gallery-input").SetInputFilesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "room-photo-1.jpg"));
        await Page.Locator("#gallery-caption").FillAsync(_caption);
        await ClickUntilAsync(Page.Locator("#gallery-add"), Page.Locator("#gallery-note"));
        await Expect(Page.Locator("#gallery-note")).ToContainTextAsync("Added");
        await Expect(Page.Locator(".gallery-image").First).ToBeVisibleAsync();

        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{await SlugAsync()}");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("#hosted-gallery")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // And a shared link shows it (audit finding A6): the card is in the prerendered HTML a link preview reads,
        // pointing at the same public photo. Absolute where the site knows its public origin (SiteIdentity:BaseUrl, set
        // in production); a test stack has none, so the path is written as it is.
        var html = await (await Page.APIRequest.GetAsync($"{BaseUrl}/o/paranormal365/events/{await SlugAsync()}")).TextAsync();
        Assert.That(html, Does.Match(@"<meta property=""og:image"" content=""(https?://[^""/]+)?/media/event-photo/[0-9a-f-]+"""));
        Assert.That(html, Does.Contain(@"name=""twitter:card"" content=""summary_large_image"""));
    }

    /// <summary>
    /// The phone's ask button stays on screen while the form is elsewhere, and steps aside while
    /// the form is on screen — where it covered the form's last lines (10/16/2026).
    /// </summary>
    [Test]
    public async Task On_a_phone_the_ask_button_steps_aside_for_the_form_and_comes_back()
    {
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{BaseUrl}/o/paranormal365/events/{await SlugAsync()}");
        await WaitUntilLoadedAsync();
        await WaitForTheCircuitAsync();

        var ask = Page.Locator("#hosted-sticky-ask");
        if (await ask.CountAsync() == 0)
        {
            // Say why, in the server's words: the weekend is shared, and a test earlier in the run
            // that books it and forgets to let go would show up here as a silent skip.
            var ev = await Page.APIRequest.GetAsync($"{ApiUrl}/api/public/hosted-events/{RoomsEventId}");
            var why = ev.Ok && (await ev.JsonAsync())!.Value.TryGetProperty("notTakingBookingsSentence", out var s)
                ? s.GetString() : null;
            Assert.Ignore($"The seeded weekend is not taking bookings on this database: {why ?? "the server didn't say why"}");
        }
        await Expect(Page.Locator("#event-act")).ToBeAttachedAsync(new() { Timeout = 30_000 });

        // At the form: out of the way.
        await Page.EvaluateAsync("() => document.getElementById('event-act').scrollIntoView({ block: 'start' })");
        await Expect(ask).ToBeHiddenAsync(new() { Timeout = 10_000 });

        // Scrolled past it: back, and on screen.
        await Page.EvaluateAsync("() => window.scrollBy(0, document.getElementById('event-act').getBoundingClientRect().bottom + 10)");
        var gone = await Page.EvaluateAsync<bool>("() => document.getElementById('event-act').getBoundingClientRect().bottom < 0");
        if (!gone)
        {
            // Not enough page below the form to scroll it away; above it, then, if there is room.
            await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
            gone = await Page.EvaluateAsync<bool>("() => document.getElementById('event-act').getBoundingClientRect().top > window.innerHeight * 0.75");
        }
        if (!gone) Assert.Ignore("The form fills this page from top to bottom; there is nowhere it is off screen.");

        await Expect(ask).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(ask).ToBeInViewportAsync();

        // And at the very bottom, the page's last line is clear of it.
        await Page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        await Page.WaitForTimeoutAsync(400);
        var clear = await Page.EvaluateAsync<bool>("""
            () => {
                const a = document.getElementById('hosted-sticky-ask');
                const footer = document.querySelector('.app-footer');
                if (!a || !footer || getComputedStyle(a).visibility === 'hidden') return true;
                return footer.getBoundingClientRect().bottom <= a.getBoundingClientRect().top + 1;
            }
            """);
        Assert.That(clear, Is.True, "At the bottom of the page the ask button covers the footer.");
    }

    private async Task<string> SlugAsync()
    {
        await using var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var ev = await api.GetAsync($"/api/public/hosted-events/{RoomsEventId}");
        Assert.That(ev.Ok, Is.True, "the seeded rooms weekend is not on the public site");
        return (await ev.JsonAsync())!.Value.GetProperty("urlName").GetString()!;
    }
}
