using System.Text.Json;
using Ben.Canvas.Playwright.Support;
using Microsoft.Playwright;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// The board and the case, end to end against the API on the disposable IsHauntedDb_e2e: Save to case creates then
/// replaces, a case board reopens from the server, a newer copy saved by somebody else becomes a choice rather than
/// an overwrite, publishing files a picture in the case, and a link card falls back to its address (R34).
/// </summary>
/// <remarks>
/// Ignored, not failed, when the API is not running, when BEN_USER_PASSWORD is not set, or when the canvas feature
/// is switched off on that database - the three things a machine without the e2e stack lacks.
/// </remarks>
[Category("Server")]
[NonParallelizable]
public sealed class CanvasServerTests : CanvasTestBase
{
    private ApiSession _api = null!;
    private Guid _org;
    private Guid _case;

    [SetUp]
    public async Task ServerAsync()
    {
        using (var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
        {
            try { (await probe.GetAsync($"{ApiUrl}/api/public/build")).EnsureSuccessStatusCode(); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Assert.Ignore($"The API is not running at {ApiUrl}. Start scripts\\run-webapi-e2e.ps1 in the Paranormal-canvas worktree.");
            }
        }

        _api = await ApiSession.SignInAsync(ApiUrl, UserEmail, UserPassword);
        // The probe is a live one — the boards endpoint either answers or it does not. It used to
        // name a site switch to turn on; that switch was removed on 2026-09-16 when research became
        // boards, so saying so would send somebody looking for something that is not there.
        if (!await _api.CanvasIsOnAsync())
            Assert.Ignore($"The API at {ApiUrl} refused api/canvas-documents for this account — check it is running and the account may read a case.");
        (_org, _case) = await _api.CaseAsync();
        foreach (var board in await _api.BoardsAsync(_case)) _api.CreatedBoards.Add(board.GetProperty("id").GetGuid());
        _baseline = _api.CreatedBoards.ToHashSet();
        _api.CreatedBoards.Clear();
    }

    private HashSet<Guid> _baseline = [];

    [TearDown]
    public async Task CleanUpAsync()
    {
        if (_api is null) return;
        foreach (var board in await _api.BoardsAsync(_case))
        {
            var id = board.GetProperty("id").GetGuid();
            if (!_baseline.Contains(id)) _api.CreatedBoards.Add(id);
        }

        await _api.DisposeAsync();
    }

    private ILocator SaveButton => Page.Locator(".bc-header [data-bc-action='save-server']");
    private ILocator SaveState => Page.Locator(".bc-savestate");

    /// <summary>Signs in, then opens the editor on the case the way the site's link does, from the address's fragment.</summary>
    private async Task OpenCaseAsync(string fragment)
    {
        await StartCleanAsync();
        await SignInAsync();
        await Page.GotoAsync($"{CanvasUrl}/#{fragment}");
        await Page.ReloadAsync();
        await WaitReadyAsync();
    }

    private async Task<Guid> SavedBoardIdAsync()
    {
        var boards = await _api.BoardsAsync(_case);
        return boards.Select(b => b.GetProperty("id").GetGuid()).Single(id => !_baseline.Contains(id));
    }

    [Test]
    public async Task Signed_out_saving_to_the_case_says_to_sign_in()
    {
        await StartCleanAsync();
        await Page.GotoAsync($"{CanvasUrl}/#case={_case}&org={_org}");
        await Page.ReloadAsync();
        await WaitReadyAsync();

        await Expect(SaveButton).ToBeDisabledAsync();
        await Expect(SaveButton).ToHaveAttributeAsync("title", "Sign in to save this board to the case.");
    }

    [Test]
    public async Task Save_to_case_creates_the_board_then_replaces_it()
    {
        await OpenCaseAsync($"case={_case}&org={_org}");
        await AddNodeAsync("card");

        await SaveButton.ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });
        var id = await SavedBoardIdAsync();
        Assert.That((await _api.BoardAsync(id)).GetProperty("revision").GetInt32(), Is.EqualTo(1));

        await AddNodeAsync("text");
        await SaveButton.ClickAsync();
        await EventuallyAsync(async () => (await _api.BoardAsync(id)).GetProperty("revision").GetInt32(), 2, 0);
        Assert.That((await _api.BoardsAsync(_case)).Count(b => !_baseline.Contains(b.GetProperty("id").GetGuid())), Is.EqualTo(1));
    }

    [Test]
    public async Task A_case_board_reopens_from_the_server_on_a_clean_device()
    {
        await OpenCaseAsync($"case={_case}&org={_org}");
        await AddNodeAsync("card");
        await AddNodeAsync("text");
        await SaveButton.ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });
        var id = await SavedBoardIdAsync();

        await OpenCaseAsync($"doc={id}&case={_case}&org={_org}");

        await Expect(Nodes).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(SaveState).ToContainTextAsync("Saved to case");
        // After the reload the header must know the person is still signed in.
        await Expect(Page.Locator(".bwc-signin")).ToContainTextAsync("Sign out");
    }

    [Test]
    public async Task A_newer_copy_saved_by_somebody_else_is_a_choice_not_an_overwrite()
    {
        await OpenCaseAsync($"case={_case}&org={_org}");
        await AddNodeAsync("card");
        await SaveButton.ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });
        var id = await SavedBoardIdAsync();

        var theirs = await _api.SaveAsSomebodyElseAsync(id);
        await AddNodeAsync("text");
        await SaveButton.ClickAsync();

        var dialog = Page.Locator("[role='dialog']", new() { HasTextString = "newer copy" });
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That((await _api.BoardAsync(id)).GetProperty("revision").GetInt32(), Is.EqualTo(theirs), "Nothing may be overwritten before the choice.");

        await dialog.Locator("[data-bc-action='conflict-mine']").ClickAsync();
        await EventuallyAsync(async () => (await _api.BoardAsync(id)).GetProperty("revision").GetInt32(), theirs + 1, 0);
        var saved = JsonDocument.Parse((await _api.BoardAsync(id)).GetProperty("documentJson").GetString()!);
        Assert.That(saved.RootElement.GetProperty("nodes").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task Publishing_files_a_picture_and_marks_the_board_published()
    {
        await OpenCaseAsync($"case={_case}&org={_org}");
        await AddNodeAsync("card");
        await SaveButton.ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });

        await Page.ClickAsync(".bc-header [data-bc-action='publish']");
        var confirm = Page.Locator("[role='dialog']", new() { HasTextString = "Publish this board?" });
        await Expect(confirm).ToContainTextAsync("only people on the case");
        await confirm.Locator("button", new() { HasTextString = "Publish" }).ClickAsync();

        await Expect(Page.Locator(".bc-toast, [role='status']", new() { HasTextString = "Published to the case." }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var board = await _api.BoardAsync(await SavedBoardIdAsync());
        Assert.That(board.GetProperty("publishedAtUtc").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(board.GetProperty("publishedUploadFileId").ValueKind, Is.EqualTo(JsonValueKind.String));
    }

    /// <summary>
    /// Following a link across boards leaves a path in the header, and any crumb on it goes back.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-18:</b> "Instead of a back button, what about creating breadcrumbs to
    /// navigate."</para>
    ///
    /// <para><b>Why this is worth its runtime.</b> <c>BoardTrailTests</c> holds the arithmetic of the
    /// path — what a crumb drops and what it opens — but nothing below a browser can hold that the
    /// header is TOLD about it. Following a link opens a board, saves, re-renders and redraws the
    /// header in one go; this is the only level at which "the crumb appeared, and clicking it went
    /// back" is a fact rather than an inference. Seen failing with the path not handed to the
    /// header.</para>
    /// </remarks>
    [Test]
    public async Task Following_a_link_leaves_a_path_in_the_header()
    {
        // Two published boards on the case: one to link from, one to link to.
        await OpenCaseAsync($"case={_case}&org={_org}");
        await AddNodeAsync("card");
        await PublishAsync();
        var target = await SavedBoardIdAsync();
        var targetTitle = (await _api.BoardAsync(target)).GetProperty("name").GetString();

        var crumbs = Page.Locator(".bc-header__crumb");
        await Expect(crumbs).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // A card on THIS board that opens that one.
        await Page.ClickAsync(".bc-rail [data-bc-action='case-boards']");
        await Page.Locator("[role='dialog'] button", new() { HasTextString = targetTitle! }).First.ClickAsync();
        await Page.Locator("[role='dialog'] button", new() { HasTextString = "The whole board" }).First.ClickAsync();

        var card = Page.Locator(".bc-node--board");
        await Expect(card).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await card.Locator("button", new() { HasTextString = "Open" }).First.ClickAsync();

        // The path names where the link was followed FROM, and the title is where it led.
        await Expect(crumbs).ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Expect(crumbs.First).ToHaveTextAsync(targetTitle!);

        // And the crumb goes back, clearing the path behind it.
        await crumbs.First.ClickAsync();
        await Expect(crumbs).ToHaveCountAsync(0, new() { Timeout = 20_000 });

        // Settled before the test ends. A crumb opens a board, and leaving that request in flight
        // hands the NEXT test a page that is still navigating — which is how this fixture's other
        // tests started timing out waiting for a Save button that had not been drawn yet.
        await Expect(Page.Locator(".bc-header__title")).ToHaveTextAsync(targetTitle!, new() { Timeout = 20_000 });
        await Expect(SaveState).Not.ToContainTextAsync("Saving", new() { Timeout = 20_000 });
    }

    private async Task PublishAsync()
    {
        await SaveButton.ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });
        await Page.ClickAsync(".bc-header [data-bc-action='publish']");
        var confirm = Page.Locator("[role='dialog']", new() { HasTextString = "Publish this board?" });
        await confirm.Locator("button", new() { HasTextString = "Publish" }).ClickAsync();
        await Expect(Page.Locator(".bc-toast, [role='status']", new() { HasTextString = "Published to the case." }).First)
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>Screenshots of the case flow for the milestone report. Opt-in with BEN_CANVAS_WALK=1.</summary>
    [Test]
    [Category("Capture")]
    public async Task Capture_the_case_flow()
    {
        if (Environment.GetEnvironmentVariable("BEN_CANVAS_WALK") != "1") Assert.Ignore("Set BEN_CANVAS_WALK=1 to capture the case flow.");

        await OpenCaseAsync($"case={_case}&org={_org}");
        var card = await AddNodeAsync("card");
        await DragAsync(card, -300, -120);
        await Board.FocusAsync();
        await Page.PasteAsync([new Flavour("text/plain", "https://no-such-host.ishaunted-canvas.invalid/cases/42")]);
        await Expect(Page.Locator(".bc-node--link")).ToHaveCountAsync(1);
        await SaveButton.ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });
        await Page.Mouse.ClickAsync(1100, 700);
        await ShotAsync("m6-saved-to-case");

        var id = await SavedBoardIdAsync();
        await _api.SaveAsSomebodyElseAsync(id);
        await AddNodeAsync("text");
        await SaveButton.ClickAsync();
        await Expect(Page.Locator("[role='dialog']", new() { HasTextString = "newer copy" })).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await ShotAsync("m6-newer-copy-choice");
        await Page.Locator("[data-bc-action='conflict-mine']").ClickAsync();
        await Expect(SaveState).ToContainTextAsync("Saved to case", new() { Timeout = 15_000 });

        await Page.ClickAsync(".bc-header [data-bc-action='publish']");
        await Expect(Page.Locator("[role='dialog']", new() { HasTextString = "Publish this board?" })).ToBeVisibleAsync();
        await ShotAsync("m6-publish-confirm");
    }

    [Test]
    public async Task A_link_with_no_preview_keeps_its_site_and_address()
    {
        await OpenCaseAsync($"case={_case}&org={_org}");
        await Board.FocusAsync();
        await Page.PasteAsync([new Flavour("text/plain", "https://no-such-host.ishaunted-canvas.invalid/cases/42")]);

        var card = Page.Locator(".bc-node--link");
        await Expect(card).ToHaveCountAsync(1);
        await Expect(card).ToContainTextAsync("no-such-host.ishaunted-canvas.invalid", new() { Timeout = 15_000 });
        await Expect(card).Not.ToContainTextAsync("error", new() { IgnoreCase = true });
    }
}
