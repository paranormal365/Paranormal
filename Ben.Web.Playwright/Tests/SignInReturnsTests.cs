using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Signing in brings a person back to the page they were on (UI test pass 6.1, 2026-09-14).
/// </summary>
/// <remarks>
/// A group's calendar opened signed out sent the visitor to a bare sign-in page, and signing in landed on the home page;
/// the header's Sign In did the same from any page.
/// </remarks>
[TestFixture]
[Category("SignIn")]
public class SignInReturnsTests : BenTestBase
{
    private async Task SignInOnTheFormAsync()
    {
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login\\?returnUrl="), new() { Timeout = 20_000 });
        await FillCredentialsAsync(UserEmail, UserPassword);
        await ClickUntilUrlAsync(Page.Locator("form button[type='submit']"), "/organizations/");
    }

    [Test]
    public async Task AGroupPageOpenedSignedOut_ComesBackAfterSigningIn()
    {
        var orgId = await OrgIdBySlugAsync("paranormal365");
        var page = $"/organizations/{orgId}?tab=calendar";

        await Page.GotoAsync($"{BaseUrl}{page}");
        await SignInOnTheFormAsync();

        await Expect(Page).ToHaveURLAsync($"{BaseUrl}{page}", new() { Timeout = 20_000 });
    }

    [Test]
    public async Task TheHeadersSignIn_CarriesThePageItIsOn()
    {
        await Page.GotoAsync($"{BaseUrl}/equipment");
        await WaitForTheCircuitAsync();
        var signIn = Page.Locator("a.ben-signin");
        await Expect(signIn).ToHaveAttributeAsync("href", "/login?returnUrl=%2Fequipment", new() { Timeout = 10_000 });

        // And it follows the person round the site, rather than keeping the page it was first drawn on.
        await Page.Locator("a[href='/publications']").First.ClickAsync();
        await Expect(signIn).ToHaveAttributeAsync("href", "/login?returnUrl=%2Fpublications", new() { Timeout = 10_000 });
    }
}
