using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Item 166 W4: the two CMS-side walkthroughs run end to end from their ? affordances —
/// every step renders its teaching, Done completes, and the dismissal lands in the person's
/// tour state (a row, so it survives any browser). Launched by hand in both tests: the manual
/// path must always work whatever the auto-launch state of the shared seed account.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("CmsCaseTours")]
public class CmsAndCaseTourTests : BenTestBase
{
    // Resolved from the slug at run time — a hardcoded GUID dies with every database rebuild.
    private string TghId = null!;

    [SetUp]
    public async Task ResolveTghId() => TghId = await OrgIdBySlugAsync("paranormal365");

    [Test]
    public async Task The_cms_editor_tour_walks_its_steps_and_records_its_dismissal()
    {
        await LoginAsync(UserEmail, UserPassword);   // Sarah — TGH administrator
        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}/cms");
        await WaitUntilLoadedAsync();

        // The auto-launch may have fired for a first-time seat — close it so the manual
        // launch is what this test measures.
        if (await Page.Locator(".ben-tour-card").CountAsync() > 0)
            await Page.Locator(".ben-tour-card").GetByText("Skip tour").ClickAsync();

        await ClickUntilAsync(Page.Locator("#cms-tour-launch"), Page.Locator(".ben-tour-card"));

        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("Pages are born here");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("sections");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("publish");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("Logos");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToHaveCountAsync(0);

        Assert.That(await DismissedToursAsync(UserEmail, UserPassword), Does.Contain("cms-editor"),
            "Completing the tour did not persist its dismissal.");
    }

    [Test]
    public async Task The_case_pages_tour_teaches_publishing_from_a_case()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/organizations/{TghId}?tab=cases");
        await WaitUntilLoadedAsync();

        // The list navigates by an Open button, not an anchor.
        var firstCase = Main.GetByRole(AriaRole.Button, new() { Name = "Open", Exact = true }).First;
        await Expect(firstCase).ToBeVisibleAsync(new() { Timeout = 45_000 });
        await ClickUntilAsync(firstCase, Page.Locator("#case-tour-launch"));

        await ClickUntilAsync(Page.Locator("#case-tour-launch"), Page.Locator(".ben-tour-card"));

        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("Publishing starts in Edit");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("real name");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("media");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
        await Expect(Page.Locator(".ben-tour-card")).ToContainTextAsync("signed out");
        await Page.Locator(".ben-tour-card").GetByRole(AriaRole.Button, new() { Name = "Done" }).ClickAsync();

        Assert.That(await DismissedToursAsync(UserEmail, UserPassword), Does.Contain("public-case-pages"));
    }

    private async Task<string> DismissedToursAsync(string email, string password)
    {
        var login = await Page.APIRequest.PostAsync("http://localhost:5252/login",
            new() { DataObject = new { email, password } });
        Assert.That(login.Ok, "API login failed while reading tour state.");
        var token = (await login.JsonAsync())!.Value.GetProperty("accessToken").GetString();
        var tours = await Page.APIRequest.GetAsync("http://localhost:5252/api/me/tours",
            new() { Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" } });
        return await tours.TextAsync();
    }
}
