using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Taking plans off sale from Site Settings removes the buy controls from the pricing and billing pages, says why,
/// and putting them back on sale restores them (beta feedback, 2026-09-14).
/// </summary>
/// <remarks>
/// The website reads switches from a snapshot the settings page invalidates on save, and the first read after an
/// invalidation still answers from the old snapshot while it refreshes. So each page is reloaded until it shows the
/// new state, within the snapshot's documented lifetime — waiting on the condition, not on a clock.
/// </remarks>
[TestFixture]
[Category("FeatureFlags")]
[NonParallelizable]   // a site-wide switch: every other page test would see it
public class PlanPurchaseSwitchTests : BenTestBase
{
    private const string SwitchId = "#sw-billing\\.purchases-enabled";

    private async Task SetSwitchAsync(bool on)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");
        var toggle = Page.Locator(SwitchId);
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 20_000 });
        if (await toggle.IsCheckedAsync() != on)
        {
            await toggle.SetCheckedAsync(on);
            await Expect(toggle).ToBeEnabledAsync(new() { Timeout = 15_000 });
        }
        await Expect(toggle).ToBeCheckedAsync(new() { Checked = on, Timeout = 15_000 });
    }

    /// <summary>Loads the page until <paramref name="shows"/> is on it, for up to the switch snapshot's lifetime.</summary>
    private async Task OpenUntilAsync(string path, ILocator shows)
    {
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (true)
        {
            await Page.GotoAsync($"{BaseUrl}{path}");
            await WaitForTheCircuitAsync();
            try
            {
                await Expect(shows).ToBeVisibleAsync(new() { Timeout = 5_000 });
                return;
            }
            catch (PlaywrightException) when (DateTime.UtcNow < deadline)
            {
                // The snapshot was still the old one on this load; the next load asks again.
            }
        }
    }

    [Test]
    public async Task Off_hides_the_buy_controls_and_says_so_on_again_brings_them_back()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var orgId = await OrgIdBySlugAsync("paranormal365");

        try
        {
            await SetSwitchAsync(on: false);

            await OpenUntilAsync("/pricing", Page.Locator("#pricing-not-on-sale"));
            await Expect(Page.Locator("[data-testid='pricing-band']").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await OpenUntilAsync($"/organizations/{orgId}/billing", Page.Locator("#plans-not-on-sale"));
            Assert.That(await Page.GetByRole(AriaRole.Button, new() { Name = "Subscribe", Exact = true }).CountAsync(), Is.EqualTo(0),
                "The billing page still offers Subscribe while plans are off sale.");
        }
        finally
        {
            await SetSwitchAsync(on: true);
        }

        await OpenUntilAsync($"/organizations/{orgId}/billing",
            Page.GetByRole(AriaRole.Button, new() { Name = "Subscribe", Exact = true })
                .Or(Page.GetByText("Nothing to subscribe to")).First);
        Assert.That(await Page.Locator("#plans-not-on-sale").CountAsync(), Is.EqualTo(0));
    }
}
