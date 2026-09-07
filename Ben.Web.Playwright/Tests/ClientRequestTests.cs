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

        // Since the 2026-09-06 evaluation (W-R1) Next is DISABLED until the address has been
        // verified, rather than clickable and then refused three screens later. This used to
        // click it and check the page had not moved on; clicking a disabled button now waits out
        // its timeout, which reads as a hang rather than as the guard doing its job.
        var nextBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Next" });
        await Expect(nextBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(nextBtn).ToBeDisabledAsync(new() { Timeout = 10_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("Step 2 —"),
            "The wizard advanced past step 1 with nothing filled in.");
    }

    // Wizard_AnonymousRedirectsToLogin was here until the 2026-09-06 evaluation's phase 1. The
    // wizard now opens to everyone and makes the account at Submit, so a test asserting that it
    // sends people to sign in would be asserting the wall the phase removed. What it turned into
    // lives in AnonymousClientRequestTests.
    //
    // The list is a different matter and keeps its gate: /my-requests shows what an account has
    // asked for, which needs an account. It waits for the redirect itself rather than for
    // NetworkIdle — the page redirects client-side after the circuit connects, which is AFTER
    // NetworkIdle, and asserting on Page.Url straight away races it (item #105).
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
