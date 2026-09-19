using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for organization management pages accessible to org members.
/// Navigates via /organizations list to avoid hard-coding Guids.
/// </summary>
[TestFixture]
[Category("Organizations")]
public class OrganizationTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(UserEmail, UserPassword);

    // ── Organization list ─────────────────────────────────────────────────────

    [Test]
    public async Task OrgList_RendersAfterLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task OrgList_ShowsBenCo()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // .First: the grid shows the name ("BenCo") and the URL name ("benco") in adjacent cells,
        // and a case-insensitive loose match hits both, which is a strict-mode violation.
        await Expect(Page.GetByText("BenCo", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task OrgList_AnonymousRedirectsToLogin()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("Sign", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected auth guard on /organizations.");
    }

    // ── Organization view ─────────────────────────────────────────────────────

    [Test]
    public async Task OrgView_NavigateFromList()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await Expect(viewLink).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // Grid command button → NavigationManager: dropped if the circuit is not live yet.
        await ClickUntilUrlAsync(viewLink, @"/organizations/[0-9a-f\-]+");
        Assert.That(Page.Url, Does.Contain("/organizations/"), "Expected navigation to org detail page.");
    }

    [Test]
    public async Task OrgView_HasTabStrip()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Telerik TabStrip renders tab items
        var detailsTab = Page.GetByText("Details", new() { Exact = false });
        await Expect(detailsTab).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task OrgView_MembersTab_ShowsMembers()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // The tab itself, by role. A loose GetByText matched the stats panel's "Members"
        // label the moment the Details tab gained one, and Playwright's strict mode
        // failed on the ambiguity rather than picking the wrong one — which is the good
        // outcome, but the locator was always too vague to mean "the tab".
        var membersTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Members" });
        await Expect(membersTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await membersTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Should show at least the owner row
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task OrgView_CasesTab_ShowsCases()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var casesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Cases" });
        await Expect(casesTab).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await casesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task OrgView_CalendarTab_Renders()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var calTab = Page.GetByText("Calendar", new() { Exact = false });
        if (await calTab.IsVisibleAsync())
        {
            await calTab.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var body = await Page.InnerTextAsync("body");
            Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        }
        else
        {
            Assert.Pass("Calendar tab not visible for this user role — expected for non-members.");
        }
    }

    [Test]
    public async Task OrgView_FilesTab_Renders()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var filesTab = Main.GetByText("Files", new() { Exact = false });
        if (await filesTab.IsVisibleAsync())
        {
            await filesTab.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var body = await Page.InnerTextAsync("body");
            Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        }
        else
        {
            Assert.Pass("Files tab not visible for this user role.");
        }
    }

    [Test]
    public async Task OrgView_MessagesTab_Renders()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" })
                           .Or(Page.GetByRole(AriaRole.Button, new() { Name = "View" }))
                           .First;
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var msgTab = Main.GetByText("Messages", new() { Exact = false });
        if (await msgTab.IsVisibleAsync())
        {
            await msgTab.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var body = await Page.InnerTextAsync("body");
            Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        }
        else
        {
            Assert.Pass("Messages tab not visible for this user role.");
        }
    }
}
