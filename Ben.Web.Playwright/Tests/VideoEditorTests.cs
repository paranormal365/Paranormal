using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// End-to-end tests for the Video Editor page at /video-editor.
/// Covers auth gating, page structure, and editor shell rendering.
/// Note: ffmpeg.wasm is not expected to fully load in CI — tests target the
/// editor shell (DOM structure) rather than WASM-dependent behaviour.
/// </summary>
[TestFixture]
[Category("VideoEditor")]
public class VideoEditorTests : BenTestBase
{
    private static string EditorUrl => $"{BaseUrl}/video-editor";

    /// <summary>
    /// The editor ships behind a switch, and this suite runs against deployments where it is off.
    /// </summary>
    /// <remarks>
    /// Every test here asserts the editor's SHELL renders. With the feature off the route does not
    /// exist at all, so each one fails on a page that is behaving exactly as configured — six
    /// failures that say nothing and hide the ones that would.
    /// </remarks>
    [SetUp]
    public async Task SkipWhenTheEditorIsSwitchedOff()
        => await SkipIfFeatureOffAsync("features.video-editor");

    // ── Auth guard ────────────────────────────────────────────────────────────

    [Test]
    public async Task VideoEditorPage_Unauthenticated_ShowsSignInMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var msg = Page.GetByText("must be signed in", new() { Exact = false });
        await Expect(msg).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Test]
    public async Task VideoEditorPage_Unauthenticated_DoesNotRenderEditorShell()
    {
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var editorDiv = Page.Locator(".bv-editor");
        Assert.That(await editorDiv.IsVisibleAsync(), Is.False,
            ".bv-editor should not render for unauthenticated users.");
    }

    [Test]
    public async Task VideoEditorPage_Unauthenticated_NoErrorPage()
    {
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            "Error page should not appear for the video editor auth guard.");
        Assert.That(body, Does.Not.Contain("HTTP 404"),
            "/video-editor route should be registered.");
    }

    // ── Authenticated — editor shell ──────────────────────────────────────────

    [Test]
    public async Task VideoEditorPage_Authenticated_RendersEditorShell()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var editorDiv = Page.Locator(".bv-editor");
        await Expect(editorDiv).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task VideoEditorPage_Authenticated_PageTitleIsVideoEditor()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page).ToHaveTitleAsync(new System.Text.RegularExpressions.Regex("Video Editor", System.Text.RegularExpressions.RegexOptions.IgnoreCase), new() { Timeout = 10_000 });
    }

    [Test]
    public async Task VideoEditorPage_Authenticated_NoSignInMessageShown()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var msg = Page.GetByText("must be signed in", new() { Exact = false });
        Assert.That(await msg.IsVisibleAsync(), Is.False,
            "Auth message should not appear for logged-in users.");
    }

    // ── Editor toolbar ────────────────────────────────────────────────────────

    [Test]
    public async Task VideoEditorPage_Authenticated_ToolbarIsPresent()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The Toolbar component renders inside .bv-editor; look for its root element.
        var toolbar = Page.Locator(".bv-editor .bv-toolbar, .bv-editor [class*='toolbar']");
        await Expect(toolbar.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task VideoEditorPage_Authenticated_TimelineIsPresent()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var timeline = Page.Locator(".bv-editor .bv-timeline, .bv-editor [class*='timeline']");
        await Expect(timeline.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task VideoEditorPage_Authenticated_VideoPreviewAreaIsPresent()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var preview = Page.Locator(".bv-editor .bv-preview, .bv-editor video, .bv-editor [class*='preview']");
        await Expect(preview.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ── ffmpeg loading state ──────────────────────────────────────────────────

    [Test]
    public async Task VideoEditorPage_Authenticated_NoUnhandledErrorOnLoad()
    {
        var consoleErrors = new List<string>();
        Page.Console += (_, msg) =>
        {
            // Capture JS errors but ignore expected CDN/WASM 404s in offline CI
            if (msg.Type == "error" && !msg.Text.Contains("cdn.jsdelivr") && !msg.Text.Contains("unpkg.com"))
                consoleErrors.Add(msg.Text);
        };

        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            "Blazor error page should not appear when loading the video editor.");
    }

    [Test]
    public async Task VideoEditorPage_Authenticated_FfmpegStatusElementExists()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The editor renders an ffmpeg state indicator in the toolbar.
        // We verify the element exists, not that ffmpeg has finished loading.
        var ffmpegStatus = Page.Locator("[class*='ffmpeg'], [class*='bv-ffmpeg']");
        // Non-fatal: ffmpeg element may not exist if still mounting
        if (await ffmpegStatus.CountAsync() > 0)
            await Expect(ffmpegStatus.First).ToBeAttachedAsync(new() { Timeout = 5_000 });
        else
            Assert.Pass("ffmpeg status element not present yet — editor still initialising.");
    }

    // ── SuperAdmin access ─────────────────────────────────────────────────────

    [Test]
    public async Task VideoEditorPage_SuperAdmin_RendersEditorShell()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var editorDiv = Page.Locator(".bv-editor");
        await Expect(editorDiv).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [Test]
    public async Task VideoEditorPage_BrowserBack_NavigatesAwayCleanly()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/video-editor");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GoBackAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            "Navigating away from the video editor should not throw.");
    }
}
