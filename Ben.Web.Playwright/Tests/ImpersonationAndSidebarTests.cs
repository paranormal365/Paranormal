using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 159: impersonation shows the impersonated person's real world, survives a reload with
/// its exit intact, and the sidebar lists YOUR groups under Home — membership rows only.
/// </summary>
[TestFixture]
[Category("ImpersonationAndSidebar")]
public class ImpersonationAndSidebarTests : BenTestBase
{
    [Test]
    public async Task The_sidebar_lists_my_groups_under_Home_not_every_group()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();

        var nav = Page.Locator("aside, .app-nav, nav").First;
        // The SuperAdmin belongs to these three; a sees-all list would show fourteen.
        foreach (var org in new[] { "Paranormal365", "BenCo" })
            await Expect(nav.GetByText(org, new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(nav.GetByText("Journey Group", new() { Exact = false })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Impersonation_shows_their_world_and_survives_a_reload_with_its_exit()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await WaitUntilLoadedAsync();

        var row = Main.Locator("tr", new() { HasTextString = "Sarah Mitchell" }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // BY ITS TITLE, and the first match. This used to be a three-way selector ending in
        // `td:last-child button` with `.Last`, which resolves in document order — so it clicked the
        // row's LAST action button. That is Delete this user. The test then failed on a banner that
        // was never going to appear, and the reason it never deleted Sarah is that the delete asks
        // first. A selector that can reach a destructive button by accident is not a selector.
        await row.Locator("button[title='Impersonate this user']").First.ClickAsync();

        // Her world: the authenticated menu (not the signed-out list), her banner, no admin section.
        await Expect(Page.GetByText("Viewing as sarah.mitchell", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
        var nav = Page.Locator("aside, .app-nav, nav").First;
        await Expect(nav.GetByText("My Work", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(nav.GetByText("Administration", new() { Exact = true })).ToHaveCountAsync(0);

        try
        {
            // The reload is the regression that used to strand the SuperAdmin silently.
            await Page.GotoAsync($"{BaseUrl}/my-investigations");
            await WaitUntilLoadedAsync();
            await Expect(Page.GetByText("Viewing as sarah.mitchell", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Return to SuperAdmin" }))
                .ToBeVisibleAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            var back = Page.GetByRole(AriaRole.Button, new() { Name = "Return to SuperAdmin" });
            if (await back.CountAsync() > 0)
            {
                await back.ClickAsync();
                await Expect(Page.GetByText("Viewing as", new() { Exact = false }))
                    .ToHaveCountAsync(0, new() { Timeout = 20_000 });
            }
        }

        // Back to my own world, admin section included.
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitUntilLoadedAsync();
        await Expect(Page.Locator("aside, .app-nav, nav").First.GetByText("Administration", new() { Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });
    }
}
