using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the login page and authentication flow.
/// </summary>
[TestFixture]
[Category("Auth")]
public class LoginTests : BenTestBase
{
    [Test]
    [Description("Login page renders email and password fields and a submit button.")]
    public async Task LoginPage_HasRequiredFields()
    {
        await Page.GotoAsync($"{BaseUrl}/login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("input[type='password']")).ToBeVisibleAsync();
        // Exact, because the page also offers "Sign in with Microsoft" and a substring match
        // resolves to both.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign In", Exact = true }))
            .ToBeVisibleAsync();
    }

    [Test]
    [Description("Valid credentials navigate away from the login page.")]
    public async Task Login_WithValidCredentials_Succeeds()
    {
        await LoginAsync(UserEmail, UserPassword);
        Assert.That(Page.Url, Does.Not.Contain("/login"), "Should have navigated away from /login after successful auth.");
    }

    [Test]
    [Description("After login, the user's email is shown in the app bar.")]
    public async Task Login_ShowsEmailInAppBar()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The signed-in email lives in the profile menu on the new site, and directly in the app
        // bar on the original; opening is a no-op where it is already visible.
        await OpenProfileMenuAsync();

        var emailText = Page.GetByText(UserEmail.Split('@')[0], new() { Exact = false });
        await Expect(emailText).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("Sign Out button appears after login.")]
    public async Task Login_ShowsSignOutButton()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await OpenProfileMenuAsync();

        var signOut = Page.GetByText("Sign Out");
        await Expect(signOut).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("Logging out returns user to unauthenticated state (Sign In button appears).")]
    public async Task Logout_ReturnsToAnonymousState()
    {
        await LoginAsync(UserEmail, UserPassword);
        await LogoutAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The header's Sign In control specifically. A loose "Sign In" also matches the home
        // page's "Sign In to Request an Investigation" call to action, and two matches is a
        // strict-mode violation rather than a pass — which only showed up once sign-out started
        // reliably landing on a page that has both.
        var signIn = Page.Locator(".app-header").GetByText("Sign In", new() { Exact = true });
        await Expect(signIn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("SuperAdmin login causes the Administration button to appear.")]
    public async Task SuperAdminLogin_ShowsAdministrationButton()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var adminBtn = Page.GetByText("Administration");
        await Expect(adminBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
