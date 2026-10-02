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
    // Resolved from the slug at run time — a hardcoded GUID dies with every database rebuild.
    private string TghId = null!;

    [SetUp]
    public async Task ResolveTghId() => TghId = await OrgIdBySlugAsync("paranormal365");

    [Test]
    public async Task Swapping_groups_in_the_sidebar_actually_swaps_the_page()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);

        // Land the way the action-needed banner lands people: deep into a tab.
        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}?tab=members");
        await WaitUntilLoadedAsync();
        await Expect(Main.GetByText("James Thornton", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 45_000 });

        // The way a person swaps since the menu card (Signal, 2026-10-01): the group's menu has
        // "Your groups" at the top, which lists their groups; another one is picked from there.
        var back = Page.Locator("[data-testid=rail-back]").Filter(new() { Visible = true }).First;
        var npsLink = Page.Locator("aside a", new() { HasTextString = "Nashville Paranormal Society" }).Filter(new() { Visible = true }).First;
        await ClickUntilAsync(back, npsLink);
        await ClickUntilAsync(npsLink,
            Main.Locator("dd", new() { HasTextString = "Nashville Paranormal Society" }));

        // …and back again, because the second swap is the one the stale instance breaks.
        var tghLink = Page.Locator("aside a", new() { HasTextString = "Paranormal365" }).Filter(new() { Visible = true }).First;
        await ClickUntilAsync(back, tghLink);
        await ClickUntilAsync(tghLink,
            Main.Locator("dd", new() { HasTextString = "Paranormal365" }));
    }
}
