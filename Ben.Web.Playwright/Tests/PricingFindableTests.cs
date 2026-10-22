using System.Text.RegularExpressions;
using Ben.Web.Playwright.Tests.Oct16;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Pricing can be found (feature/pricing-findable, 10/22/2026).
/// </summary>
/// <remarks>
/// <para><b>What Ben said.</b> "There is no direct menu link to prices on the home page. The way I
/// am finding it is going to Community then Pricing." And: "I don't see, as an administrator, how
/// to configure event settings like pricing."</para>
///
/// <para><b>What these check, on every screen type and in both themes:</b> the top bar has Pricing
/// signed out and signed in, the home page has See pricing under the three ways in, the footer has
/// Pricing, and Administration → Pricing shows the plans, the events and the Ghost Walk Tour
/// subscription with a sentence each and refuses an ordinary member. Below 992px every control
/// looked at is at least 44px each way.</para>
///
/// <para><b>Running them:</b> <c>scripts/run-e2e.sh --filter TestCategory=PricingFindable</c>, with
/// all three hosts up. Nothing here changes a setting: the selling switches are read, not moved,
/// so a shared database is left as it was found.</para>
/// </remarks>
[TestFixture]
[Category("PricingFindable")]
[NonParallelizable]
public class PricingFindableTests : Oct16TestBase
{
    /// <summary>The bar's own Pricing entry.</summary>
    private ILocator BarPricing => Page.Locator("#top-menu a.topnav-link[href='/pricing']");

    private async Task OpenAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
    }

    // ── The top bar ────────────────────────────────────────────────────────

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Signed_out_the_top_bar_has_Pricing_on_every_screen(string theme)
    {
        await UseThemeAsync(theme);
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenAsync("/");
            problems.AddRange(await LookAtAsync(BarPricing, "pricing-bar-visitor", theme));
            problems.AddRange(await TouchTargetAsync(BarPricing, "Pricing on the bar", theme));
        }

        NothingWrong(problems);

        await BarPricing.ClickAsync();
        await Page.WaitForURLAsync(new Regex("/pricing$"));
        await Expect(Page.Locator("#top-menu .topnav-item.active a[href='/pricing']")).ToBeVisibleAsync();
    }

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Signed_in_the_top_bar_has_Pricing_and_Community_does_not(string theme)
    {
        await UseThemeAsync(theme);
        await LoginAsync(MemberEmail, MemberPassword);
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenAsync("/feed");
            problems.AddRange(await LookAtAsync(BarPricing, "pricing-bar-member", theme));
            problems.AddRange(await TouchTargetAsync(BarPricing, "Pricing on the bar", theme));
        }

        // One place for it: Community's rail no longer lists it.
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenAsync("/find");
        Assert.That(await Page.Locator(".app-rail a[href='/pricing']").CountAsync(), Is.Zero,
            "Pricing is still listed under Community as well as on the bar.");

        NothingWrong(problems);
    }

    // ── The home page and the footer ───────────────────────────────────────

    [TestCase("dark")]
    [TestCase("light")]
    public async Task The_home_page_has_See_pricing_under_the_three_ways_in_and_the_footer_has_Pricing(string theme)
    {
        await UseThemeAsync(theme);
        var problems = new List<string>();
        var button = Page.GetByTestId("home-see-pricing");
        var footer = Page.GetByTestId("footer-pricing");

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenAsync("/");
            problems.AddRange(await LookAtAsync(button, "pricing-home-button", theme, Page.Locator(".pitch-pillar")));
            problems.AddRange(await TouchTargetAsync(button, "See pricing", theme));
            problems.AddRange(await LookAtAsync(footer, "pricing-footer", theme));
            problems.AddRange(await TouchTargetAsync(footer, "Pricing in the footer", theme));
        }

        NothingWrong(problems);

        await button.ClickAsync();
        await Page.WaitForURLAsync(new Regex("/pricing$"));
    }

    // ── Administration → Pricing ───────────────────────────────────────────

    [TestCase("dark")]
    [TestCase("light")]
    public async Task Administration_Pricing_shows_every_price_with_a_sentence_on_every_screen(string theme)
    {
        await UseThemeAsync(theme);
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var problems = new List<string>();

        foreach (var (width, height) in Widths)
        {
            await Page.SetViewportSizeAsync(width, height);
            await OpenAsync("/admin/pricing");

            foreach (var section in new[] { "admin-pricing-plans", "admin-pricing-events", "admin-pricing-ghost-walk" })
            {
                var preview = Page.GetByTestId($"{section}-preview");
                problems.AddRange(await LookAtAsync(preview, $"{section}", theme));
                if (await preview.CountAsync() > 0 && (await preview.InnerTextAsync()).Trim().Length < 10)
                    problems.Add($"{section} at {width}px ({theme}) has no sentence.");
            }

            problems.AddRange(await TouchTargetAsync(Page.GetByTestId("admin-pricing-public"), "See what visitors see", theme));
            problems.AddRange(await TouchTargetAsync(Page.GetByTestId("admin-pricing-edit-plans"), "Edit plans and prices", theme));
            problems.AddRange(await TouchTargetAsync(Page.Locator("#pricing-events button:has-text('Save')"), "an event price's Save", theme));
            problems.AddRange(await TouchTargetAsync(Page.Locator("#pricing-events .admin-pricing__switch"), "Sell event credits", theme));
            problems.AddRange(await TouchTargetAsync(Page.Locator("#pricing-events input.form-control"), "the event price box", theme));
        }

        NothingWrong(problems);

        Assert.That(await Page.GetByTestId("admin-pricing-public").GetAttributeAsync("href"), Is.EqualTo("/pricing"));
        Assert.That(await Page.GetByTestId("admin-pricing-edit-plans").GetAttributeAsync("href"), Is.EqualTo("/admin/subscription-tiers"));
    }

    [Test]
    public async Task Administration_lists_Pricing_and_it_opens_the_page()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenAsync("/admin/dashboard");

        var entry = Page.Locator(".app-rail a[href='/admin/pricing']");
        await Expect(entry).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await entry.ClickAsync();
        await Page.WaitForURLAsync(new Regex("/admin/pricing$"));
        await Expect(Page.GetByTestId("admin-pricing-events")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    public async Task A_refused_price_shows_the_servers_sentence_and_changes_nothing()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenAsync("/admin/pricing");

        // A percentage over 100 is refused by the server, so nothing is stored.
        var percent = Page.Locator("[data-key='events.credit-subscriber-percent']");
        await Expect(percent).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var before = await Page.GetByTestId("admin-pricing-events-preview").InnerTextAsync();

        await percent.Locator("input").FillAsync("150");
        await percent.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        var refusal = Page.GetByTestId("admin-pricing-refusal");
        await Expect(refusal).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That((await refusal.InnerTextAsync()).Trim(), Is.Not.EqualTo("Couldn't save that price."),
            "The page showed its own fallback rather than the server's reason.");
        Assert.That(await Page.GetByTestId("admin-pricing-events-preview").InnerTextAsync(), Is.EqualTo(before));
    }

    [Test]
    public async Task A_member_is_refused_Administration_Pricing()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/pricing");
        await WaitForTheCircuitAsync();

        await Page.WaitForURLAsync(url => !url.Contains("/admin/pricing"), new() { Timeout = 15_000 });
        Assert.That(await Page.GetByTestId("admin-pricing-events").CountAsync(), Is.Zero);
    }

    [Test]
    public async Task Site_Settings_says_the_prices_are_also_on_Pricing()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenAsync("/admin/site-settings?setting=events.credit-price-usd");

        var line = Page.GetByTestId("settings-also-on-pricing");
        await Expect(line).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await line.Locator("a").GetAttributeAsync("href"), Is.EqualTo("/admin/pricing"));
    }
}
