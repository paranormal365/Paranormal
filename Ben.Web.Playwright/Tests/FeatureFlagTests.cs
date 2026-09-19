using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A switched-off section must refuse its addresses, not merely hide its links.
/// </summary>
/// <remarks>
/// <para>This is the whole reason the feature flags were built the way they were. A gate that
/// only removes navigation entries looks finished — the section vanishes from the menu, and a
/// screenshot proves it — while every URL under it still answers anyone who bookmarked one. This
/// codebase has met that failure often enough to distrust the hidden-link half on its own, so the
/// test asserts the half that is easy to get wrong.</para>
///
/// <para>The switch is thrown through the admin page rather than the API, because that is the
/// path an administrator uses and therefore the one worth proving. It also exercises the toggle
/// control itself.</para>
///
/// <para>The waits are not padding. The website reads feature switches from a snapshot that
/// refreshes about every thirty seconds, and a read that finds it stale returns the value already
/// in hand while refreshing behind itself — so the first page load after a change legitimately
/// shows the old answer. Saving from the admin page invalidates the snapshot for that reason, and
/// the helper still loads twice to stay honest about the lag.</para>
/// </remarks>
[TestFixture]
[Category("FeatureFlags")]
public class FeatureFlagTests : BenTestBase
{
    private const string EquipmentSwitchId = "#sw-features\\.equipment";
    private const string GatedUrl = "/equipment-catalog";

    /// <summary>Throws the switch on the settings page and waits for the save to land.</summary>
    private async Task SetEquipmentAsync(bool on)
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/site-settings");

        var toggle = Page.Locator(EquipmentSwitchId);
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 20_000 });

        if (await toggle.IsCheckedAsync() != on)
        {
            await toggle.SetCheckedAsync(on);
            // The save is a round trip; the control re-enables when it finishes.
            await Expect(toggle).ToBeEnabledAsync(new() { Timeout = 15_000 });
        }

        await Expect(toggle).ToBeCheckedAsync(new() { Checked = on, Timeout = 15_000 });
    }

    /// <summary>
    /// Loads a page twice, so the answer reflects the current flag rather than the snapshot that
    /// was current when the page was first asked.
    /// </summary>
    private async Task<string> SettledBodyAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await Task.Delay(3_000);
        await Page.GotoAsync($"{BaseUrl}{path}");
        return await Page.InnerTextAsync("body");
    }

    [Test]
    public async Task SwitchingASectionOff_TakesItsUrlDown_AndBringsItBack()
    {
        try
        {
            await SetEquipmentAsync(on: true);
            Assert.That(await SettledBodyAsync(GatedUrl), Does.Not.Contain("Page not found"),
                "The equipment catalogue should render while its feature is on.");

            await SetEquipmentAsync(on: false);
            Assert.That(await SettledBodyAsync(GatedUrl), Does.Contain("Page not found"),
                "A switched-off section must refuse its URL, not just drop out of the navigation — "
                + "a bookmark or a shared link would still reach it.");

            // The navigation reads the same provider, so the two cannot disagree.
            await Page.GotoAsync(BaseUrl);
            var nav = await Page.InnerTextAsync("body");
            Assert.That(nav, Does.Not.Contain("My Equipment"),
                "The navigation should not offer a section whose pages refuse to load.");

            // Back on: the section returns, contents intact — the switch hides, never deletes.
            await SetEquipmentAsync(on: true);
            Assert.That(await SettledBodyAsync(GatedUrl), Does.Not.Contain("Page not found"),
                "Turning the feature back on must restore the section.");
        }
        finally
        {
            // Leave the site as it was found, whatever happened above.
            await SetEquipmentAsync(on: true);
        }
    }
}
