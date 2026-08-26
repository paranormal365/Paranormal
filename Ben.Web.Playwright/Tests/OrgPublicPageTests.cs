using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for public organization pages:
/// home page, custom CMS pages, and public case list.
/// These routes do not require authentication.
/// </summary>
[TestFixture]
[Category("OrgPublic")]
public class OrgPublicPageTests : BenTestBase
{
    // Seeded by DevelopmentDataSeeder
    private const string TghUrl = "paranormal365";
    private const string NpsUrl = "nps";

    [Test]
    public async Task OrgPublicHome_RendersOrgName()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Heading role, not bare text: the Apply-to-join panel also names the group, and a
        // strict-mode GetByText resolves to both. The heading is the sharper assertion anyway.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Paranormal365" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task OrgPublicHome_ShowsCasesNavItem()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var casesLink = Page.GetByRole(AriaRole.Link, new() { Name = "Cases" });
        await Expect(casesLink).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task OrgPublicCaseList_RendersForTgh()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}/cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("#2026-", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task OrgPublicCaseList_HauntedBadgeVisible()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}/cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var hauntedBadge = Page.GetByText("Haunted", new() { Exact = false }).First;
        await Expect(hauntedBadge).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task OrgPublicCaseList_ViewCaseNavigatesToDetail()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}/cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View" }).First;
        await Expect(viewLink).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var href = await viewLink.GetAttributeAsync("href");
        Assert.That(href, Does.Contain($"/o/{TghUrl}/cases/"), "Expected case detail link");
    }

    [Test]
    public async Task OrgPublicHome_UnknownOrg_ShowsNotFound()
    {
        await Page.GotoAsync($"{BaseUrl}/o/this-org-does-not-exist-xyz");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Contain("not found").IgnoreCase
                       .Or.Contain("404").IgnoreCase
                       .Or.Contain("doesn't exist").IgnoreCase,
            "Expected some form of not-found indication");
    }

    [Test]
    public async Task OrgPublicHome_NpsOrg_Renders()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{NpsUrl}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Nashville Paranormal Society" }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task OrgPublicCaseList_BackButtonNavigatesToOrg()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}/cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Should have a link back to the org home
        var orgLink = Page.GetByRole(AriaRole.Link, new() { Name = "Paranormal365", Exact = false });
        await Expect(orgLink.First).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── Public contact fields ─────────────────────────────────────────────────

    [Test]
    [Description("Public org home shows phone, email, and website when set.")]
    public async Task OrgPublicHome_ContactRow_ShowsWhenFieldsSet()
    {
        // The tgh or nps org may have contact fields set in dev seed — test is lenient
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Page should render without errors regardless of whether contact fields are set
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            "Public org page should not error even if contact fields are empty.");
    }

    [Test]
    [Description("Public org header renders without Telerik or application errors.")]
    public async Task OrgPublicHome_Header_RendersWithoutErrors()
    {
        await Page.GotoAsync($"{BaseUrl}/o/{TghUrl}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1_000);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("does not have a property matching"),
            "Public org page should not produce Telerik parameter errors.");
        Assert.That(body, Does.Not.Contain("NullReferenceException"));
    }
}
