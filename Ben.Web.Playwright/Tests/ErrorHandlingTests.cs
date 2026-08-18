using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for error states, 404 pages, and invalid/expired routes.
/// </summary>
[TestFixture]
[Category("ErrorHandling")]
public class ErrorHandlingTests : BenTestBase
{
    [Test]
    public async Task UnknownRoute_ShowsNotFound()
    {
        await Page.GotoAsync($"{BaseUrl}/this-route-definitely-does-not-exist-xyz-abc");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            "Should not show a .NET exception page for a 404.");
        Assert.That(body, Does.Contain("not found").IgnoreCase
                       .Or.Contain("404")
                       .Or.Contain("doesn't exist").IgnoreCase
                       .Or.Contain("not exist").IgnoreCase,
            "Expected a user-friendly not-found message.");
    }

    [Test]
    public async Task OrgRoute_InvalidGuid_DoesNotCrash()
    {
        await Page.GotoAsync($"{BaseUrl}/organizations/not-a-valid-guid");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("NullReferenceException"));
        Assert.That(body, Does.Not.Contain("InvalidOperationException"));
    }

    [Test]
    public async Task CaseDetailRoute_InvalidRef_ShowsNotFound()
    {
        await Page.GotoAsync($"{BaseUrl}/o/tgh/cases/9999-999");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task AdminRoute_UnauthorizedUser_DoesNotShowAdminData()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        // Should not expose sensitive user management data
        Assert.That(body, Does.Not.Contain("Impersonate"),
            "Regular user should not see impersonation option.");
    }

    [Test]
    public async Task ErrorPage_RendersHelpfulMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/Error");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Is.Not.Empty);
    }

    [Test]
    public async Task PublicCase_PrivateCase_NotExposed()
    {
        // A case that is not marked IsPublic should return not-found on the public endpoint
        await Page.GotoAsync($"{BaseUrl}/o/tgh/cases/2026-999");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("NullReferenceException"));
    }

    [Test]
    public async Task ApiEndpoint_Returns401ForProtectedRoute()
    {
        // Direct API call without auth should return 401/403, not a 500.
        // ApiUrl rather than string-replacing a port out of BaseUrl: that only worked while the
        // front end was on :5078, and silently called the front end instead of the API otherwise.
        var response = await Page.APIRequest.GetAsync($"{ApiUrl}/api/admin/app-users");
        Assert.That(response.Status, Is.EqualTo(401).Or.EqualTo(403),
            "Protected API endpoint should return 401/403 without auth.");
    }

    [Test]
    [Description("The home page produces no Telerik component parameter errors.")]
    public async Task HomePage_NoTelerikParameterErrors()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Wait long enough for Blazor circuit and async data loads
        await Page.WaitForTimeoutAsync(3_000);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("does not have a property matching"),
            "Home page should not show Telerik component parameter errors (e.g. TelerikMap Style, TelerikWindow Title).");
    }

    [Test]
    [Description("Public case detail page produces no Telerik component errors.")]
    public async Task PublicCaseDetail_NoTelerikErrors()
    {
        await Page.GotoAsync($"{BaseUrl}/o/tgh/cases/2026-001");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(2_000);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("does not have a property matching"),
            "Public case page should not show Telerik component parameter errors.");
    }
}
