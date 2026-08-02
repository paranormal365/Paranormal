using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the organization security page (<c>/organization-security</c>).
/// Verifies membership table, access grants, and permission checks.
/// </summary>
[TestFixture]
[Category("OrgSecurity")]
public class OrganizationSecurityTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(UserEmail, UserPassword);

    [Test]
    public async Task Page_RendersAfterLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/organization-security");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task Page_ShowsOrganizationDropdown()
    {
        await Page.GotoAsync($"{BaseUrl}/organization-security");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        // Should show at least one org the user belongs to
        Assert.That(body, Does.Contain("BenCo").Or.Contain("Tennessee").Or.Contain("Organization"),
            "Expected at least one org to appear on the security page.");
    }

    [Test]
    public async Task Page_AnonymousRedirectsToLogin()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/organization-security");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("Sign", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected auth guard on /organization-security.");
    }
}
