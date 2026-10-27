using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The page that takes an e2e run's posts off the live feed.
/// </summary>
/// <remarks>
/// The hide test writes its own post as a seeded member, hides and restores only that one, and so
/// leaves every feed as it found it. Until 10/27/2026 it hid whichever seeded post came first and
/// waited for <c>BEN_TEST_POSTS_HIDE=1</c>, so it skipped on every run.
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

        // The proof is being sent somewhere else. Waiting for the network to go quiet timed out on a
        // busy run, because a live page's connection never does, and the checks below passed before
        // the page had drawn anything (release e2e run, 10/27/2026).
        await Page.WaitForURLAsync(url => !url.Contains("/admin/test-posts", StringComparison.OrdinalIgnoreCase),
                                   new() { Timeout = 30_000 });

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
        // A post of its own, by a seeded account, so it is on the list and nobody else's is touched.
        var key = $"Hide-and-return check {Guid.NewGuid():N}"[..34];
        await using (var member = await ApiSignedInAsync(MemberEmail, MemberPassword))
        {
            var form = Context.APIRequest.CreateFormData();
            form.Set("body", key);
            var made = await member.PostAsync("/api/feed/posts", new() { Multipart = form });
            Assert.That(made.Ok, Is.True, $"the post to hide was refused: {made.Status} {await made.TextAsync()}");
        }

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/test-posts");
        var mine = Page.Locator("[data-testid='test-post-row']").Filter(new() { HasText = key });
        await Expect(mine.Locator("[data-testid='state-visible']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await WaitForTheCircuitAsync();

        // It is on the feed before it is hidden, or "gone" below would prove nothing.
        var before = await Browser.NewContextAsync();
        var feedBefore = await before.NewPageAsync();
        await feedBefore.GotoAsync($"{BaseUrl}/feed");
        await Expect(feedBefore.GetByText(key, new() { Exact = false }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await before.CloseAsync();

        await mine.Locator("[data-testid='test-post-check']").CheckAsync();
        await Page.Locator("[data-testid='hide-posts']").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Hide them" }).ClickAsync();
        await Expect(Page.Locator("[data-testid='hide-result']")).ToContainTextAsync("Hid 1 post", new() { Timeout = 30_000 });

        // Gone for a visitor.
        var visitor = await Browser.NewContextAsync();
        var feed = await visitor.NewPageAsync();
        await feed.GotoAsync($"{BaseUrl}/feed");
        await Expect(feed.Locator(".bv-feed-post").First.Or(feed.GetByText("Nothing here yet")))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await feed.GetByText(key, new() { Exact = false }).CountAsync(), Is.EqualTo(0),
            "the hidden post is still on the feed");
        await visitor.CloseAsync();

        // And back.
        await Expect(mine.Locator("[data-testid='state-hidden']")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await mine.Locator("[data-testid='test-post-check']").CheckAsync();
        await Page.Locator("[data-testid='unhide-posts']").ClickAsync();
        await Expect(Page.Locator("[data-testid='hide-result']")).ToContainTextAsync("Put 1 post back", new() { Timeout = 30_000 });
    }
}
