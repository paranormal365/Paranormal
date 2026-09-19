using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A public group with no authored home page shows a page built from what it already keeps,
/// not "has not published a home page yet" (item 205).
/// </summary>
[TestFixture]
[Category("DefaultGroupPage")]
public class DefaultGroupPageTests : BenTestBase
{
    [Test]
    public async Task A_group_with_no_home_page_gets_a_default_one()
    {
        // Any listed group will do; the first one the discovery page offers. A group that has
        // authored a page shows that page instead, which is also right, so the test looks for
        // one without.
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = ApiUrl });
        var urlName = Environment.GetEnvironmentVariable("BEN_DEFAULT_PAGE_ORG");
        if (string.IsNullOrWhiteSpace(urlName))
        {
            var list = await api.GetAsync("/api/public/organizations/browse?page=1&pageSize=50");
            if (!list.Ok) Assert.Ignore("no public directory to pick a group from; set BEN_DEFAULT_PAGE_ORG");
            var json = await list.JsonAsync();
            var items = json!.Value.ValueKind == System.Text.Json.JsonValueKind.Array ? json.Value
                      : json.Value.TryGetProperty("items", out var arr) ? arr : default;
            foreach (var item in items.EnumerateArray())
            {
                var candidate = item.GetProperty("urlName").GetString();
                var home = await api.GetAsync($"/api/public/organizations/{candidate}");
                if (!home.Ok) continue;
                var h = (await home.JsonAsync())!.Value;
                if (h.TryGetProperty("homePage", out var hp) && hp.ValueKind == System.Text.Json.JsonValueKind.Null) { urlName = candidate; break; }
            }
        }
        if (string.IsNullOrWhiteSpace(urlName)) Assert.Ignore("every listed group has authored a page — nothing to show a default for");

        await Page.GotoAsync($"{BaseUrl}/o/{urlName}");
        await Expect(Page.Locator("[data-testid='default-group-page']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.GetByText("has not published a home page yet")).ToHaveCountAsync(0);
        // Something true about the group: a member count and whether it takes cases.
        await Expect(Page.Locator("[data-testid='default-accepting'], [data-testid='default-not-accepting']")).ToHaveCountAsync(1);
        TestContext.Out.WriteLine("default page for: " + urlName);
        if (Environment.GetEnvironmentVariable("BEN_GROUP_SHOT") is { Length: > 0 } shot)
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
    }
}
