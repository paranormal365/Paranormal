using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Smoke tests: verify every primary public route returns a rendered page
/// (not a 404 or blank screen) without requiring authentication.
/// </summary>
[TestFixture]
[Category("Smoke")]
public class SmokeTests : BenTestBase
{
    private static IEnumerable<(string Url, string ExpectedText)> PublicRoutes()
    {
        yield return ($"{BaseUrl}/",            "IsHaunted");
        yield return ($"{BaseUrl}/find",        "Find");
        yield return ($"{BaseUrl}/login",       "Sign");
        yield return ($"{BaseUrl}/o/tgh",       "Tennessee Ghost Hunters");
        yield return ($"{BaseUrl}/o/tgh/cases", "#2026-");
    }

    [TestCaseSource(nameof(PublicRoutes))]
    [Description("Each public route renders without a blank/error page.")]
    public async Task PublicRoute_Renders((string Url, string ExpectedText) route)
    {
        await Page.GotoAsync(route.Url);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Page should not be blank or show a .NET error page
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Is.Not.Empty, $"Page body was empty for {route.Url}");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"), $"Error page detected at {route.Url}");
        Assert.That(body, Does.Not.Contain("does not have a property matching"), $"Telerik parameter error at {route.Url}");

        var match = Page.GetByText(route.ExpectedText, new() { Exact = false });
        await Expect(match.First).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }
}
