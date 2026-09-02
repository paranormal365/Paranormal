using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The admin screen that removes field sessions whose readings are not on this server.
/// </summary>
/// <remarks>
/// <para>Built after 32 of them were found on production: a Playwright run against a shared
/// database leaves the row in SQL and the document's bytes on the machine that ran the suite, so
/// the session exists everywhere and opens nowhere.</para>
///
/// <para>The delete test is destructive and needs a database with an orphan in it, so it only runs
/// when <c>BEN_ORPHAN_PURGE=1</c>. The gate test and the empty-state test are safe anywhere and
/// run with the rest of the suite.</para>
/// </remarks>
[TestFixture]
[Category("OrphanedSessions")]
public class OrphanedSessionsTests : BenTestBase
{
    [Test]
    public async Task An_ordinary_member_is_sent_away()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/orphaned-sessions");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The page navigates away rather than rendering a refusal: an admin tool a member cannot
        // use should not be a screen they can look at.
        await Expect(Page.Locator("[data-testid='delete-orphans']")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid='orphan-row']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_superadmin_sees_either_the_list_or_the_all_clear()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/orphaned-sessions");

        // One of the two must appear. A page that shows neither is the failure this asserts
        // against — "nothing here" and "we could not ask" are different sentences, and the screen
        // is built to always say one of them.
        await Page.WaitForSelectorAsync(
            "[data-testid='no-orphans'], [data-testid='orphan-row'], .alert-warning",
            new() { Timeout = 30_000 });

        var allClear = await Page.Locator("[data-testid='no-orphans']").CountAsync();
        var rows     = await Page.Locator("[data-testid='orphan-row']").CountAsync();
        var refusal  = await Page.Locator(".alert-warning").CountAsync();

        Assert.That(allClear + rows + refusal, Is.GreaterThan(0),
                    "the screen must say which kind of nothing it got");
        TestContext.Out.WriteLine($"all-clear={allClear} rows={rows} refusal={refusal}");

        if (Environment.GetEnvironmentVariable("BEN_ORPHAN_SHOT") is { Length: > 0 } shot)
            await Page.ScreenshotAsync(new() { Path = shot, FullPage = true });
    }

    [Test]
    public async Task The_button_deletes_them_and_the_list_empties()
    {
        if (Environment.GetEnvironmentVariable("BEN_ORPHAN_PURGE") != "1")
            Assert.Ignore("Set BEN_ORPHAN_PURGE=1 — this deletes rows.");

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/orphaned-sessions");
        await Page.WaitForSelectorAsync("[data-testid='orphan-row']", new() { Timeout = 30_000 });

        var before = await Page.Locator("[data-testid='orphan-row']").CountAsync();
        Assert.That(before, Is.GreaterThan(0), "this test needs an orphan to delete");

        await Page.Locator("[data-testid='delete-orphans']").ClickAsync();

        // The confirmation names the count rather than asking "are you sure?".
        var confirm = Page.GetByRole(AriaRole.Button, new() { Name = "Delete them" });
        await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await confirm.ClickAsync();

        await Expect(Page.Locator("[data-testid='purge-result']"))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid='purge-result']"))
            .ToContainTextAsync($"Deleted {before}");

        // And the screen agrees with the database afterwards.
        await Expect(Page.Locator("[data-testid='orphan-row']")).ToHaveCountAsync(0);
    }
}
