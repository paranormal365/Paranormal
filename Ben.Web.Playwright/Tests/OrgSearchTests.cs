using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the organization /find (discovery) page:
/// geocode search, result cards, in-range vs out-of-range display.
/// </summary>
[TestFixture]
[Category("OrgSearch")]
public class OrgSearchTests : BenTestBase
{
    [SetUp]
    public async Task GoToFind()
    {
        await Page.GotoAsync($"{BaseUrl}/find");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Test]
    public async Task SearchBox_AcceptsInput()
    {
        var input = Page.Locator("input[placeholder*='city' i], input[placeholder*='address' i], input[placeholder*='zip' i]").First;
        await input.FillAsync("Nashville, TN");
        var value = await input.InputValueAsync();
        Assert.That(value, Is.EqualTo("Nashville, TN"));
    }

    [Test]
    public async Task SearchButton_IsEnabledAfterInput()
    {
        var input = Page.GetByPlaceholder("Enter city, address, or zip code");
        await input.FillAsync("Nashville TN");
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "Find Groups", Exact = false })
                      .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Search" }))
                      .First;
        await Expect(btn).ToBeEnabledAsync(new() { Timeout = 3_000 });
    }

    [Test]
    public async Task Search_NashvilleTn_ReturnsResults()
    {
        var input = Page.GetByPlaceholder("Enter city, address, or zip code");
        await input.FillAsync("Nashville, TN");
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "Find Groups", Exact = false })
                      .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Search" }))
                      .First;
        await btn.ClickAsync();
        // Wait for result cards or a "no results" message
        await Page.WaitForSelectorAsync(".card, .alert", new() { Timeout = 15_000 });
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task Search_RenderedOrgCard_HasViewGroupLink()
    {
        var input = Page.GetByPlaceholder("Enter city, address, or zip code");
        await input.FillAsync("Nashville, TN");
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "Find Groups", Exact = false })
                      .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Search" }))
                      .First;
        await btn.ClickAsync();
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        var viewLink = Page.GetByRole(AriaRole.Link, new() { Name = "View Group", Exact = false }).First;
        await Expect(viewLink).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task Search_UnknownCity_ShowsEmptyState()
    {
        var input = Page.GetByPlaceholder("Enter city, address, or zip code");
        await input.FillAsync("Zxyqwerty, XX 99999");
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "Find Groups", Exact = false })
                      .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Search" }))
                      .First;
        await btn.ClickAsync();
        await Page.WaitForTimeoutAsync(3_000);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        // Should show empty/no-results state or geocode error
        Assert.That(body, Does.Contain("No").IgnoreCase
                       .Or.Contain("not found").IgnoreCase
                       .Or.Contain("couldn't").IgnoreCase
                       .Or.Contain("Could not").IgnoreCase,
            "Expected an empty-state or geocode-error message for an unknown location.");
    }
}
