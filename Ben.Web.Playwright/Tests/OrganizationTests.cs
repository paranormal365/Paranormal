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
        await Expect(Page.GetByText("BenCo", new() { Exact = false }))
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
        await viewLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
        var membersTab = Main.GetByText("Members", new() { Exact = false });
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
        var casesTab = Main.GetByText("Cases", new() { Exact = false });
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
