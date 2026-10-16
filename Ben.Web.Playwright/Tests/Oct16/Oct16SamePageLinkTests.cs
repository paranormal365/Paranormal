using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests.Oct16;

/// <summary>
/// A link to a spot on the same page stays on the page (fix/public-event-page, 10/16/2026).
/// </summary>
/// <remarks>
/// <para><b>The bug.</b> The site has <c>&lt;base href="/"&gt;</c>, and a browser resolves a bare
/// <c>#event-act</c> against the base: the home page. On a phone, a hosted event's sticky "Ask for a
/// place" went home instead of to the form. Every in-page link had the same fault; JumpLink now
/// writes <c>/this/page?query#spot</c>.</para>
///
/// <para><b>Each test checks the same three things:</b> the path and query did not change, the
/// fragment is the spot, and the spot is on the screen. Two run with the page live and one with
/// blazor.web.js refused, so the page stays as the server drew it — there, the browser follows the
/// link on its own, which is exactly the moment a bare <c>#spot</c> and a bare <c>#</c> used to go
/// home.</para>
/// </remarks>
[TestFixture]
[Category("Oct16")]
[Category("SamePageLinks")]
[NonParallelizable]
public class Oct16SamePageLinkTests : Oct16TestBase
{
    private string _orgId = "";
    private (string Id, string Name)? _business;

    [SetUp]
    public async Task FindTheGroup() => _orgId = await OrgIdBySlugAsync(GroupSlug);

    [TearDown]
    public async Task PurgeTheWalkingTourBusiness()
    {
        if (_business is not { } business) return;
        _business = null;

        var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        var purged = await admin.DeleteAsync($"/api/admin/organizations/{business.Id}/purge",
            new() { DataObject = new { confirmName = business.Name } });
        if (!purged.Ok) await admin.DeleteAsync($"/api/organizations/{business.Id}");
    }

    /// <summary>The séance weekend's public page, published and asking.</summary>
    private async Task<string> TheWeekendsPageAsync()
    {
        var admin = await ApiAsAsync(SuperAdminEmail, SuperAdminPassword);
        await admin.PostAsync($"/api/organizations/{_orgId}/events/{RoomsEventId}/booking-mode",
            new() { DataObject = new { mode = 0 } });
        var slug = await PublishSeededEventAsync(_orgId, RoomsEventId);
        return $"{BaseUrl}/o/{GroupSlug}/events/{slug}";
    }

    /// <summary>The path and query of an address, without its fragment.</summary>
    private static string PageOf(string url) => new Uri(url).GetLeftPart(UriPartial.Query);

    private async Task StaysOnThePageAsync(string before, string spot)
    {
        await Page.WaitForURLAsync(new Regex(Regex.Escape("#" + spot) + "$"), new() { Timeout = 10_000 });
        Assert.That(PageOf(Page.Url), Is.EqualTo(PageOf(before)),
            $"The link to #{spot} left the page: from {before} to {Page.Url}.");
        await Expect(Page.Locator($"#{spot}")).ToBeInViewportAsync(new() { Timeout = 10_000 });
    }

    // ── The reported one ────────────────────────────────────────────────────

    [Test]
    public async Task On_a_phone_Ask_for_a_place_scrolls_to_the_form_and_stays_on_the_event()
    {
        var url = await TheWeekendsPageAsync();
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync(url);
        await WaitForTheCircuitAsync();

        var ask = Page.Locator("#hosted-sticky-ask");
        await Expect(ask).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await ask.GetAttributeAsync("href"), Does.StartWith(new Uri(url).AbsolutePath + "#"),
            "The sticky button's address does not name this page.");

        var before = Page.Url;
        await ask.ClickAsync();
        await StaysOnThePageAsync(before, "event-act");

        // Once the form is on screen the fixed button steps aside (HostedEventShowcase.razor.js),
        // rather than sitting over the form's last lines and its send button.
        await Expect(ask).ToBeHiddenAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task Before_the_page_is_live_Ask_for_a_place_still_stays_on_the_event()
    {
        var url = await TheWeekendsPageAsync();
        await Page.SetViewportSizeAsync(375, 812);
        await Page.RouteAsync("**/_framework/blazor.web.js", route => route.AbortAsync());
        await Page.GotoAsync(url);

        var ask = Page.Locator("#hosted-sticky-ask");
        await Expect(ask).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var before = Page.Url;
        await ask.ClickAsync();
        await StaysOnThePageAsync(before, "event-act");
    }

    [Test]
    public async Task Arriving_with_the_form_in_the_address_lands_on_the_form()
    {
        // OrgPublicSection's "Ask for a place" and the organizer's page link here from elsewhere.
        // Blazor scrolls to the top after a page change; the page now follows the fragment itself.
        var url = await TheWeekendsPageAsync();
        await Page.GotoAsync(url + "#event-act");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#event-act")).ToBeInViewportAsync(new() { Timeout = 20_000 });
        Assert.That(PageOf(Page.Url), Is.EqualTo(PageOf(url)));
    }

    // ── A settings index link ───────────────────────────────────────────────

    [Test]
    public async Task A_settings_index_link_scrolls_to_its_section_and_keeps_the_tab()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{_orgId}?tab=settings");
        await WaitForTheCircuitAsync();
        await WaitUntilLoadedAsync();
        await SkipAnyTourAsync();

        var index = Page.Locator("nav[aria-label='Settings sections']");
        await Expect(index).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var duties = index.GetByRole(AriaRole.Link, new() { Name = "Duties", Exact = true });
        Assert.That(await duties.GetAttributeAsync("href"), Does.Contain("?tab=settings#settings-duties"),
            "The index link should name this page and its tab.");
        await Expect(Page.Locator("#settings-duties")).Not.ToBeInViewportAsync();   // so the scroll is real

        var before = Page.Url;
        await duties.ClickAsync();
        await StaysOnThePageAsync(before, "settings-duties");
        Assert.That(Page.Url, Does.Contain("tab=settings"), "Following the index link lost the Settings tab.");
    }

    // ── A tour page link ────────────────────────────────────────────────────

    /// <summary>
    /// "Pick a night" on the poster, and the "Next walk" tile, both jump to the dates. No seeded
    /// group runs a tour, so a walking-tour business is registered for this test and purged after
    /// (the same recipe as CalendarLettersAreFollowedTests).
    /// </summary>
    [Test]
    public async Task A_tour_pages_Pick_a_night_and_Next_walk_scroll_to_the_dates()
    {
        var tourUrl = await AWalkingTourWithADateAsync();

        await Page.GotoAsync(tourUrl);
        await WaitForTheCircuitAsync();

        var pick = Page.GetByRole(AriaRole.Link, new() { Name = "Pick a night", Exact = true });
        await Expect(pick).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var before = Page.Url;
        await pick.ClickAsync();
        await StaysOnThePageAsync(before, "tour-dates");

        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        var next = Page.Locator(".page-hero__fact", new() { HasTextString = "Next walk" }).Locator("a");
        await Expect(next).ToBeVisibleAsync();
        await next.ClickAsync();
        await Expect(Page.Locator("#tour-dates")).ToBeInViewportAsync(new() { Timeout = 10_000 });
        Assert.That(PageOf(Page.Url), Is.EqualTo(PageOf(before)), "The Next walk tile left the tour page.");
    }

    // ── href="#" before the page is live ────────────────────────────────────

    /// <summary>
    /// A do-nothing link (<c>href="#"</c>, a click handler behind it) pressed by a visitor before the
    /// page is live used to go to the home page: "#" against <c>&lt;base href="/"&gt;</c>. App.razor
    /// now cancels that click in the capture phase.
    /// </summary>
    /// <remarks>
    /// The page's own <c>href="#"</c> is used when the signed-out page has one on screen; otherwise
    /// one is added to the page, which exercises the same document-wide guard. The test says which.
    /// </remarks>
    [Test]
    public async Task Signed_out_a_do_nothing_link_pressed_before_the_page_is_live_stays_put()
    {
        var url = await TheWeekendsPageAsync();
        await Page.RouteAsync("**/_framework/blazor.web.js", route => route.AbortAsync());
        await Page.GotoAsync(url);
        await Expect(Page.Locator("#event-act")).ToBeAttachedAsync(new() { Timeout = 30_000 });

        var real = Page.Locator("a[href='#']").Filter(new() { Visible = true });
        string which;
        ILocator link;
        if (await real.CountAsync() > 0)
        {
            link = real.First;
            which = "the page's own \"" + (await link.InnerTextAsync()).Trim() + "\"";
        }
        else
        {
            await Page.EvaluateAsync(@"() => {
                const a = document.createElement('a');
                a.href = '#'; a.id = 'oct16-do-nothing'; a.textContent = 'Do nothing';
                a.style.cssText = 'position:fixed;top:120px;left:20px;z-index:99999;background:#fff;color:#000;padding:4px';
                document.body.appendChild(a);
            }");
            link = Page.Locator("#oct16-do-nothing");
            which = "an added href=\"#\" link (the signed-out page shows none)";
        }

        var before = Page.Url;
        await link.ClickAsync();
        await Page.WaitForTimeoutAsync(1_500);   // a navigation home would have started by now
        Assert.That(PageOf(Page.Url), Is.EqualTo(PageOf(before)),
            $"Pressing {which} before the page was live left it, for {Page.Url}.");
        await Expect(Page.Locator("#event-act")).ToBeAttachedAsync();
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    /// <summary>A walking-tour business of Sarah's with one bookable tour and one public date; its tour page's address.</summary>
    private async Task<string> AWalkingTourWithADateAsync()
    {
        var sarah = await ApiAsAsync(UserEmail, UserPassword);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var name = $"Oct16 Jump Walks {tag}";
        var slug = $"oct16-jump-{tag}";

        var registered = await OkJsonAsync(await sarah.PostAsync("/api/security/organizations/register",
            new() { DataObject = new { name, urlName = slug, kind = 1 } }), "registering the walking-tour business");
        var orgId = registered.GetProperty("organizationId").GetString()!;
        _business = (orgId, name);

        var types = await OkJsonAsync(await sarah.GetAsync("/api/organization-address-types"), "reading address types");
        var typeId = types.EnumerateArray().First().GetProperty("id").GetString();

        // Far from Nashville, with its coordinates, so nothing is geocoded and no nearby panel counts it.
        var address = await OkJsonAsync(await sarah.PostAsync($"/api/organizations/{orgId}/addresses", new()
        {
            DataObject = new
            {
                organizationAddressTypeId = typeId, streetAddress1 = "16 Jump Street", city = "Lanternburg",
                state = "ND", zipCode = "58001", country = "US", isPrimary = true,
                latitude = 47.12m, longitude = -100.82m,
            },
        }), "adding the meeting point");

        var tour = await OkJsonAsync(await sarah.PostAsync($"/api/organizations/{orgId}/tours", new()
        {
            DataObject = new
            {
                name = $"Oct16 Jump Walk {tag}", description = "<p>A walk.</p>",
                startOrganizationAddressId = address.GetProperty("id").GetString(),
                durationMinutes = 90, defaultCapacity = 6, timeZoneId = "America/Chicago",
            },
        }), "making the tour");

        var start = DateTime.UtcNow.Date.AddDays(10).AddHours(1);
        await OkJsonAsync(await sarah.PostAsync($"/api/organizations/{orgId}/calendar", new()
        {
            DataObject = new
            {
                title = $"Oct16 Jump Walk night {tag}", startDateTime = start, endDateTime = start.AddMinutes(90),
                isAllDay = false, isPublic = true, tourId = tour.GetProperty("id").GetString(), attendeeCapacity = 6,
            },
        }), "adding a date to the tour");

        return $"{BaseUrl}/o/{slug}/tours/{tour.GetProperty("urlName").GetString()}";
    }
}
