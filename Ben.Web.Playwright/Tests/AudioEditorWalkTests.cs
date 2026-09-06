using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Uses the audio editor the way a person does, and writes down what happened.
/// </summary>
/// <remarks>
/// <para>Not a test suite in the usual sense: nothing here stops at the first surprise. Each case
/// records a verdict per observation into <c>ProjectNotes/AudioEditor-Walk-2026-09-06/</c> with a
/// screenshot, so the audit can be re-ranked from what was seen rather than from what the code
/// says (2026-09-06 audio audit, phase 0). The letters in the verdicts are the finding ids from
/// <c>ProjectNotes/AudioEditor-Audit-2026-09-06-Plan.md</c>.</para>
///
/// <para><c>[Explicit]</c>, because it uploads a file per case and takes minutes; it is run by
/// name on the isolated stack, never as part of the suite.</para>
/// </remarks>
[TestFixture]
[Category("AudioEditorWalk")]
[Explicit("Phase 0 walk — run on the isolated stack with --filter TestCategory=AudioEditorWalk")]
public class AudioEditorWalkTests : BenTestBase
{
    private static readonly string TestAudioPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    private static readonly string OutputDir = Path.Combine(RepoRoot(), "ProjectNotes", "AudioEditor-Walk-2026-09-06");
    private static readonly string VerdictsPath = Path.Combine(OutputDir, "verdicts.md");

    private string _caseUrl = string.Empty;

    // ── Recording ─────────────────────────────────────────────────────────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Web.Playwright")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static void Record(string id, string verdict, string note)
    {
        Directory.CreateDirectory(OutputDir);
        File.AppendAllText(VerdictsPath,
            $"| {DateTime.Now:HH:mm:ss} | {id} | **{verdict}** | {note.Replace("|", "/")} |\n", Encoding.UTF8);
        TestContext.Out.WriteLine($"[{id}] {verdict} — {note}");
    }

    private async Task SnapAsync(string name)
    {
        Directory.CreateDirectory(OutputDir);
        try
        {
            await Page.ScreenshotAsync(new() { Path = Path.Combine(OutputDir, $"{name}.png"), FullPage = false });
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"screenshot {name} failed: {ex.Message}");
        }
    }

    [OneTimeSetUp]
    public void OpenVerdicts()
    {
        Directory.CreateDirectory(OutputDir);
        if (!File.Exists(VerdictsPath))
            File.WriteAllText(VerdictsPath,
                "# Audio editor walk — verdicts\n\n| time | finding | verdict | observed |\n|---|---|---|---|\n");
    }

    // ── Getting to the editor ─────────────────────────────────────────────────

    private async Task<bool> ReachCaseFilesAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;
        _caseUrl = Page.Url;
        // The upload input is display:none behind a label, so waiting for IT to be visible times
        // out on a tab that opened perfectly — the trap the existing AudioScrubModeTests fell into.
        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);
        await Expect(Page.Locator("#case-file-upload")).ToBeAttachedAsync(new() { Timeout = 15_000 });
        return true;
    }

    private async Task<bool> UploadFixtureAsync()
    {
        await Page.Locator("#case-file-upload").SetInputFilesAsync(TestAudioPath);
        var waveform = Page.Locator("[id^='ws-']").First;
        try { await Expect(waveform).ToBeVisibleAsync(new() { Timeout = 60_000 }); }
        catch { return false; }
        return true;
    }

    private ILocator Modal => Page.Locator(".modal.show").First;
    private ILocator ModalWaveform => Modal.Locator("[id^='ws-']").First;
    private ILocator Regions => Modal.Locator("[part~='region']");

    private async Task OpenFullViewAsync()
    {
        var wrapper = Page.Locator("[id^='afp-']").First;
        await wrapper.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Open Full View", new() { Exact = false }).ClickAsync();
        await Expect(Modal).ToBeVisibleAsync(new() { Timeout = 15_000 });
        // The toolbar enables once the modal's own player reports ready.
        // Generous: by the later cases the Files tab holds a dozen copies of the fixture, each
        // with its own compact player decoding 7 MB before the modal's player gets its turn.
        await Expect(Modal.GetByRole(AriaRole.Button, new() { Name = "Clear Regions" }))
            .ToBeEnabledAsync(new() { Timeout = 90_000 });
        await Page.WaitForTimeoutAsync(500);
    }

    /// <summary>Signs in, uploads, opens full view. False when the environment is not there.</summary>
    private async Task<bool> ReadyInFullViewAsync()
    {
        if (!await ReachCaseFilesAsync()) { Record("env", "SKIP", "TGH org / Belmont case not reachable"); return false; }
        if (!await UploadFixtureAsync())  { Record("env", "FAIL", "fixture upload never produced a waveform"); return false; }
        await OpenFullViewAsync();
        return true;
    }

    /// <summary>Drags across the modal waveform from one fraction of its width to another.</summary>
    private async Task DrawRegionAsync(double fromFraction, double toFraction)
    {
        var box = await ModalWaveform.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, "modal waveform has no box");
        var y = box!.Y + box.Height / 2;
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * fromFraction), y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * ((fromFraction + toFraction) / 2)), y, new() { Steps = 8 });
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * toFraction), y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);
    }

    private ILocator ToolbarButton(string text) =>
        Modal.GetByRole(AriaRole.Button, new() { Name = text, Exact = false }).First;

    // ── The walk ──────────────────────────────────────────────────────────────

    [Test, Order(1)]
    [Description("Compact preview renders; full view opens, closes, and reopens.")]
    public async Task Walk01_FullView_open_close_reopen()
    {
        if (!await ReachCaseFilesAsync()) { Record("env", "SKIP", "not reachable"); return; }
        if (!await UploadFixtureAsync())  { Record("env", "FAIL", "no waveform after upload"); return; }
        Record("compact", "PASS", "compact waveform rendered after upload");
        await SnapAsync("01-compact");

        await OpenFullViewAsync();
        Record("A", "PASS", "full view opened from the context menu");
        await SnapAsync("01-fullview-open");

        // Close with the X.
        await Modal.Locator(".btn-close").First.ClickAsync();
        await Page.WaitForTimeoutAsync(500);
        var closedCount = await Page.Locator(".modal.show").CountAsync();
        Record("A", closedCount == 0 ? "PASS" : "FAIL", $"after X: {closedCount} modal(s) still shown");

        // Now make the parent re-render: clicking the compact player toggles play, which raises
        // OnPlay/OnTimeUpdate and StateHasChanged in AudioFilePreview.
        await Page.Locator("[id^='afp-']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(1500);
        var reappeared = await Page.Locator(".modal.show").CountAsync();
        await SnapAsync("01-after-parent-rerender");
        Record("A", reappeared == 0 ? "PASS" : "FAIL",
            reappeared == 0 ? "modal stayed closed after a parent re-render"
                            : "modal came back on its own after the compact player re-rendered the parent");

        if (reappeared > 0)
        {
            // The resurrected modal now covers the page, so nothing beneath it can be clicked. Is
            // Escape any better than the X was?
            await Page.Keyboard.PressAsync("Escape");
            await Page.WaitForTimeoutAsync(800);
            var afterEscape = await Page.Locator(".modal.show").CountAsync();
            Record("A", "NOTE", $"after Escape on the resurrected modal: {afterEscape} modal(s) shown");
            return;
        }

        // Stop whatever is playing, then reopen.
        await Page.Locator("[id^='afp-']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        await OpenFullViewAsync();
        Record("A", "PASS", "reopened from the context menu after closing");
    }

    [Test, Order(2)]
    [Description("Regions: one at a time; silence detection; whether a user region survives it.")]
    public async Task Walk02_Regions_and_silence_detection()
    {
        if (!await ReadyInFullViewAsync()) return;

        await DrawRegionAsync(0.10, 0.20);
        var afterFirst = await Regions.CountAsync();
        await DrawRegionAsync(0.40, 0.50);
        var afterSecond = await Regions.CountAsync();
        await SnapAsync("02-two-regions-drawn");
        Record("regions", afterSecond == 1 ? "PASS" : (afterSecond == 0 ? "FAIL" : "NOTE"),
            $"regions after 1st draw: {afterFirst}, after 2nd: {afterSecond} (design: one user region at a time)");

        // Edit panel shows the drawn region's range and enables Cut/Silence.
        await ToolbarButton("Edit").ClickAsync();
        await Page.WaitForTimeoutAsync(400);
        var cutEnabled = await Modal.GetByRole(AriaRole.Button, new() { Name = "Cut" }).First.IsEnabledAsync();
        Record("edit-target", cutEnabled ? "PASS" : "FAIL", $"Cut enabled with a drawn region: {cutEnabled}");
        var rangeText = await Modal.Locator("text=/\\d+:\\d\\d(\\.\\d)?–\\d+:\\d\\d/").First.TextContentAsync();
        Record("edit-target", "NOTE", $"edit panel region readout: {rangeText?.Trim()}");

        // Silence detection.
        await ToolbarButton("Silence").ClickAsync();
        await Page.WaitForTimeoutAsync(2500);
        var withSilence = await Regions.CountAsync();
        await SnapAsync("02-silence-on");
        var rangeAfter = await Modal.Locator("text=/\\d+:\\d\\d(\\.\\d)?–\\d+:\\d\\d/").First.TextContentAsync();
        Record("B", "NOTE", $"regions with silence detection on: {withSilence}; edit readout now: {rangeAfter?.Trim()}");
        Record("B", rangeAfter?.Trim() == rangeText?.Trim() ? "PASS" : "FAIL",
            rangeAfter?.Trim() == rangeText?.Trim()
                ? "the edit target stayed on the region the person drew"
                : "the edit target moved to a machine-detected region");

        // Draw a user region while silence shading is on.
        await DrawRegionAsync(0.60, 0.70);
        var afterUserRegion = await Regions.CountAsync();
        await SnapAsync("02-silence-then-user-region");
        Record("B", afterUserRegion >= withSilence ? "PASS" : "FAIL",
            $"regions after drawing a user region with silence on: {afterUserRegion} (was {withSilence})");
    }

    [Test, Order(3)]
    [Description("Spectrogram: colormap then resolution; does the colormap survive?")]
    public async Task Walk03_Spectrogram_colormap_survives_resolution_change()
    {
        if (!await ReadyInFullViewAsync()) return;

        await ToolbarButton("Show Spectrogram").ClickAsync();
        await Page.WaitForTimeoutAsync(6000);
        await SnapAsync("03-spectrogram-jet");

        // The colormap picker is the second select in the toolbar once the spectrogram is on.
        var selects = Modal.Locator("select");
        var selectCount = await selects.CountAsync();
        Record("C", "NOTE", $"toolbar selects visible with spectrogram on: {selectCount}");

        async Task<string> SampleAsync() =>
            await Page.EvaluateAsync<string>(
                @"() => { const cs = [...document.querySelectorAll('.modal.show canvas')];
                          const c = cs.sort((a,b) => b.width*b.height - a.width*a.height)[0];
                          if (!c) return 'no-canvas';
                          const ctx = c.getContext('2d'); if (!ctx) return 'no-ctx';
                          let r=0,g=0,b=0,n=0;
                          for (let y=0; y<c.height; y+=Math.max(1,(c.height/16)|0))
                            for (let x=0; x<c.width; x+=Math.max(1,(c.width/32)|0)) {
                              const d = ctx.getImageData(x,y,1,1).data; r+=d[0]; g+=d[1]; b+=d[2]; n++; }
                          return `${(r/n)|0},${(g/n)|0},${(b/n)|0}`; }");

        var jet = await SampleAsync();

        // Find the colormap select by its options rather than by position or exact label.
        var colormapIndex = -1;
        for (var i = 0; i < selectCount; i++)
        {
            var labels = await selects.Nth(i).Locator("option").AllTextContentsAsync();
            if (labels.Any(l => l.Contains("irid", StringComparison.OrdinalIgnoreCase))) { colormapIndex = i; break; }
        }
        var resolutionIndex = -1;
        for (var i = 0; i < selectCount; i++)
        {
            var labels = await selects.Nth(i).Locator("option").AllTextContentsAsync();
            if (labels.Any(l => l.Contains("1024") || l.Contains("2048"))) { resolutionIndex = i; break; }
        }
        Record("C", "NOTE", $"colormap select index {colormapIndex}, resolution select index {resolutionIndex}");

        if (colormapIndex >= 0 && resolutionIndex >= 0)
        {
            var viridisValue = await selects.Nth(colormapIndex).Locator("option")
                .Filter(new() { HasTextString = "irid" }).First.GetAttributeAsync("value");
            await selects.Nth(colormapIndex).SelectOptionAsync(viridisValue!);
            await Page.WaitForTimeoutAsync(6000);
            var viridis = await SampleAsync();
            await SnapAsync("03-spectrogram-viridis");
            Record("C", viridis != jet ? "PASS" : "FAIL", $"colormap change repainted: jet={jet} viridis={viridis}");

            var options = await selects.Nth(resolutionIndex).Locator("option").AllAsync();
            var current = await selects.Nth(resolutionIndex).InputValueAsync();
            var other = (await Task.WhenAll(options.Select(o => o.GetAttributeAsync("value")))).First(v => v != current);
            await selects.Nth(resolutionIndex).SelectOptionAsync(other!);
            await Page.WaitForTimeoutAsync(8000);
            var afterResolution = await SampleAsync();
            await SnapAsync("03-spectrogram-after-resolution");
            var reverted = afterResolution == jet || Math.Abs(Delta(afterResolution, jet)) < Math.Abs(Delta(afterResolution, viridis));
            Record("C", reverted ? "FAIL" : "PASS",
                reverted ? $"resolution change reverted the colormap toward jet: {afterResolution}"
                         : $"colormap survived the resolution change: {afterResolution}");
        }
        else
        {
            Record("C", "NOTE", "could not find the colormap select; captured screenshots only");
        }

        static int Delta(string a, string b)
        {
            var pa = a.Split(',').Select(int.Parse).ToArray();
            var pb = b.Split(',').Select(int.Parse).ToArray();
            return pa.Length == 3 && pb.Length == 3 ? Math.Abs(pa[0] - pb[0]) + Math.Abs(pa[1] - pb[1]) + Math.Abs(pa[2] - pb[2]) : 999;
        }
    }

    [Test, Order(4)]
    [Description("EQ panel: does ticking High-pass do anything without touching its slider?")]
    public async Task Walk04_Eq_checkbox_without_slider()
    {
        if (!await ReadyInFullViewAsync()) return;

        await ToolbarButton("EQ / Filters").ClickAsync();
        await Page.WaitForTimeoutAsync(400);
        await SnapAsync("04-eq-panel");

        // There is no outside-observable audio graph, so the walk records what the markup does:
        // whether the checkbox has a change handler wired beyond @bind.
        var checkboxes = Modal.Locator("input[type='checkbox']");
        var n = await checkboxes.CountAsync();
        Record("D", "NOTE", $"EQ panel checkboxes: {n}; each toggled and screenshot taken");
        for (var i = 0; i < n; i++) await checkboxes.Nth(i).CheckAsync();
        await Page.WaitForTimeoutAsync(500);
        await SnapAsync("04-eq-all-checked");
        Record("D", "CODE", "no handler beyond @bind on the four enable checkboxes (AudioFilePreview.razor:186,191,200,212); effect not observable from outside");
    }

    [Test, Order(5)]
    [Description("Each of the eight destructive edits, and what Saved Clips shows for them.")]
    public async Task Walk05_Destructive_edits()
    {
        if (!await ReadyInFullViewAsync()) return;

        await DrawRegionAsync(0.20, 0.30);
        await ToolbarButton("Edit").ClickAsync();
        await Page.WaitForTimeoutAsync(400);

        var ops = new[] { "Cut", "Silence", "Normalize", "Reverse", "Apply Gain", "Apply Fade", "Apply Speed", "Apply Pitch" };
        var made = 0;
        foreach (var op in ops)
        {
            var button = Modal.GetByRole(AriaRole.Button, new() { Name = op, Exact = false }).First;
            if (!await button.IsEnabledAsync())
            {
                Record("E", "FAIL", $"{op} button disabled with a region drawn");
                continue;
            }
            var before = await Modal.Locator("text=/Saved Clips \\((\\d+)\\)/").CountAsync() == 0
                ? 0 : ParseCount(await Modal.Locator("text=/Saved Clips \\((\\d+)\\)/").First.TextContentAsync());
            await button.ClickAsync();
            try
            {
                await Expect(Modal.Locator("text=/Saved Clips \\((\\d+)\\)/").First)
                    .ToHaveTextAsync(new Regex($@"Saved Clips \({before + 1}\)"), new() { Timeout = 60_000 });
                made++;
                Record("edit:" + op, "PASS", $"produced saved clip #{before + 1}");
            }
            catch
            {
                var err = await Modal.Locator(".alert-danger").First.TextContentAsync().ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
                Record("edit:" + op, "FAIL", $"no new saved clip within 60 s; error shown: {err?.Trim() ?? "(none)"}");
            }
            await Page.WaitForTimeoutAsync(800);
        }
        await SnapAsync("05-saved-clips");

        // F: what range badge does each saved clip carry?
        var badges = await Modal.Locator(".badge.bg-success").AllTextContentsAsync();
        // "0:00–0:00" or "0:00.0–0:00.0": a range with no length is the badge for a file whose
        // range was never recorded.
        var zeroed = badges.Count(b => Regex.IsMatch(b.Trim(), @"^0:00(\.0)?–0:00(\.0)?$"));
        Record("F", zeroed == 0 ? "PASS" : "FAIL",
            $"{made} edits made; {zeroed} of {badges.Count} saved-clip badges read 0:00–0:00: [{string.Join("; ", badges.Select(b => b.Trim()))}]");

        static int ParseCount(string? text)
        {
            var m = Regex.Match(text ?? "", @"\((\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }
    }

    [Test, Order(6)]
    [Description("Region explorer: does the second region play its own audio?")]
    public async Task Walk06_Region_explorer_second_region()
    {
        if (!await ReadyInFullViewAsync()) return;

        async Task<string?> ExploreAsync(double from, double to, string snap)
        {
            await DrawRegionAsync(from, to);
            var region = Regions.First;
            await region.ClickAsync(new() { Button = MouseButton.Right });
            await Page.GetByText("Explore Region", new() { Exact = false }).ClickAsync();
            var explorer = Page.Locator(".modal.show").Last;
            await Expect(explorer).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Page.WaitForTimeoutAsync(4000);
            await SnapAsync(snap);
            // The explorer's player control bar shows "current / total"; the total is what the
            // loaded bytes are, regardless of what the info bar claims.
            var clock = await explorer.Locator("text=/\\d+:\\d\\d(\\.\\d)? \\/ \\d+:\\d\\d/").First
                .TextContentAsync().ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
            await explorer.Locator(".btn-close").First.ClickAsync();
            await Page.WaitForTimeoutAsync(800);

            // I: does the explorer actually go away, or does the parent bring it back?
            var explorersShown = await Page.Locator(".modal.show[aria-label^='Region Explorer']").CountAsync();
            Record("I", explorersShown == 0 ? "PASS" : "FAIL",
                explorersShown == 0 ? "explorer closed with its X" : "explorer came back after its X (parent never told it closed)");
            if (explorersShown > 0)
            {
                await Page.Keyboard.PressAsync("Escape");
                await Page.WaitForTimeoutAsync(800);
                if (await Page.Locator(".modal.show[aria-label^='Region Explorer']").CountAsync() > 0)
                    throw new InvalidOperationException("explorer cannot be dismissed; second region not attempted");
            }
            return clock?.Trim();
        }

        var first = await ExploreAsync(0.10, 0.15, "06-explorer-first");
        string? second;
        try { second = await ExploreAsync(0.50, 0.75, "06-explorer-second"); }
        catch (Exception ex) { Record("H", "NOTE", $"second region not reachable: {ex.Message}"); return; }
        Record("H", "NOTE", $"explorer clock for a short region: {first ?? "(none)"}; for a long region: {second ?? "(none)"}");
        if (first is not null && second is not null)
            Record("H", first == second ? "FAIL" : "PASS",
                first == second ? "the second explorer showed the first region's audio length"
                                : "each explorer loaded its own region's audio");

        // I: did closing the explorer leave the parent thinking it is still open?
        var stillOpen = await Page.Locator(".modal.show").CountAsync();
        Record("I", "NOTE", $"modals shown after closing the explorer: {stillOpen} (1 = only the full view, as expected)");
    }

    [Test, Order(7)]
    [Description("EVP panel: scan, keep, dismiss, and the marker list's play button.")]
    public async Task Walk07_Evp_scan_and_markers()
    {
        if (!await ReadyInFullViewAsync()) return;

        await ToolbarButton("EVP Markers").ClickAsync();
        await Page.WaitForTimeoutAsync(400);

        await Modal.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();
        try
        {
            await Expect(Modal.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" })).ToBeVisibleAsync(new() { Timeout = 90_000 });
            await Page.WaitForTimeoutAsync(1000);
        }
        catch { }
        await SnapAsync("07-after-scan");
        var scanMessage = await Modal.Locator("text=/candidate|found|nothing|no sounds/i").First
            .TextContentAsync().ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
        Record("scan", scanMessage is null ? "NOTE" : "PASS", $"scan message: {scanMessage?.Trim() ?? "(none seen)"}");

        // Keep the first candidate if there is one.
        var keep = Modal.GetByTitle("Keep as an EVP marker").First;
        if (await keep.CountAsync() > 0)
        {
            await keep.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
            var confirm = Page.Locator(".modal.show[aria-label='Keep this as an EVP']")
                .GetByRole(AriaRole.Button, new() { Name = "Keep it", Exact = true }).First;
            await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
            // The label is required; pressing Keep it without one is a validation refusal, not a
            // defect (the walk's first pass recorded it as one).
            await Page.Locator(".modal.show[aria-label='Keep this as an EVP'] input[type='text']").First
                .FillAsync("walk: kept candidate");
            await confirm.ClickAsync();
            await Page.WaitForTimeoutAsync(1500);
            var dialogGone = await Page.Locator(".modal.show[aria-label='Keep this as an EVP']").CountAsync() == 0;
            await SnapAsync("07-kept-candidate");
            Record("review", dialogGone ? "PASS" : "FAIL",
                dialogGone ? "kept a candidate through the confirm dialog" : "the Keep dialog stayed open after Keep it");
            if (!dialogGone) { await Page.Keyboard.PressAsync("Escape"); await Page.WaitForTimeoutAsync(600); }
        }
        else
        {
            // Add a marker by hand instead.
            await Modal.GetByRole(AriaRole.Button, new() { Name = "Add Marker at", Exact = false }).ClickAsync();
            await Page.WaitForTimeoutAsync(500);
            var dialog = Page.Locator(".modal.show").Last;
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).First.ClickAsync();
            await Page.WaitForTimeoutAsync(1500);
            Record("marker", "PASS", "added a marker by hand");
        }

        // J: the marker list's ▶.
        // Only the confirmed-marker rows (they carry a coloured range badge); candidate rows have
        // their own ▶ which goes through a different, working path.
        var playButtons = Modal.Locator("div:has(> span.badge[style*='background']) button:has(svg use[href$='#play'])");
        if (await playButtons.CountAsync() > 0)
        {
            await playButtons.First.ClickAsync();
            await Page.WaitForTimeoutAsync(1200);
            var playing = await Page.EvaluateAsync<bool>(
                "() => [...document.querySelectorAll('.modal.show audio, .modal.show video')].some(m => !m.paused)");
            var pauseVisible = await Modal.GetByRole(AriaRole.Button, new() { Name = "Pause", Exact = false }).CountAsync() > 0;
            await SnapAsync("07-marker-play");
            Record("J", (playing || pauseVisible) ? "PASS" : "FAIL",
                $"after marker ▶: media playing={playing}, Pause button visible={pauseVisible}");
        }
        else
        {
            Record("J", "NOTE", "no marker play button found in the list");
        }
    }

    [Test, Order(8)]
    [Description("Case mixer: nine clips, remove, export; then the Viewer persona.")]
    public async Task Walk08_Case_mixer()
    {
        if (!await ReachCaseFilesAsync()) { Record("env", "SKIP", "not reachable"); return; }
        if (!await UploadFixtureAsync())  { Record("env", "FAIL", "no waveform after upload"); return; }

        var mixerUrl = _caseUrl.TrimEnd('/') + "/audio-mix";
        await Page.GotoAsync(mixerUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1500);
        await SnapAsync("08-mixer-empty");

        var addButtons = Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true });
        var addCount = await addButtons.CountAsync();
        Record("K", addCount > 0 ? "PASS" : "FAIL", $"audio clips offered by the mixer: {addCount}");
        if (addCount == 0) return;

        for (var i = 0; i < 9; i++)
        {
            await addButtons.First.ClickAsync();
            await Page.WaitForTimeoutAsync(200);
        }
        var clips = await Page.Locator("[data-clip-id]").CountAsync();
        var refusal = await Page.Locator("text=/full|no free track|room/i").CountAsync();
        await SnapAsync("08-mixer-nine-clips");
        Record("K", "NOTE", $"after 9 adds: {clips} clip blocks on the grid; refusal message shown: {refusal > 0}");

        // Every block the same width?
        var widths = await Page.Locator("[data-clip-id]").EvaluateAllAsync<int[]>("els => els.map(e => e.getBoundingClientRect().width|0)");
        Record("K", widths.Distinct().Count() == 1 ? "FAIL" : "PASS",
            $"clip block widths: {string.Join(",", widths.Distinct())} (all equal means length is not real)");

        // Transport.
        var playDisabled = await Page.GetByRole(AriaRole.Button, new() { Name = "Play", Exact = false }).First.IsDisabledAsync();
        Record("K", playDisabled ? "FAIL" : "PASS", $"Play disabled: {playDisabled}");

        // Remove one.
        var before = clips;
        var x = Page.Locator("[data-clip-id] span", new() { HasTextString = "✕" }).First;
        try
        {
            await x.ClickAsync(new() { Timeout = 8_000 });
            await Page.WaitForTimeoutAsync(500);
            var afterRemove = await Page.Locator("[data-clip-id]").CountAsync();
            Record("K", afterRemove < before ? "PASS" : "FAIL", $"✕ removed a clip: {before} → {afterRemove}");
        }
        catch (TimeoutException)
        {
            Record("K", "FAIL", "the clip block swallows the click meant for its ✕ — remove is unreachable by mouse");
        }

        // Export.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export Mix" }).ClickAsync();
        await Page.WaitForTimeoutAsync(15_000);
        await SnapAsync("08-mixer-after-export");
        var exportError = await Page.Locator(".text-danger").First.TextContentAsync().ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
        Record("K", Page.Url.Contains("/audio-mix") ? (exportError is null ? "NOTE" : "FAIL") : "PASS",
            Page.Url.Contains("/audio-mix") ? $"still on the mixer after export; message: {exportError?.Trim() ?? "(none)"}"
                                            : "export finished and returned to the case");

        // Viewer persona.
        await LoginAsync(ViewerEmail, ViewerPassword);
        await Page.GotoAsync(_caseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1500);
        var mixerButton = await Page.GetByRole(AriaRole.Button, new() { Name = "Audio Mixer", Exact = false }).CountAsync();
        await SnapAsync("08-viewer-case");
        Record("K-perm", "NOTE", $"Viewer sees the Audio Mixer button on the case: {mixerButton > 0}");
        if (mixerButton > 0)
        {
            await Page.GotoAsync(mixerUrl);
            await Page.WaitForTimeoutAsync(1500);
            var viewerAdd = await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).CountAsync();
            if (viewerAdd > 0)
            {
                await Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).First.ClickAsync();
                await Page.GetByRole(AriaRole.Button, new() { Name = "Export Mix" }).ClickAsync();
                await Page.WaitForTimeoutAsync(10_000);
                var msg = await Page.Locator(".text-danger").First.TextContentAsync().ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
                await SnapAsync("08-viewer-export");
                Record("K-perm", "NOTE", $"Viewer export message: {msg?.Trim() ?? "(none)"}");
            }
        }
    }
}
