using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Editing, driven the way a person drives it.
/// </summary>
/// <remarks>
/// <para>Six phases of the 2026-09-05 audit each found something on screen that a green suite had
/// passed over, and several of those were introduced in the same phase that found them: a control
/// that never rendered, a gate that excluded the case it was written for, a comment in the wrong
/// place that took the editor down. The service layer has some 2,300 facts about it; the layer
/// between a person and those services had almost none.</para>
///
/// <para>So this fixture presses things. It imports real media, splits, annotates, plays, exports
/// and reopens — each assertion about something a person would notice, not about a method
/// returning what it was told to return (2026-09-05 audit, F19).</para>
///
/// <para>It runs against the standalone host rather than the site because that host needs no
/// sign-in to edit, which keeps the fixture about editing.</para>
/// </remarks>
[TestFixture]
[Category("Wasm")]
[NonParallelizable]
public class WasmEditorEditingTests : BenTestBase
{
    private static string WasmUrl =>
        Environment.GetEnvironmentVariable("BEN_WASM_URL") ?? "http://localhost:5180";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
    };

    /// <summary>Skips the fixture when the WebAssembly host is not running.</summary>
    /// <remarks>
    /// A missing host is a missing precondition, not a failure: without this, eight tests fail in
    /// about ninety milliseconds saying nothing about the code and burying whatever does.
    /// </remarks>
    [OneTimeSetUp]
    public async Task SkipWhenTheWasmHostIsNotRunning()
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await probe.GetAsync(WasmUrl);
            if (!response.IsSuccessStatusCode)
                Assert.Ignore($"The WebAssembly editor host at {WasmUrl} answered {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Assert.Ignore(
                $"The WebAssembly editor host is not running at {WasmUrl}. "
                + "Start it with: dotnet run --project Ben.Wasm.Video --urls http://localhost:5180");
        }
    }

    // ── Getting to a timeline with something on it ────────────────────────────

    private static string FixtureMedia(string fileName)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Media", fileName);
        Assert.That(File.Exists(path), $"Fixture media is missing from the test output: {path}");
        return path;
    }

    /// <summary>
    /// Opens the editor with empty storage, so one test's project never decides another's outcome.
    /// </summary>
    /// <remarks>
    /// The editor autosaves and reopens the last project on load (phase 5), which is exactly right
    /// for a person and poison for a suite: the second test would start with the first test's
    /// timeline. Clearing before navigating is what makes each of these independent.
    /// </remarks>
    private async Task StartCleanAsync()
    {
        await Page.GotoAsync(WasmUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.EvaluateAsync(@"async () => {
            localStorage.clear();
            try {
                const root = await navigator.storage.getDirectory();
                for await (const [name] of root.entries())
                    await root.removeEntry(name, { recursive: true });
            } catch { /* no OPFS in this browser — nothing cached to clear */ }
        }");

        await Page.GotoAsync(WasmUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.Locator(".bv-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Starts the engine and waits for it, which every edit below needs.</summary>
    private async Task EnsureEngineReadyAsync()
    {
        var initialize = Page.GetByRole(AriaRole.Button, new() { Name = "Initialize" });
        if (await initialize.CountAsync() > 0)
            await initialize.First.ClickAsync();

        await Expect(Page.Locator(".bv-toolbar__status"))
            .ToContainTextAsync("Ready", new() { Timeout = 180_000 });
    }

    private async Task ImportAsync(string fileName)
    {
        var input = Page.Locator("#bv-file-input");
        if (await input.CountAsync() == 0)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Open" }).First.ClickAsync();
            await Expect(input).ToBeAttachedAsync(new() { Timeout = 15_000 });
        }

        await input.SetInputFilesAsync(FixtureMedia(fileName));
    }

    /// <summary>An editor with one video clip on the timeline and the engine running.</summary>
    private async Task ReadyWithOneClipAsync()
    {
        await StartCleanAsync();
        await EnsureEngineReadyAsync();
        await ImportAsync("porch-camera.mp4");
        await Expect(Page.Locator(".bv-clip-chip")).ToHaveCountAsync(1, new() { Timeout = 120_000 });
    }

    private ILocator Chips => Page.Locator(".bv-clip-chip");

    /// <summary>
    /// Puts the playhead in the middle of the first clip.
    /// </summary>
    /// <remarks>
    /// The ruler spans the whole timeline including the track-label gutter on its left, so a click
    /// at a fixed offset lands wherever that gutter happens to end — which for the first clip is
    /// its very start, where a split is a no-op. Written that way first, and both split tests
    /// failed on a timeline that was behaving correctly. Aiming at the chip's own midpoint is
    /// independent of the gutter's width, the zoom level and the clip's length.
    /// </remarks>
    private async Task SeekIntoTheFirstClipAsync()
    {
        var chip  = await Chips.First.BoundingBoxAsync();
        var ruler = Page.Locator(".bv-timeline__ruler");
        var rulerBox = await ruler.BoundingBoxAsync();

        Assert.That(chip, Is.Not.Null, "There is no clip to seek into.");
        Assert.That(rulerBox, Is.Not.Null, "The timeline has no ruler to click.");

        await ruler.ClickAsync(new()
        {
            Position = new()
            {
                X = (float)(chip!.X + chip.Width / 2 - rulerBox!.X),
                Y = (float)(rulerBox.Height / 2),
            },
        });
    }

    // ── Importing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The chip reports the fixture's real length, which only a working engine can produce.
    /// </summary>
    /// <remarks>
    /// The exact number rather than "not zero". A clip that failed to decode still gets a chip, and
    /// a chip with some plausible-looking duration on it is exactly the kind of thing a weaker
    /// assertion waves through. porch-camera.mp4 is eight seconds; ffprobe says so and so must the
    /// editor.
    /// </remarks>
    [Test]
    [Description("A video file becomes a chip on the timeline showing its true length.")]
    public async Task ImportingAVideoPutsItOnTheTimeline()
    {
        await ReadyWithOneClipAsync();

        await Expect(Page.Locator(".bv-clip-chip__dur").First)
            .ToHaveTextAsync("0:08.0", new() { Timeout = 60_000 });
    }

    [Test]
    [Description("An audio file lands on an audio track rather than being decoded and dropped.")]
    public async Task ImportingAudioPutsItOnAnAudioTrack()
    {
        await StartCleanAsync();
        await EnsureEngineReadyAsync();
        await ImportAsync("basement-evp.m4a");

        // The clip that never arrived: on this host an mp3 used to be decoded, reported "Done" and
        // then orphaned, with no audio track to land on (2026-09-05 audit, F2).
        await Expect(Page.Locator(".bv-clip-chip--audio")).ToHaveCountAsync(1, new() { Timeout = 120_000 });
    }

    [Test]
    [Description("A still image imports and can be placed.")]
    public async Task ImportingAnImageProducesAClip()
    {
        await StartCleanAsync();
        await EnsureEngineReadyAsync();
        await ImportAsync("site-photo.jpg");

        await Expect(Chips).ToHaveCountAsync(1, new() { Timeout = 120_000 });
    }

    // ── Cutting ───────────────────────────────────────────────────────────────

    [Test]
    [Description("Splitting at the playhead makes two clips out of one.")]
    public async Task SplittingAtThePlayheadMakesTwoClips()
    {
        await ReadyWithOneClipAsync();

        await Chips.First.ClickAsync();
        await SeekIntoTheFirstClipAsync();
        await Page.Keyboard.PressAsync("s");

        await Expect(Chips).ToHaveCountAsync(2, new() { Timeout = 30_000 });
    }

    [Test]
    [Description("Undo takes the split back, and one press is enough.")]
    public async Task UndoingASplitPutsTheClipBack()
    {
        await ReadyWithOneClipAsync();

        await Chips.First.ClickAsync();
        await SeekIntoTheFirstClipAsync();
        await Page.Keyboard.PressAsync("s");
        await Expect(Chips).ToHaveCountAsync(2, new() { Timeout = 30_000 });

        await Page.Keyboard.PressAsync("Control+z");

        await Expect(Chips).ToHaveCountAsync(1, new() { Timeout = 30_000 });
    }

    // ── Annotating ────────────────────────────────────────────────────────────

    [Test]
    [Description("A marker lands on the ruler.")]
    public async Task AMarkerAppearsOnTheRuler()
    {
        await ReadyWithOneClipAsync();

        await Page.GetByTitle("Add marker at playhead (M)").ClickAsync();

        await Expect(Page.Locator(".bv-marker-flag")).ToHaveCountAsync(1, new() { Timeout = 15_000 });
    }

    [Test]
    [Description("A callout gets its own chip, so its timing can be edited like a clip's.")]
    public async Task ACalloutGetsItsOwnChip()
    {
        await ReadyWithOneClipAsync();
        var before = await Chips.CountAsync();

        await Page.GetByTitle("Add callout shape (rectangle, ellipse, arrow…)").ClickAsync();

        await Expect(Chips).ToHaveCountAsync(before + 1, new() { Timeout = 15_000 });
    }

    [Test]
    [Description("A title gets its own chip too.")]
    public async Task ATitleGetsItsOwnChip()
    {
        await ReadyWithOneClipAsync();
        var before = await Chips.CountAsync();

        await Page.GetByTitle("Add text overlay").ClickAsync();

        await Expect(Chips).ToHaveCountAsync(before + 1, new() { Timeout = 15_000 });
    }

    [Test]
    [Description("Deleting a selected clip removes it, and undo brings it back.")]
    public async Task DeletingAClipCanBeUndone()
    {
        await ReadyWithOneClipAsync();

        await Chips.First.ClickAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Expect(Chips).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        await Page.Keyboard.PressAsync("Control+z");
        await Expect(Chips).ToHaveCountAsync(1, new() { Timeout = 15_000 });
    }

    /// <summary>
    /// Duplicating works on an annotation, not only on a clip.
    /// </summary>
    /// <remarks>
    /// Only video and audio could be duplicated, so three matching callouts meant building each
    /// from scratch (2026-09-05 audit, callouts-15).
    /// </remarks>
    [Test]
    [Description("Ctrl+D copies a selected callout.")]
    public async Task DuplicatingACalloutMakesASecondOne()
    {
        await ReadyWithOneClipAsync();
        await Page.GetByTitle("Add callout shape (rectangle, ellipse, arrow…)").ClickAsync();

        var afterAdding = await Chips.CountAsync();
        await Page.Locator(".bv-clip-chip--callout").First.ClickAsync();
        await Page.Keyboard.PressAsync("Control+d");

        await Expect(Chips).ToHaveCountAsync(afterAdding + 1, new() { Timeout = 15_000 });
    }

    // ── Keeping the work ──────────────────────────────────────────────────────

    /// <summary>
    /// Reopening restores the timeline <i>and</i> its footage.
    /// </summary>
    /// <remarks>
    /// The single most valuable assertion in this file. Since the media bin was introduced, no
    /// placed clip's media had ever come back after a reload — the project restored, the file sat
    /// in storage, and every chip said "missing" (2026-09-05 audit, found on screen in phase 5).
    /// Nothing in any suite would have noticed.
    /// </remarks>
    [Test]
    [Description("After a reload the clip is back and its media is not missing.")]
    public async Task ReloadingRestoresTheClipAndItsFootage()
    {
        await ReadyWithOneClipAsync();

        // Autosave runs a couple of seconds after editing stops.
        await Page.WaitForTimeoutAsync(4_000);
        await Page.ReloadAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Chips).ToHaveCountAsync(1, new() { Timeout = 120_000 });
        await Expect(Page.Locator(".bv-clip-chip--missing")).ToHaveCountAsync(0, new() { Timeout = 120_000 });
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    /// <summary>
    /// An export runs to completion and offers somewhere to put the result.
    /// </summary>
    /// <remarks>
    /// Signed out the server option is offered disabled with a reason rather than hidden, because
    /// an absent option reads as an editor that cannot publish rather than one that is a sign-in
    /// away (2026-09-05 audit, F12).
    /// </remarks>
    [Test]
    [Description("Export Now finishes and asks where the file should go.")]
    public async Task ExportingProducesAFileAndAsksWhereItGoes()
    {
        await ReadyWithOneClipAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Export" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export Now" }).ClickAsync();

        var prompt = Page.Locator(".bv-export-destination-prompt");
        await Expect(prompt).ToBeVisibleAsync(new() { Timeout = 600_000 });
        await Expect(prompt).ToContainTextAsync("is ready");

        var upload = prompt.GetByRole(AriaRole.Button, new() { Name = "Upload to server" });
        Assert.That(await upload.IsDisabledAsync(), Is.True,
            "Signed out, the server destination must be offered and refused, not hidden.");
    }

    // ── The engine ────────────────────────────────────────────────────────────

    /// <summary>
    /// The engine comes from this app, not from somebody else's CDN.
    /// </summary>
    /// <remarks>
    /// <para>Thirty megabytes of WebAssembly used to be fetched from cdn.jsdelivr.net at every
    /// load, so an editor whose whole point is that footage stays local could not start when that
    /// CDN was having a bad morning (2026-09-05 audit, media-13).</para>
    ///
    /// <para>Loopback does not count as off-site, and that exclusion is not a convenience: the
    /// sidecar is a companion app on this same machine, reached over loopback, and probing for it
    /// is a feature. This assertion is about third parties. It was written without the exclusion
    /// and failed on the sidecar probe the first time it ran, which is the test doing its job to
    /// its author.</para>
    /// </remarks>
    [Test]
    [Description("Starting the engine touches no third-party host.")]
    public async Task TheEngineIsServedByTheAppItself()
    {
        var offsite = new List<string>();
        var core = new List<string>();

        Page.Request += (_, request) =>
        {
            if (IsThirdParty(request.Url)) offsite.Add(request.Url);
            if (request.Url.Contains("ffmpeg-core", StringComparison.OrdinalIgnoreCase))
                core.Add(request.Url);
        };

        await StartCleanAsync();
        await EnsureEngineReadyAsync();

        Assert.That(core, Is.Not.Empty, "The engine started without fetching a core at all.");
        Assert.That(offsite, Is.Empty,
            "The editor reached a third party while starting:\n  " + string.Join("\n  ", offsite));
    }

    /// <summary>Whether a URL leaves this machine.</summary>
    // ── The live player (phase 12, decision D5) ───────────────────────────────

    /// <summary>Switches the preview from the rendered proxy to the sequence player.</summary>
    private async Task SwitchToLivePlaybackAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Live", Exact = true }).First.ClickAsync();
        await Expect(Page.Locator(".bv-live")).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    [Description("The live player finds a clip's media even when the bin holds the stored copy.")]
    public async Task LivePlaybackFindsTheMediaForAPlacedClip()
    {
        await ReadyWithOneClipAsync();
        await SwitchToLivePlaybackAsync();

        // The player looked only under the clip's own id and played black for a clip that was
        // plainly on the timeline, because a clip placed from the bin shares the bin entry's stored
        // copy rather than making a second one. Found by opening the page (phase 12).
        await Expect(Page.Locator(".bv-live__warning")).ToHaveCountAsync(0, new() { Timeout = 30_000 });

        var loaded = await Page.EvaluateAsync<bool>(
            "() => [...document.querySelectorAll('.bv-live__video')].some(v => v.src.startsWith('blob:'))");

        Assert.That(loaded, Is.True, "no source was loaded into either video element");
    }

    [Test]
    [Description("Pressing play in live mode moves the playhead and the picture with it.")]
    public async Task LivePlaybackActuallyPlays()
    {
        await ReadyWithOneClipAsync();
        await SwitchToLivePlaybackAsync();

        await Page.Locator(".bv-live__transport button").First.ClickAsync();

        // The whole promise of the live player: this happens with no render in between, so a
        // second and a half is a long time to wait for it.
        await Expect(Page.Locator(".bv-live__clock")).Not.ToContainTextAsync("0:00 /", new() { Timeout = 15_000 });

        var elapsed = await Page.EvaluateAsync<double>(
            "() => { const v = [...document.querySelectorAll('.bv-live__video')]"
            + ".find(x => x.style.display !== 'none'); return v ? v.currentTime : 0; }");

        Assert.That(elapsed, Is.GreaterThan(0), "the picture did not move with the playhead");
    }

    [Test]
    [Description("Switching back to the rendered preview leaves nothing playing behind it.")]
    public async Task LeavingLivePlaybackStopsIt()
    {
        await ReadyWithOneClipAsync();
        await SwitchToLivePlaybackAsync();
        await Page.Locator(".bv-live__transport button").First.ClickAsync();
        await Expect(Page.Locator(".bv-live__clock")).Not.ToContainTextAsync("0:00 /", new() { Timeout = 15_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Rendered", Exact = true }).First.ClickAsync();
        await Expect(Page.Locator(".bv-live")).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        // A player nobody can see, still playing, is a second sound and a second clock.
        var stillPlaying = await Page.EvaluateAsync<bool>(
            "() => [...document.querySelectorAll('video, audio')].some(m => !m.paused)");

        Assert.That(stillPlaying, Is.False, "something was left playing after the switch");
    }

    private static bool IsThirdParty(string url)
    {
        if (url.StartsWith("data:") || url.StartsWith("blob:")) return false;
        if (url.StartsWith(WasmUrl, StringComparison.OrdinalIgnoreCase)) return false;

        return !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback;
    }
}
