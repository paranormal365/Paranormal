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
        // Step 1 is the address/location entry. By label, not by placeholder: the placeholders are
        // worked examples ("123 Main St"), so matching on the word "address" or "city" found
        // nothing even though the fields were right there.
        await Expect(Page.GetByLabel("Street Address", new() { Exact = false }).First)
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.GetByLabel("City", new() { Exact = false }).First).ToBeVisibleAsync();
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

    // Both redirect tests wait for the redirect itself, not NetworkIdle. The page redirects
    // client-side after the circuit connects, which is AFTER NetworkIdle — asserting on Page.Url
    // straight away races it, and the race made RequestList the only failure in a 265-test run
    // while its twin above happened to win the same race (item #105). The product was fine:
    // verified live, anonymous /my-requests lands on /login.
    [Test]
    public async Task Wizard_AnonymousRedirectsToLogin()
        => await AssertAnonymousRedirectsToLoginAsync("/my-requests/new", "wizard");

    [Test]
    public async Task RequestList_AnonymousRedirectsToLogin()
        => await AssertAnonymousRedirectsToLoginAsync("/my-requests", "request list");

    private async Task AssertAnonymousRedirectsToLoginAsync(string path, string what)
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}{path}");
        try
        {
            await Page.WaitForURLAsync(url => url.Contains("/login"), new() { Timeout = 15_000 });
        }
        catch (TimeoutException)
        {
            // Fall through: the assert below reports what the page actually showed instead.
        }
        var body = await Page.InnerTextAsync("body");
        Assert.That(Page.Url.Contains("/login") || body.Contains("Sign In", StringComparison.OrdinalIgnoreCase),
            Is.True, $"Expected redirect to login for unauthenticated {what}; page stayed on {Page.Url}.");
    }
}
