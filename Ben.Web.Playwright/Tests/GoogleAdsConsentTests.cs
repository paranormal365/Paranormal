using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The cookie choice for Google Ads measurement (feature/google-ads-conversion, 10/19/2026): the
/// banner fits every width in both themes, Accept and Decline are the same size, nothing is
/// requested from Google before Accept, the choice is remembered, and the footer's Cookie settings
/// brings the banner back.
/// </summary>
/// <remarks>
/// <para><b>Needs a tag id.</b> The website host must run with <c>GoogleAds__TagId</c> set (any
/// <c>AW-</c> value; e.g. <c>GoogleAds__TagId=AW-000000000</c>). Without one the site renders no
/// banner at all, which is correct, and these tests are ignored rather than failed.</para>
/// <para>Every request to googletagmanager.com is intercepted and answered locally with an empty
/// script, so nothing reaches Google, and the count of those requests is the proof of "nothing
/// before consent". Signed out throughout: the banner is for every visitor.</para>
/// </remarks>
[TestFixture]
[Category("GoogleAdsConsent")]
[NonParallelizable]
public class GoogleAdsConsentTests : Oct16TestBase
{
    private int _googleRequests;

    [SetUp]
    public async Task InterceptGoogle()
    {
        _googleRequests = 0;
        await Page.RouteAsync("**/*googletagmanager.com/**", async route =>
        {
            Interlocked.Increment(ref _googleRequests);
            await route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = "" });
        });
    }

    /// <summary>Waits up to ten seconds for the intercepted request to Google.</summary>
    private async Task GoogleWasAskedAsync()
    {
        for (var i = 0; i < 40 && _googleRequests == 0; i++) await Page.WaitForTimeoutAsync(250);
        Assert.That(_googleRequests, Is.GreaterThanOrEqualTo(1), "Accept did not load the Google tag.");
    }

    private ILocator Banner => Page.Locator("[data-testid='ads-consent-banner']");

    private async Task FreshVisitAsync(string path = "/privacy")
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await Page.EvaluateAsync("() => { try { localStorage.removeItem('ih-ads-consent'); } catch (e) {} }");
        await Page.GotoAsync($"{BaseUrl}{path}");
        if (await Page.Locator("#ads-consent-banner").CountAsync() == 0)
            Assert.Ignore("No GoogleAds:TagId on this host, so there is no banner to test (set GoogleAds__TagId).");
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_banner_fits_every_screen_with_equal_buttons(string theme)
    {
        await UseThemeAsync(theme);
        var problems = new List<string>();
        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await FreshVisitAsync();
            await Expect(Banner).ToBeVisibleAsync(new() { Timeout = 10_000 });

            problems.AddRange(await LookAtAsync(Banner, "ads-consent-banner", theme));

            var accept = Page.Locator("[data-testid='ads-consent-accept']");
            var decline = Page.Locator("[data-testid='ads-consent-decline']");
            problems.AddRange(await TouchTargetAsync(accept, "ads-consent-accept", theme));
            problems.AddRange(await TouchTargetAsync(decline, "ads-consent-decline", theme));

            if (await accept.BoundingBoxAsync() is { } a && await decline.BoundingBoxAsync() is { } d
                && (Math.Abs(a.Width - d.Width) > 1 || Math.Abs(a.Height - d.Height) > 1))
                problems.Add($"{width}px {theme}: Accept is {a.Width:0}x{a.Height:0} but Decline is {d.Width:0}x{d.Height:0}.");

            if (_googleRequests > 0)
                problems.Add($"{width}px {theme}: {_googleRequests} request(s) to Google before any choice.");
        }
        NothingWrong(problems);
    }

    [Test]
    public async Task Decline_loads_nothing_and_is_remembered()
    {
        await FreshVisitAsync();
        await Page.Locator("[data-testid='ads-consent-decline']").ClickAsync();
        await Expect(Banner).ToBeHiddenAsync();

        await Page.GotoAsync($"{BaseUrl}/help");
        await Page.WaitForTimeoutAsync(1000);
        await Expect(Banner).ToBeHiddenAsync();
        Assert.That(_googleRequests, Is.EqualTo(0), "Google was contacted after Decline.");
        Assert.That(await Page.EvaluateAsync<string>("() => localStorage.getItem('ih-ads-consent')"), Is.EqualTo("denied"));
    }

    [Test]
    public async Task Accept_loads_the_tag_and_is_remembered()
    {
        await FreshVisitAsync();
        Assert.That(_googleRequests, Is.EqualTo(0), "Google was contacted before Accept.");

        await Page.Locator("[data-testid='ads-consent-accept']").ClickAsync();
        await Expect(Banner).ToBeHiddenAsync();
        await GoogleWasAskedAsync();

        await Page.GotoAsync($"{BaseUrl}/help");
        await Page.WaitForTimeoutAsync(1000);
        await Expect(Banner).ToBeHiddenAsync();
        Assert.That(await Page.EvaluateAsync<string>("() => localStorage.getItem('ih-ads-consent')"), Is.EqualTo("granted"));
    }

    [Test]
    public async Task Cookie_settings_in_the_footer_brings_the_choice_back()
    {
        await FreshVisitAsync();
        await Page.Locator("[data-testid='ads-consent-decline']").ClickAsync();
        await Expect(Banner).ToBeHiddenAsync();

        await WaitForTheCircuitAsync();
        await Page.Locator("[data-testid='footer-cookie-settings']").ClickAsync();
        await Expect(Banner).ToBeVisibleAsync();

        await Page.Locator("[data-testid='ads-consent-accept']").ClickAsync();
        await Expect(Banner).ToBeHiddenAsync();
        await GoogleWasAskedAsync();
    }

    [Test]
    public async Task Subscribed_with_a_foreign_group_goes_to_the_groups_list()
    {
        await FreshVisitAsync();
        await Page.Locator("[data-testid='ads-consent-decline']").ClickAsync();
        await Page.GotoAsync($"{BaseUrl}/subscribed?org={Guid.NewGuid()}&how=paid");
        await Page.WaitForURLAsync(url => !url.Contains("/subscribed"), new() { Timeout = 20_000 });
        Assert.That(Page.Url, Does.Not.Contain("/billing"));
        Assert.That(_googleRequests, Is.EqualTo(0));
    }
}
