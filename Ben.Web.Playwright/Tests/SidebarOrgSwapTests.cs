using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 172 (Ben's live report): after landing on one group's page — the way a banner or bell
/// link lands you, with a ?tab= deep link — clicking another group in the sidebar must show
/// THAT group. The same component instance serves every /organizations/{id} route, so this is
/// exactly the parameter-only navigation that used to reload nothing and read as "clicking
/// does nothing".
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("SidebarOrgSwap")]
public class SidebarOrgSwapTests : BenTestBase
{
    private const string TghId = "881ea0f6-8c0d-475e-9065-c6ed15e3302f";

    [Test]
    public async Task Swapping_groups_in_the_sidebar_actually_swaps_the_page()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        // Land the way the action-needed banner lands people: deep into a tab.
        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}?tab=members");
        await WaitUntilLoadedAsync();
        await Expect(Main.GetByText("James Thornton", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 45_000 });

        // The sidebar's your-groups link to another group (item 159's list).
        var npsLink = Page.Locator("#nav-menu a", new() { HasTextString = "Nashville Paranormal Society" }).First;
        await Expect(npsLink).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await ClickUntilAsync(npsLink,
            Main.Locator("dd", new() { HasTextString = "Nashville Paranormal Society" }));

        // …and back again, because the second swap is the one the stale instance breaks.
        var tghLink = Page.Locator("#nav-menu a", new() { HasTextString = "Tennessee Ghost Hunters" }).First;
        await ClickUntilAsync(tghLink,
            Main.Locator("dd", new() { HasTextString = "Tennessee Ghost Hunters" }));
    }
}
