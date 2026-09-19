using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright;

/// <summary>
/// Guards route parity with Ben.Web.WebApp. Six routes were missing after the port and nothing
/// caught it, because an unmatched route rendered an empty page rather than a "not found" one —
/// /upload-files simply looked blank. These assert the pages actually render content.
/// </summary>
[TestFixture]
public class RestoredRouteTests : BenTestBase
{
    [SetUp]
    public async Task SignIn() => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    // Resolved from the slug at run time — a hardcoded GUID dies with every database rebuild.
    private string OrgId = null!;

    [SetUp]
    public async Task ResolveOrgId() => OrgId = await OrgIdBySlugAsync("benco");

    [TestCase("/upload-files",        "Upload")]
    [TestCase("/media-library",       "Media")]
    [TestCase("/organization-security","Security")]
    [TestCase("/organizations/{org}/equipment-feedback", "Feedback")]
    public async Task RestoredRoute_RendersContent(string path, string expected)
    {
        path = path.Replace("{org}", OrgId);   // resolved in SetUp — ids do not survive reseeds
        await Page.GotoAsync($"{BaseUrl}{path}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Task.Delay(1000);

        var body = (await Page.Locator("body").InnerTextAsync() ?? "").Trim();
        var main = await Main.InnerTextAsync() ?? "";

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Not.Contain("Page not found"),
                $"{path} is not routed at all");
            Assert.That(body.Length, Is.GreaterThan(80),
                $"{path} rendered an all-but-empty page");
            Assert.That(main, Does.Contain(expected).IgnoreCase,
                $"{path} did not render its own content");
        });
    }

    /// <summary>The safety net itself: a route that does not exist must say so.</summary>
    [Test]
    public async Task UnknownRoute_ShowsNotFoundRatherThanABlankPage()
    {
        await Page.GotoAsync($"{BaseUrl}/no-such-page-exists");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("Page not found")).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }
}
