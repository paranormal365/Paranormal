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
/// <para>The tests that choose and delete make their own orphans: a session sent the way the phone
/// sends it, whose stored file is then removed from the uploads folder the run gave the API. They
/// choose and delete only those, by session id, so they are safe on any database. Until 10/27/2026
/// they waited for <c>BEN_ORPHAN_PURGE=1</c> and a database that happened to hold orphans, so they
/// skipped on every run.</para>
/// </remarks>
[TestFixture]
[Category("OrphanedSessions")]
public class OrphanedSessionsTests : BenTestBase
{
    /// <summary>
    /// A session of the seat's own whose stored file is gone: an orphan, made on purpose.
    /// </summary>
    /// <remarks>
    /// The send names a new device session each time, so it is always a new session with a new file.
    /// The file is found as the one that appeared in the seat's session folder during the send; the
    /// tests run one at a time, so nothing else is writing there.
    /// </remarks>
    private async Task<string> MakeAnOrphanAsync()
    {
        var uploads = Environment.GetEnvironmentVariable("BEN_E2E_UPLOADS");
        if (string.IsNullOrEmpty(uploads))
            Assert.Ignore("Run through scripts/run-e2e.sh, which tells the tests where the API keeps its files.");

        await using var api = await ApiSignedInAsync(UserEmail, UserPassword);
        var me = (await (await api.GetAsync("/api/me/profile")).JsonAsync())!.Value.GetProperty("appUserId").GetString()!;
        var folder = Path.Combine(uploads, "users", Guid.Parse(me).ToString(), "field-sessions");
        string[] Files() => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.ben") : [];
        var before = Files();

        var form = Context.APIRequest.CreateFormData();
        form.Append("file", new FilePayload
        {
            Name = "field-session.ben", MimeType = "application/vnd.ishaunted.field-session",
            Buffer = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "field-session-stage.ben")),
        });
        form.Append("deviceSessionId", Guid.NewGuid().ToString());
        var sent = await api.PostAsync("/api/field-sessions/bundle", new() { Multipart = form });
        Assert.That(sent.Ok, Is.True, "the session to orphan was not accepted: " + await sent.TextAsync());
        var sessionId = (await sent.JsonAsync())!.Value.GetProperty("id").GetString()!;

        var added = Files().Except(before).ToList();
        Assert.That(added, Has.Count.EqualTo(1), "the send should have stored exactly one new file");
        File.Delete(added[0]);
        return sessionId;
    }

    private ILocator OrphanRow(string sessionId)
        => Page.Locator($"[data-testid='orphan-row'][data-session-id='{sessionId}']");

    [Test]
    public async Task One_row_can_be_chosen_on_its_own()
    {
        var first = await MakeAnOrphanAsync();
        await MakeAnOrphanAsync();

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/orphaned-sessions");
        await Expect(OrphanRow(first)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.That(await Page.Locator("[data-testid='orphan-row']").CountAsync(), Is.GreaterThanOrEqualTo(2));

        await WaitForTheCircuitAsync();
        await OrphanRow(first).Locator("[data-testid='orphan-check']").CheckAsync();
        await Expect(Page.Locator("[data-testid='delete-orphans']")).ToContainTextAsync("1 selected");
    }

    [Test]
    public async Task An_ordinary_member_is_sent_away()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/orphaned-sessions");

        // The proof is being sent somewhere else. Waiting for the network to go quiet timed out on a
        // busy run, because a live page's connection never does, and the checks below passed before
        // the page had drawn anything (release e2e run, 10/27/2026).
        await Page.WaitForURLAsync(url => !url.Contains("/admin/orphaned-sessions", StringComparison.OrdinalIgnoreCase),
                                   new() { Timeout = 30_000 });

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
        // Its own two orphans, chosen by id: whatever else is on the list is left alone.
        string[] mine = [await MakeAnOrphanAsync(), await MakeAnOrphanAsync()];
        var before = mine.Length;

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/orphaned-sessions");
        foreach (var id in mine) await Expect(OrphanRow(id)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await WaitForTheCircuitAsync();

        // Nothing is chosen yet, so the button must refuse to be pressed. A delete that defaults
        // to "everything" is the version of this screen that deletes something Ben wanted to keep.
        await Expect(Page.Locator("[data-testid='delete-orphans']")).ToBeDisabledAsync();

        foreach (var id in mine) await OrphanRow(id).Locator("[data-testid='orphan-check']").CheckAsync();
        await Expect(Page.Locator("[data-testid='delete-orphans']")).ToBeEnabledAsync();
        await Expect(Page.Locator("[data-testid='delete-orphans']")).ToContainTextAsync($"{before}");

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
        foreach (var id in mine) await Expect(OrphanRow(id)).ToHaveCountAsync(0);
    }
}
