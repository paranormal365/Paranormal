using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The page that takes an e2e run's posts off the live feed.
/// </summary>
/// <remarks>
/// The hide itself runs only when <c>BEN_TEST_POSTS_HIDE=1</c>: it changes what everybody sees on
/// the feed of whatever database the suite is pointed at, and on the shared database that is the
/// live site. The gates and the selection are proved unconditionally.
/// </remarks>
[TestFixture]
[Category("TestPosts")]
public class TestPostsTests : BenTestBase
{
    [Test]
    public async Task A_superadmin_sees_either_the_list_or_the_all_clear()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/test-posts");
        await Page.WaitForSelectorAsync(
            "[data-testid='test-post-row'], [data-testid='no-test-posts'], [data-testid='test-posts-refusal']",
            new() { Timeout = 30_000 });

        await Expect(Page.Locator("[data-testid='test-posts-refusal']")).ToHaveCountAsync(0);

        var rows = await Page.Locator("[data-testid='test-post-row']").CountAsync();
        if (rows == 0) { await Expect(Page.Locator("[data-testid='no-test-posts']")).ToBeVisibleAsync(); return; }

        // Nothing is chosen until somebody chooses it, so the hide button starts out inert.
        await Expect(Page.Locator("[data-testid='hide-posts']")).ToBeDisabledAsync();
        await Page.Locator("[data-testid='check-all']").CheckAsync();
        // Blazor re-renders the row boxes on the server's reply, so this is a wait, not a read.
        await Expect(Page.Locator("[data-testid='test-post-check']:checked")).ToHaveCountAsync(rows);
    }

    [Test]
    public async Task An_ordinary_member_is_sent_away()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/test-posts");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("[data-testid='hide-posts']")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid='test-post-row']")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Hide one post, prove it left the feed, put it back, prove it returned. The feed is read as
    /// a visitor, because that is who the debris was being shown to.
    /// </summary>
    [Test]
    public async Task Hiding_a_post_takes_it_off_the_feed_and_unhiding_brings_it_back()
    {
        if (Environment.GetEnvironmentVariable("BEN_TEST_POSTS_HIDE") != "1")
            Assert.Ignore("Set BEN_TEST_POSTS_HIDE=1 — this changes the feed of the database it runs against.");

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/test-posts");
        await Page.WaitForSelectorAsync("[data-testid='test-post-row'], [data-testid='no-test-posts']",
                                        new() { Timeout = 30_000 });

        var visible = Page.Locator("[data-testid='test-post-row']:has([data-testid='state-visible'])");
        if (await visible.CountAsync() == 0) Assert.Ignore("no visible test post to hide");

        var body = (await visible.First.Locator("[data-testid='test-post-body']").InnerTextAsync()).Trim();
        var key  = body.Length > 40 ? body[..40] : body;
        TestContext.Out.WriteLine("hiding: " + key);

        await visible.First.Locator("[data-testid='test-post-check']").CheckAsync();
        await Page.Locator("[data-testid='hide-posts']").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Hide them" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='hide-result']")).ToContainTextAsync("Hid 1 post", new() { Timeout = 30_000 });

        // Gone for a visitor.
        var visitor = await Browser.NewContextAsync();
        var feed = await visitor.NewPageAsync();
        await feed.GotoAsync($"{BaseUrl}/feed");
        await feed.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await feed.WaitForTimeoutAsync(1500);
        Assert.That(await feed.GetByText(key, new() { Exact = false }).CountAsync(), Is.EqualTo(0),
            "the hidden post is still on the feed");
        await visitor.CloseAsync();

        // And back.
        var hidden = Page.Locator("[data-testid='test-post-row']:has([data-testid='state-hidden'])")
                         .Filter(new() { HasText = key });
        await hidden.First.Locator("[data-testid='test-post-check']").CheckAsync();
        await Page.Locator("[data-testid='unhide-posts']").ClickAsync();
        await Expect(Page.Locator("[data-testid='hide-result']")).ToContainTextAsync("Put 1 post back", new() { Timeout = 30_000 });
    }
}
