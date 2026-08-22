using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the organization discovery page (<c>/find</c>).
/// </summary>
[TestFixture]
[Category("OrgDiscovery")]
public class OrgDiscoveryTests : BenTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await Page.GotoAsync($"{BaseUrl}/find");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Test]
    [Description("Discovery page renders a search input.")]
    public async Task Page_HasSearchInput()
    {
        var input = Page.Locator("input[placeholder*='city' i], input[placeholder*='address' i], input[placeholder*='zip' i]").First;
        await Expect(input).ToBeVisibleAsync();
    }

    [Test]
    [Description("The /o/{urlName} public org page is reachable.")]
    public async Task OrgPublicPage_IsReachable()
    {
        await Page.GotoAsync($"{BaseUrl}/o/tgh");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Heading role — the Apply panel also names the group; see OrgPublicPageTests.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tennessee Ghost Hunters" })).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    [Description("The 'Cases' tab on an org page shows the list of public cases.")]
    public async Task OrgPublicPage_CasesTabShowsPublicCases()
    {
        await Page.GotoAsync($"{BaseUrl}/o/tgh/cases");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Should show at least one case reference
        var caseRef = Page.GetByText("#2026-", new() { Exact = false });
        await Expect(caseRef.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
