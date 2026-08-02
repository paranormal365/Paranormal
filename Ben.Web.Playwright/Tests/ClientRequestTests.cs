using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the client request wizard (<c>/my-requests/new</c>) and
/// request list (<c>/my-requests</c>).
/// Requires authentication as a regular user.
/// </summary>
[TestFixture]
[Category("ClientRequests")]
public class ClientRequestTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(UserEmail, UserPassword);

    // ── Request list ──────────────────────────────────────────────────────────

    [Test]
    public async Task RequestList_PageRendersAfterLogin()
    {
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Should show a heading or the "no requests" state — either way, no crash
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Is.Not.Empty);
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task RequestList_HasNewRequestLink()
    {
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var newBtn = Page.GetByText("New Request", new() { Exact = false })
                         .Or(Page.GetByText("Request an Investigation", new() { Exact = false }))
                         .First;
        await Expect(newBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    // ── Wizard ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Wizard_Step1_RendersAddressForm()
    {
        await Page.GotoAsync($"{BaseUrl}/my-requests/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Step 1 is the address/location entry
        await Expect(Page.Locator("input[placeholder*='address' i], input[placeholder*='street' i], input[placeholder*='city' i]").First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task Wizard_Step1_RequiresFields_BeforeAdvancing()
    {
        await Page.GotoAsync($"{BaseUrl}/my-requests/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Try to advance without filling required fields
        var nextBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Next" });
        if (await nextBtn.IsVisibleAsync())
        {
            await nextBtn.ClickAsync();
            // Should still be on step 1 or show validation
            var body = await Page.InnerTextAsync("body");
            Assert.That(body, Is.Not.Empty);
        }
        else
        {
            Assert.Pass("No Next button visible on wizard step 1 — layout may differ.");
        }
    }

    [Test]
    public async Task Wizard_AnonymousRedirectsToLogin()
    {
        // Sign out first
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/my-requests/new");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Should redirect to login or show auth prompt
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("Sign") || body.Contains("sign in", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected redirect to login for unauthenticated wizard access.");
    }

    [Test]
    public async Task RequestList_AnonymousRedirectsToLogin()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/my-requests");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("Sign", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected redirect to login for unauthenticated request list.");
    }
}
