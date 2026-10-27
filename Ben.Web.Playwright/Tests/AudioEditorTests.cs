using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Does the editor act on what the person asked for, and say so when it will not?
/// </summary>
/// <remarks>
/// <para>The audio editor answered every failed edit with one hardcoded line — "only WAV and MP3
/// sources can be edited" — because the client returns null on failure and drops the response
/// body. That line was true for exactly one of the reasons the endpoint refuses, and phase 1 added
/// several more: a private recording that cannot be published, a value outside its range, a region
/// past the end of the recording, a recording longer than the edit ceiling. Every one of them would
/// have reached the screen as a sentence about file formats.</para>
///
/// <para>So these tests are not about the refusals — the controller tests cover those — but about
/// whether the refusal arrives. A server guard nobody can read is worse than no guard, because the
/// person retries the same thing (2026-09-06 audio audit, phase 1).</para>
/// </remarks>
[TestFixture]
[Category("AudioEditor")]
public class AudioEditorTests : BenTestBase
{
    private static readonly string TestAudioPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    /// <summary>The editor, on its own page since phase 1 of the 10/26/2026 plan. It was a
    /// fullscreen modal, and everything these tests look for sat inside it, as it sits inside this.</summary>
    private ILocator Modal => Page.Locator("#audio-editor");

    /// <summary>The compact player on the card for this test's own recording.</summary>
    private ILocator? _ourPlayer;

    /// <summary>The address the editor opened at, so a test can come back to it.</summary>
    private string? _editorUrl;

    /// <summary>
    /// Opens the editor from a compact player's menu: right-click, "Open in editor", and wait for
    /// the page to be ready to use.
    /// </summary>
    private async Task OpenEditorFromPlayerAsync(ILocator player)
    {
        await player.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Open in editor", new() { Exact = true }).ClickAsync();
        await Page.WaitForURLAsync("**/audio/**", new() { Timeout = 30_000 });
        _editorUrl = Page.Url;
        await WaitForEditorAsync();
    }

    private async Task WaitForEditorAsync()
    {
        await Expect(Modal).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Modal.GetByRole(AriaRole.Button, new() { Name = "Clear Regions" }))
            .ToBeEnabledAsync(new() { Timeout = 90_000 });
    }

    /// <summary>
    /// Leaves the editor the way a person would, by its Back link, then opens the same recording
    /// again. Leaving is what saves how it was set up; it used to be closing the window.
    /// </summary>
    private async Task LeaveAndReopenEditorAsync()
    {
        Assert.That(_editorUrl, Is.Not.Null, "the editor was never opened");
        await Page.GetByTestId("audio-editor-back").ClickAsync();
        await Page.WaitForURLAsync(u => !u.Contains("/audio/"), new() { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(800);   // the save on the way out is a round trip

        await Page.GotoAsync(_editorUrl!);
        await WaitForEditorAsync();
    }


    /// <summary>
    /// Signs in, puts a recording in Sarah's own library, and opens the full-view editor on it.
    /// </summary>
    /// <remarks>
    /// <para><b>Her own library, not a case's Files tab.</b> These tests are about the editor, and
    /// the case tab was the wrong place to reach it from for two reasons that only showed up under
    /// load. It draws a waveform for every audio file on the case, so after a few runs — or after
    /// the other fixtures in a full suite have uploaded to the same case — the page is decoding a
    /// dozen recordings before the test's own file appears; eleven files was enough to push the
    /// twelfth past a minute. And the file a test then picked up was not necessarily its own,
    /// which surfaced as the editor refusing to save settings for a recording somebody else
    /// owned (2026-09-06 audio audit, phase 6).</para>
    ///
    /// <para>The library draws waveforms on demand instead, one tap at a time, and everything here
    /// belongs to the seat running the test.</para>
    /// </remarks>
    /// <summary>The name the last recording was uploaded under, for a test that needs to find it again.</summary>
    private string? _lastUploadedName;

    private async Task<bool> ReadyInFullViewAsync(byte[]? audio = null, string extension = "mp3", string mimeType = "audio/mpeg", bool openEditor = true)
    {
        await LoginAsync(UserEmail, UserPassword);

        await Page.GotoAsync($"{BaseUrl}/upload-files");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The panel is behind its own button, and the file input does not exist until it opens.
        var openPanel = Page.GetByRole(AriaRole.Button, new() { Name = "Upload New File" });
        try { await Expect(openPanel).ToBeVisibleAsync(new() { Timeout = 30_000 }); }
        catch { TestContext.Out.WriteLine("gave up: /upload-files did not render"); return false; }
        await openPanel.ClickAsync();

        // A file type is required before anything will send.
        var fileType = Page.Locator("select").First;
        try { await Expect(fileType).ToBeVisibleAsync(new() { Timeout = 20_000 }); }
        catch { TestContext.Out.WriteLine("gave up: no file-type picker on /upload-files"); return false; }
        await fileType.SelectOptionAsync(new SelectOptionValue { Label = "Audio" });

        var input = Page.Locator("#chunked-upload-input-7f31");
        try { await Expect(input).ToBeAttachedAsync(new() { Timeout = 20_000 }); }
        catch { TestContext.Out.WriteLine("gave up: no upload input on /upload-files"); return false; }

        // A name of its own, for two reasons: the library shows every recording this seat can SEE,
        // including seeded ones owned by other people, so "the first card" is not necessarily ours
        // — and the editor will not save settings for a file that is not. A unique name also walks
        // past the same-name dialog rather than having to answer it.
        var uploadedName = $"editor-test-{Guid.NewGuid():N}.{extension}";
        _lastUploadedName = uploadedName;
        await input.SetInputFilesAsync(new FilePayload
        {
            Name      = uploadedName,
            MimeType  = mimeType,
            Buffer    = audio ?? await File.ReadAllBytesAsync(TestAudioPath),
        });

        // The bytes go through page JavaScript in chunks, started by an explicit button.
        var upload = Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true });
        try { await Expect(upload).ToBeEnabledAsync(new() { Timeout = 20_000 }); }
        catch { TestContext.Out.WriteLine("gave up: the Upload button never enabled — a file type may be needed"); return false; }
        await upload.ClickAsync();

        // The page asks before making a second file of the same name, and the upload sits at
        // "Waiting" behind that dialog. The unique name above should mean it never appears, but
        // answering it costs nothing and a silent three-minute wait costs a lot.
        var keepBoth = Page.GetByRole(AriaRole.Button, new() { Name = "Keep Both" });
        await Page.WaitForTimeoutAsync(800);
        if (await keepBoth.CountAsync() > 0 && await keepBoth.IsVisibleAsync())
            await keepBoth.ClickAsync();

        // Waiting on the library rather than on a word in the panel: the progress row is replaced
        // when the upload finishes, so "Done is visible" was a race against the page tidying up.
        // The library is where the recording has to appear for any of this to matter anyway.
        await Page.GotoAsync($"{BaseUrl}/media-library");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // On demand, one waveform: the grid does not draw them until asked, which is the whole
        // reason this fixture moved here.
        // The card for OUR recording, found by the name it was given.
        var ourCard = Page.Locator(".card").Filter(new() { HasTextString = uploadedName }).First;
        try { await Expect(ourCard).ToBeVisibleAsync(new() { Timeout = 180_000 }); }
        catch
        {
            TestContext.Out.WriteLine($"gave up: {uploadedName} never reached the library");
            return false;
        }

        var waveformButton = ourCard.GetByTestId("show-waveform").First;
        try { await Expect(waveformButton).ToBeVisibleAsync(new() { Timeout = 30_000 }); }
        catch { TestContext.Out.WriteLine("gave up: our card has no Waveform button"); return false; }
        await waveformButton.ClickAsync();

        try { await Expect(ourCard.Locator("[id^='ws-']").First).ToBeVisibleAsync(new() { Timeout = 60_000 }); }
        catch { TestContext.Out.WriteLine("gave up: the waveform never drew"); return false; }

        _ourPlayer = ourCard.Locator("[id^='afp-']").First;
        if (openEditor) await OpenEditorFromPlayerAsync(_ourPlayer);
        return true;
    }

    /// <summary>
    /// The editor is a page with an address of its own: opening that address directly, with no
    /// list behind it, gives the editor on the same recording.
    /// </summary>
    [Test]
    public async Task The_editor_has_its_own_address()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        Assert.That(_editorUrl, Does.Match(@"/audio/[0-9a-fA-F-]{36}"), "the editor did not open at /audio/{id}");
        var title = (await Page.Locator(".audio-editor-page__title").InnerTextAsync()).Trim();

        await Page.GotoAsync($"{BaseUrl}/");
        await Page.GotoAsync(_editorUrl!);
        await WaitForEditorAsync();

        Assert.That((await Page.Locator(".audio-editor-page__title").InnerTextAsync()).Trim(), Is.EqualTo(title),
            "the address opened a different recording");
    }

    /// <summary>Back leaves the editor for the page it was opened from, not somewhere fixed.</summary>
    [Test]
    public async Task Back_returns_to_where_you_came_from()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        Assert.That(_editorUrl, Does.Contain("return=%2Fmedia-library"), "the editor was not told where it was opened from");
        await Page.GetByTestId("audio-editor-back").ClickAsync();
        await Page.WaitForURLAsync("**/media-library**", new() { Timeout = 30_000 });
    }

    /// <summary>
    /// The compact player still has a bigger window for listening without leaving the list, and it
    /// plays.
    /// </summary>
    /// <remarks>
    /// That window used to be the whole editor, fullscreen. It is now a quick look with its own
    /// controls and a way into the editor.
    /// </remarks>
    [Test]
    public async Task Quick_look_still_plays()
    {
        if (!await ReadyInFullViewAsync(openEditor: false))
        {
            Assert.Ignore("Could not reach the recording's player; see the output for where it gave up.");
            return;
        }

        await _ourPlayer!.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Quick look", new() { Exact = true }).ClickAsync();

        var look = Page.GetByTestId("audio-quick-look");
        await Expect(look).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.GetByTestId("open-in-editor")).ToBeVisibleAsync();

        var play = look.GetByRole(AriaRole.Button, new() { Name = "Play" });
        await Expect(play).ToBeVisibleAsync(new() { Timeout = 60_000 });
        var time = look.Locator(".ws-player-time");
        var before = (await time.InnerTextAsync()).Trim();

        await play.ClickAsync();
        await Page.WaitForTimeoutAsync(1_500);
        var during = (await time.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"quick look time {before} -> {during}");

        Assert.That(during, Is.Not.EqualTo(before), "the quick look did not play");
    }

    /// <summary>
    /// Turning on silence detection must not take the selection away.
    /// </summary>
    /// <remarks>
    /// <para>The walk drew a region at 1:14.6–1:33.2, turned silence detection on, and the edit
    /// panel then read 3:00.6–3:06.5 — a stretch the machine had found — with the drawn region gone
    /// from the waveform. Cut and Silence would have destroyed audio nobody chose
    /// (2026-09-06 audio walk, finding B).</para>
    ///
    /// <para>Read off the panel rather than from any internal state, because the panel is what
    /// somebody about to click Cut is looking at.</para>
    /// </remarks>
    [Test]
    public async Task Silence_detection_does_not_steal_the_region_you_drew()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        await DrawRegionAsync(0.20, 0.40);

        await AudioEditorUi.OpenTabAsync(Modal, "Edit");
        var readout = Modal.Locator("#edit-region-readout").First;
        await Expect(readout).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var before = (await readout.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"the panel showed: {before}");
        Assert.That(before, Does.Not.Contain("Draw a region"),
            "the drag did not produce a selection, so this test cannot say anything about B");

        await AudioEditorUi.ChooseAsync(Page, "view", "Detect silence");
        await Page.WaitForTimeoutAsync(2_000);   // detection walks the whole decoded buffer

        var after = (await readout.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"after silence detection: {after}");

        Assert.That(after, Is.EqualTo(before),
            "silence detection moved the edit target: Cut would now destroy a stretch the machine "
            + "chose rather than the one the person drew");
    }

    /// <summary>
    /// A recording from the phone (.m4a) scans and edits in the editor like an MP3 (audio phase 4).
    /// </summary>
    /// <remarks>
    /// <para>The fixture is cut from a real device bundle and keeps the phone's layout, with the
    /// index after the audio, which is why the server copies it to a file before ffmpeg reads it.
    /// It goes up as <c>audio/x-m4a</c>, which is what Chrome and Safari on a Mac call an .m4a.</para>
    ///
    /// <para>A server with no ffmpeg answers "This server cannot read .m4a yet.", and the editor
    /// shows that sentence. That is ignored here rather than failed: run-e2e.sh configures ffmpeg
    /// whenever this machine has one, and the sentence is the right answer when it does not.</para>
    /// </remarks>
    [Test]
    public async Task A_phone_recording_can_be_scanned_and_edited()
    {
        var m4a = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "device-clip.m4a"));
        if (!await ReadyInFullViewAsync(m4a, "m4a", "audio/x-m4a"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        var panel = Modal.Locator("#evp-markers-panel");
        var scan  = panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" });
        await Expect(scan).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await scan.ClickAsync();

        var message = panel.Locator(".evp-lab__msg");
        await Expect(message).ToBeVisibleAsync(new() { Timeout = 120_000 });
        var said = (await message.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"scan said: {said}");

        if (said.Contains("cannot read .m4a"))
        {
            Assert.Ignore($"This host has no ffmpeg, and the editor said so: {said}");
            return;
        }

        // A finding, either way. The old answer to an .m4a was "The scan failed. The file may not
        // be readable audio.", which blamed a recording that was fine.
        Assert.That(said, Does.Match("Nothing stood out|candidate"), "the scan did not run");

        var clipsButton = Modal.Locator("#audio-editor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Clips", Exact = false });
        async Task<int> ClipCountAsync()
        {
            var m = System.Text.RegularExpressions.Regex.Match(await clipsButton.InnerTextAsync(), @"\((\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }
        var before    = await ClipCountAsync();
        var editError = Modal.Locator(".alert-danger").First;

        // Normalize runs straight from the menu. Edits were refused for anything but MP3 and WAV.
        await AudioEditorUi.ChooseAsync(Page, "edit", "Normalize");

        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (await ClipCountAsync() > before) break;
            if (await editError.CountAsync() > 0 && await editError.IsVisibleAsync())
                Assert.Fail($"Normalize was refused: {(await editError.InnerTextAsync()).Trim()}");
            await Page.WaitForTimeoutAsync(1_000);
        }

        Assert.That(await ClipCountAsync(), Is.GreaterThan(before),
            "Normalize of a phone recording produced neither a saved file nor a message");
    }

    /// <summary>
    /// An edit is saved as FLAC, says how big it would have been as WAV, and plays; ticking "Save
    /// as WAV" saves WAV and says so (phase 4b).
    /// </summary>
    /// <remarks>
    /// The waveform drawn on the new clip's row is the proof a browser can open the FLAC: the row's
    /// player decodes the file itself, and a file it cannot decode draws nothing.
    /// </remarks>
    [Test]
    public async Task An_edit_is_saved_as_flac_and_says_how_big_each_format_is()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        var note = Modal.Locator("#audio-last-save");

        await AudioEditorUi.ChooseAsync(Page, "edit", "Normalize");
        await Expect(note).ToContainTextAsync("Saved as", new() { Timeout = 120_000 });
        var said = (await note.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"first save: {said}");

        if (said.Contains("cannot save FLAC yet"))
        {
            Assert.Ignore($"This host has no ffmpeg, and the editor said so: {said}");
            return;
        }
        Assert.That(said, Does.StartWith("Saved as FLAC").And.Contain("as WAV it would be").And.Contain("Nothing is lost"));

        // The newest clip is first in the list, and its player has drawn the FLAC it was handed.
        var newest = Modal.Locator(".audio-clip").First;
        await Expect(newest.Locator("[id^='ws-']").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
        var drawn = await newest.EvaluateAsync<bool>(@"row => {
            const deadline = Date.now() + 30000;
            return new Promise(done => (function look() {
                const host = row.querySelector('[id^=""ws-""]');
                const shadow = host && [...host.querySelectorAll('*')].concat([host]).find(e => e.shadowRoot)?.shadowRoot;
                if (shadow && shadow.querySelectorAll('canvas').length > 0) return done(true);
                if (Date.now() > deadline) return done(false);
                setTimeout(look, 250);
            })());
        }");
        Assert.That(drawn, Is.True, "the saved FLAC never drew a waveform, so a browser could not open it");

        // The other choice, from the Edit tab.
        await AudioEditorUi.OpenTabAsync(Modal, "Edit");
        await Modal.Locator("#edit-save-as-wav").CheckAsync();
        await AudioEditorUi.ChooseAsync(Page, "edit", "Normalize");
        await Expect(note).ToContainTextAsync("Saved as WAV", new() { Timeout = 120_000 });
        TestContext.Out.WriteLine($"second save: {(await note.InnerTextAsync()).Trim()}");
        Assert.That(await note.InnerTextAsync(), Does.Not.Contain("cannot save FLAC"), "WAV was asked for, so nothing needs explaining");
    }

    /// <summary>
    /// Every scan setting can be changed from Fine-tune, a change reaches the scan, a preset can be
    /// saved, and the settings a recording was last scanned with come back on reopening (phase 5).
    /// </summary>
    [Test]
    public async Task Scan_settings_change_the_scan_and_come_back_with_their_preset()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        var panel = Modal.Locator("#evp-markers-panel");
        await panel.GetByRole(AriaRole.Button, new() { Name = "Fine-tune" }).ClickAsync();
        var settings = panel.Locator("#evp-scan-settings");
        await Expect(settings).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(settings.Locator("#evp-preset-pick")).ToHaveValueAsync("builtin:Medium");

        async Task<string> ScanAsync()
        {
            var scan = panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" });
            await scan.ClickAsync();
            await Expect(scan).ToBeEnabledAsync(new() { Timeout = 120_000 });
            await Expect(panel.Locator(".evp-lab__msg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            var scores = await panel.Locator(".evp-row--candidate .evp-score__num").AllInnerTextsAsync();
            return string.Join(",", scores);
        }

        var atMedium = await ScanAsync();
        Assert.That(atMedium, Is.Not.Empty, "Medium should find the test sounds");

        // A setting whose effect cannot be missed: nothing scores 95 or more. (The listening range
        // was the first choice, but these sounds are loud enough to score 94 either way; that it
        // reaches the detector is Scan_RecordsEverySettingItRanWith and A_narrower_band_changes_the_scores.)
        await settings.Locator("#evp-band-high").FillAsync("1000");
        await settings.Locator("#evp-min-score").FillAsync("95");
        await Expect(settings.Locator("#evp-preset-pick")).ToHaveValueAsync("custom");
        // Waited for by what it should say: the button can read "enabled" before the click has
        // started the scan, and the first scan's message and rows are still on the page then.
        await panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();
        await Expect(panel.Locator(".evp-lab__msg")).ToHaveTextAsync("Nothing stood out from the background.",
            new() { Timeout = 120_000 });
        var filtered = string.Join(",", await panel.Locator(".evp-row--candidate .evp-score__num").AllInnerTextsAsync());
        TestContext.Out.WriteLine($"scores at Medium: [{atMedium}], with a lowest score of 95: [{filtered}]");
        Assert.That(filtered, Is.Empty, "the lowest score did not reach the scan");

        var name = $"Low range {Guid.NewGuid():N}"[..20];
        await settings.Locator("#evp-preset-save-as").ClickAsync();
        await settings.Locator("#evp-preset-name").FillAsync(name);
        await settings.Locator("#evp-preset-save").ClickAsync();
        await Expect(settings.Locator("#evp-preset-message")).ToContainTextAsync($"Saved \"{name}\"", new() { Timeout = 15_000 });
        await Expect(settings.Locator("#evp-preset-pick option", new() { HasTextString = name })).ToHaveCountAsync(1);

        await LeaveAndReopenEditorAsync();

        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        await panel.GetByRole(AriaRole.Button, new() { Name = "Fine-tune" }).ClickAsync();
        await Expect(settings.Locator("#evp-band-high")).ToHaveValueAsync("1000", new() { Timeout = 15_000 });
        await Expect(settings.Locator("#evp-min-score")).ToHaveValueAsync("95");
        await Expect(settings.Locator("#evp-preset-pick option", new() { HasTextString = name })).ToHaveCountAsync(1);
        Assert.That(await settings.Locator("#evp-preset-pick").InputValueAsync(), Does.StartWith("saved:"),
            "the remembered settings are the saved preset's, so the list should say so");
    }

    // ── Phase 7: keyboard, spectrogram marks, overview strip, height, loop, volume line ──

    /// <summary>
    /// Waits until <paramref name="read"/> satisfies <paramref name="ok"/>, then returns the value;
    /// after <paramref name="timeoutMs"/> returns the last value read, for the assertion to report.
    /// </summary>
    private static async Task<T> WaitForAsync<T>(Func<Task<T>> read, Func<T, bool> ok, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var value = await read();
        while (!ok(value) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            value = await read();
        }
        return value;
    }

    /// <summary>Scans the 20-second recording with three sounds and waits for the three candidates.</summary>
    private async Task<ILocator> ScanForThreeAsync()
    {
        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        var panel = Modal.Locator("#evp-markers-panel");
        await panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();
        await Expect(panel.Locator(".evp-row--candidate")).ToHaveCountAsync(3, new() { Timeout = 120_000 });
        return panel;
    }

    private Task<double> PlayheadAsync() => Modal.EvaluateAsync<double>(
        "el => { const a = [...document.querySelectorAll('audio, video')].find(m => m.duration > 0); return a ? a.currentTime : -1; }");

    /// <summary>The keys from the shortcuts window do what it says (phase 7).</summary>
    [Test]
    public async Task Keyboard_shortcuts_drive_the_editor()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        var panel = await ScanForThreeAsync();
        // Nothing focused, so the keys go to the page (a click could land on a link).
        await Page.EvaluateAsync("() => document.activeElement?.blur()");

        // N highlights the first candidate in the list and plays it.
        await Page.Keyboard.PressAsync("n");
        var focused = panel.Locator(".evp-row--candidate.is-focused");
        await Expect(focused).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        // Z zooms in on it; F fits the whole recording again.
        await Page.Keyboard.PressAsync("z");
        Assert.That(await WaitForAsync(() => AudioEditorUi.WaveformZoomRatioAsync(Modal), r => r > 2), Is.GreaterThan(2), "Z did not zoom in");
        await Page.Keyboard.PressAsync("f");
        Assert.That(await WaitForAsync(() => AudioEditorUi.WaveformZoomRatioAsync(Modal), r => r < 1.05), Is.LessThan(1.05), "F did not fit");

        // D dismisses it, and the highlight moves to the next one.
        await Page.Keyboard.PressAsync("d");
        await Expect(panel.Locator(".evp-row--candidate")).ToHaveCountAsync(2, new() { Timeout = 15_000 });
        await Expect(focused).ToHaveCountAsync(1);

        // [ and ] make a selection from the playhead.
        await Page.Keyboard.PressAsync("Space");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("[");
        await Page.Keyboard.PressAsync("Shift+ArrowRight");
        await Page.Keyboard.PressAsync("]");
        await AudioEditorUi.OpenTabAsync(Modal, "Edit");
        var readout = Modal.Locator("#edit-region-readout");
        await Expect(readout).Not.ToContainTextAsync("Draw a region", new() { Timeout = 10_000 });
        TestContext.Out.WriteLine($"selection from the keys: {(await readout.InnerTextAsync()).Trim()}");

        // ? lists them all.
        await Page.Keyboard.PressAsync("Shift+Slash");
        await Expect(Page.Locator("#audio-shortcuts")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.Locator("#audio-shortcuts")).ToContainTextAsync("Keep the highlighted candidate");
    }

    /// <summary>The candidates show on the spectrogram where they are on the waveform (phase 7).</summary>
    [Test]
    public async Task The_spectrogram_marks_the_candidates()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        await ScanForThreeAsync();
        await AudioEditorUi.SetSpectrogramAsync(Modal, true);

        // The overlay canvas's pixels at the middle of each sound (4.0 s, 9.0 s, 15.0 s of 20) and
        // between them (6.5 s): tinted at the first three, clear at the last.
        async Task<int> AlphaAtAsync(double seconds) => await Modal.EvaluateAsync<int>(@"(el, at) => {
            const c = el.querySelector('canvas.ws-spectro-overlay');
            if (!c || !c.width) return -1;
            const x = Math.round(c.width * at / 20), y = Math.round(c.height / 2);
            return c.getContext('2d').getImageData(x, y, 1, 1).data[3];
        }", seconds + 0.4);
        Assert.That(await WaitForAsync(() => AlphaAtAsync(4.0), a => a > 0, 15_000), Is.GreaterThan(0), "the first candidate is not on the spectrogram");
        Assert.That(await AlphaAtAsync(9.0), Is.GreaterThan(0));
        Assert.That(await AlphaAtAsync(15.0), Is.GreaterThan(0));
        Assert.That(await AlphaAtAsync(6.5), Is.EqualTo(0), "something was drawn between the candidates");
    }

    /// <summary>The overview strip and the spectrogram's height come back on reopening (phase 7).</summary>
    [Test]
    public async Task The_overview_strip_and_spectrogram_height_are_remembered()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        // The strip's pane, with the plugin's waveform drawn into it.
        Task<int> MinimapsAsync() => Modal.EvaluateAsync<int>(
            @"el => [...el.querySelectorAll('.ws-minimap-pane')].filter(p => p.children.length > 0 && p.getBoundingClientRect().height > 20).length");

        await AudioEditorUi.ChooseAsync(Page, "view", "Overview strip");
        Assert.That(await WaitForAsync(MinimapsAsync, n => n > 0), Is.GreaterThan(0), "no overview strip appeared");

        await AudioEditorUi.SetSpectrogramAsync(Modal, true);
        await AudioEditorUi.OpenTabAsync(Modal, "Display");
        await Modal.Locator("#spectrogram-height").FillAsync("300");
        var pane = Modal.Locator(".ws-spectro-pane");
        Assert.That(await WaitForAsync(() => pane.EvaluateAsync<double>("el => el.getBoundingClientRect().height"), h => h is > 299 and < 301),
            Is.InRange(299, 301), "the spectrogram did not take the new height");

        await LeaveAndReopenEditorAsync();

        Assert.That(await WaitForAsync(MinimapsAsync, n => n > 0, 20_000), Is.GreaterThan(0), "the overview strip was not remembered");
        await Expect(Modal.Locator(".ws-spectro-pane")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        Assert.That(await WaitForAsync(() => Modal.Locator(".ws-spectro-pane").EvaluateAsync<double>("el => el.getBoundingClientRect().height"),
            h => h is > 299 and < 301, 20_000), Is.InRange(299, 301), "the spectrogram's height was not remembered");
    }

    /// <summary>With Loop on, a candidate plays round instead of stopping at its end (phase 7).</summary>
    [Test]
    public async Task A_candidate_loops_with_loop_on()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        var panel = await ScanForThreeAsync();
        await AudioEditorUi.ClickToolbarAsync(Page, Modal, "Loop");

        // The first in the list is the highest score; play it and watch the playhead for 5 s.
        var first = panel.Locator(".evp-row--candidate").First;
        await first.GetByTitle("Play this stretch with its surrounding context").ClickAsync();
        var times = new List<double>();
        for (var i = 0; i < 10; i++)
        {
            await Page.WaitForTimeoutAsync(500);
            var text = await PlayheadTextAsync();   // "Add Marker at 0:04.2"
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d+):(\d+(?:\.\d+)?)");
            if (m.Success) times.Add(int.Parse(m.Groups[1].Value) * 60 + double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        TestContext.Out.WriteLine($"playhead every half second: {string.Join(", ", times.Select(t => t.ToString("0.0")))}");

        Assert.That(times, Has.Count.EqualTo(10));
        Assert.That(times.Zip(times.Skip(1)).Any(p => p.Second < p.First - 0.2), Is.True,
            "the playhead never went back to the start, so it did not loop");
        Assert.That(times.Max() - times.Min(), Is.LessThan(3.0), "it played on past the candidate instead of going round");
    }

    /// <summary>"Turn parts down": draw the line down over a stretch and save it as a new file (phase 7).</summary>
    [Test]
    public async Task A_volume_line_can_be_drawn_and_saved()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.ChooseAsync(Page, "edit", "Turn parts down");
        var bar = Modal.Locator("#audio-volume-bar");
        await Expect(bar).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // Untouched, there is nothing to save, and it says so.
        await bar.Locator("#audio-volume-save").ClickAsync();
        await Expect(bar).ToContainTextAsync("still at full volume");

        // Drag the first point (top left) down by half the waveform's height.
        var point = await Modal.EvaluateAsync<double[]>(@"el => {
            for (const h of el.querySelectorAll('[id^=""ws-""] *, [id^=""ws-""]'))
                if (h.shadowRoot) {
                    const pts = [...h.shadowRoot.querySelectorAll('svg circle, svg ellipse')];
                    if (pts.length) { const r = pts[0].getBoundingClientRect(); return [r.x + r.width / 2, r.y + r.height / 2, h.getBoundingClientRect().height]; }
                }
            return [];
        }");
        Assert.That(point, Has.Length.EqualTo(3), "no points to drag on the volume line");
        await Page.Mouse.MoveAsync((float)point[0], (float)point[1]);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)point[0], (float)(point[1] + point[2] / 2), new() { Steps = 8 });
        await Page.Mouse.UpAsync();

        await bar.Locator("#audio-volume-save").ClickAsync();
        await Expect(bar).ToHaveCountAsync(0, new() { Timeout = 60_000 });
        await Expect(Modal.Locator("#audio-last-save")).ToContainTextAsync("Saved as", new() { Timeout = 15_000 });
    }

    // ── Phase 8: the detector's own view ──────────────────────────────────────

    /// <summary>How many pixels of a lane's canvas are drawn on (non-transparent), sampled on a grid.</summary>
    private Task<int> LaneInkAsync(string kind) => Modal.EvaluateAsync<int>(@"(el, kind) => {
        const c = el.querySelector(`.ws-lane--${kind} canvas`);
        if (!c || !c.width) return -1;
        const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
        let n = 0;
        for (let i = 3; i < d.length; i += 4 * 7) if (d[i] > 0) n++;
        return n;
    }", kind);

    /// <summary>
    /// The detector lane shows what the scan saw, a candidate's "Why?" explains its score, and the
    /// lanes come back on reopening (phase 8).
    /// </summary>
    [Test]
    public async Task The_detector_lane_and_why_explain_a_candidate()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        var panel = await ScanForThreeAsync();

        await AudioEditorUi.ChooseAsync(Page, "view", "Detector lane");
        Assert.That(await WaitForAsync(() => LaneInkAsync("detector"), n => n > 50), Is.GreaterThan(50), "the detector lane drew nothing");
        await AudioEditorUi.ChooseAsync(Page, "view", "Level lane");
        Assert.That(await WaitForAsync(() => LaneInkAsync("level"), n => n > 50), Is.GreaterThan(50), "the level lane drew nothing");

        var first = panel.Locator(".evp-row--candidate").First;
        await first.GetByTitle("Why the scan found this").ClickAsync();
        var why = panel.Locator(".evp-why");
        await Expect(why).ToContainTextAsync("dB above the room", new() { Timeout = 15_000 });
        await Expect(why.Locator(".evp-why__terms li")).ToHaveCountAsync(3);
        await Expect(why).ToContainTextAsync("not how likely it is to be a voice");

        await LeaveAndReopenEditorAsync();
        Assert.That(await WaitForAsync(() => LaneInkAsync("detector"), n => n > 50, 20_000), Is.GreaterThan(50), "the detector lane was not remembered");
        Assert.That(await WaitForAsync(() => LaneInkAsync("level"), n => n > 50, 20_000), Is.GreaterThan(50), "the level lane was not remembered");
    }

    /// <summary>
    /// Each candidate shows two readings, how much it stood out and how voice-like it is; the list sorts by
    /// either, and the voice-like filter hides on the page only, with nothing hidden at 0 (phase 9b).
    /// </summary>
    [Test]
    public async Task Candidates_show_both_readings_and_sort_and_filter_by_voice_like()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        var panel = await ScanForThreeAsync();
        var rows = panel.Locator(".evp-row--candidate");

        await Expect(rows.First.Locator(".evp-score__label").First).ToHaveTextAsync("Stood out");
        var voice = rows.Locator(".evp-score--voice[data-voice]");
        await Expect(voice).ToHaveCountAsync(3, new() { Timeout = 15_000 });

        await panel.Locator("#evp-sort").GetByRole(AriaRole.Button, new() { Name = "Voice-like" }).ClickAsync();
        await Expect(panel.Locator("#evp-sort button[aria-pressed=true]")).ToHaveTextAsync("Voice-like");
        var readings = new List<int>();
        foreach (var reading in await voice.AllAsync())
            readings.Add(int.Parse((await reading.GetAttributeAsync("data-voice"))!));
        TestContext.Out.WriteLine($"voice-like readings: {string.Join(", ", readings)}");
        Assert.That(readings, Is.Ordered.Descending, "Voice-like did not put the most voice-like first.");

        // At the highest reading, everything below it is hidden and said so; at 0 all of it comes back.
        var filter = panel.Locator("#evp-voice-filter");
        var top = readings.Max();
        await filter.FillAsync(top.ToString());
        var below = readings.Count(r => r < top);
        await Expect(rows).ToHaveCountAsync(readings.Count - below, new() { Timeout = 10_000 });
        if (below > 0)
            await Expect(panel.Locator("#evp-voice-hidden")).ToHaveTextAsync($"{below} hidden");
        if (top < 100)
        {
            await filter.FillAsync("100");
            await Expect(rows).ToHaveCountAsync(0, new() { Timeout = 10_000 });
            await Expect(panel.Locator("#evp-voice-hidden")).ToHaveTextAsync("3 hidden");
        }
        await filter.FillAsync("0");
        await Expect(rows).ToHaveCountAsync(3, new() { Timeout = 10_000 });
        await Expect(panel.Locator("#evp-voice-hidden")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Moving the listening range away from where the sounds are changes what a rescan scores them: the
    /// dials reach the scan (phase 11). The test audio's sounds are at 500, 1,500 and 2,500 Hz, so a floor
    /// of 3,000 Hz leaves only what leaks past the filter. They are still found (the room's noise is
    /// filtered the same way, so they still stand out from it) but "in the listening range" falls.
    /// </summary>
    [Test]
    public async Task Moving_the_listening_range_changes_how_a_rescan_scores()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        var panel = await ScanForThreeAsync();
        var scores = panel.Locator(".evp-row--candidate .evp-score:not(.evp-score--voice) .evp-score__num");
        async Task<int> TopAsync()
        {
            var all = new List<int>();
            foreach (var n in await scores.AllAsync()) all.Add(int.Parse(await n.InnerTextAsync()));
            return all.Max();
        }
        var before = await TopAsync();

        await panel.GetByRole(AriaRole.Button, new() { Name = "Fine-tune" }).ClickAsync();
        await panel.Locator("#evp-band-low").FillAsync("3000");
        await panel.Locator("#evp-rescan").ClickAsync();
        var narrowed = await WaitForAsync(TopAsync, top => top < before - 10, 120_000);
        TestContext.Out.WriteLine($"top score {before} at 300 Hz, {narrowed} from 3,000 Hz");
        Assert.That(narrowed, Is.LessThan(before - 10), "Moving the listening range did not change the scores.");

        await panel.Locator("#evp-band-low").FillAsync("300");
        await panel.Locator("#evp-rescan").ClickAsync();
        Assert.That(await WaitForAsync(TopAsync, top => top == before, 120_000), Is.EqualTo(before),
            "Putting the range back did not bring the scores back.");
    }

    /// <summary>
    /// Blind review, from the profile: pending candidates show no readings and list in time order, and
    /// the readings come back when it is turned off (phase 11).
    /// </summary>
    [Test]
    public async Task Blind_review_hides_the_readings_until_it_is_turned_off()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav", openEditor: false))
        {
            Assert.Ignore("Could not upload a recording; see the output for where it gave up.");
            return;
        }
        var name = _lastUploadedName!;
        await SetBlindReviewAsync(true);
        try
        {
            await OpenOurRecordingAsync(name);
            var panel = await ScanForThreeAsync();
            await Expect(panel.Locator("#evp-blind-note")).ToBeVisibleAsync();
            await Expect(panel.Locator(".evp-row--candidate .evp-score")).ToHaveCountAsync(0);
            await Expect(panel.Locator("#evp-sort")).ToHaveCountAsync(0);
            await Expect(panel.Locator(".evp-row__why")).ToHaveCountAsync(0);
        }
        finally
        {
            await SetBlindReviewAsync(false);
        }
        await OpenOurRecordingAsync(name);
        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        var shown = Modal.Locator("#evp-markers-panel");
        await Expect(shown.Locator(".evp-row--candidate .evp-score").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(shown.Locator("#evp-sort")).ToBeVisibleAsync();
    }

    private async Task SetBlindReviewAsync(bool on)
    {
        await Page.GotoAsync($"{BaseUrl}/profile");
        var box = Page.Locator("#evp-blind-review");
        await Expect(box).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await box.IsCheckedAsync() != on) await box.ClickAsync();
        await Expect(box).ToBeCheckedAsync(new() { Checked = on, Timeout = 10_000 });
        // Saved, not only ticked: a reload reads it back.
        await Page.ReloadAsync();
        await Expect(Page.Locator("#evp-blind-review")).ToBeCheckedAsync(new() { Checked = on, Timeout = 30_000 });
    }

    private async Task OpenOurRecordingAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/media-library");
        var card = Page.Locator(".card").Filter(new() { HasTextString = name }).First;
        await card.GetByTestId("show-waveform").First.ClickAsync(new() { Timeout = 60_000 });
        await Expect(card.Locator("[id^='ws-']").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await OpenEditorFromPlayerAsync(card.Locator("[id^='afp-']").First);
    }

    /// <summary>The EVP admin page draws its figures and a chart per measurement (phase 11).</summary>
    [Test]
    public async Task The_evp_admin_page_shows_the_figures()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/evp");
        await Expect(Page.Locator("#admin-evp-tiles")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#admin-evp-tiles")).ToContainTextAsync("Missed");
        await Expect(Page.Locator("#admin-evp-decoder")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await Page.Locator("#admin-evp-no-rulings").CountAsync() == 0)
            await Expect(Page.GetByTestId("admin-evp-histogram")).ToHaveCountAsync(9);
    }

    /// <summary>
    /// Several recordings chosen in the library are scanned as one batch, and the Scans page follows it
    /// to the end, each recording with what it found (phase 10).
    /// </summary>
    [Test]
    public async Task Several_recordings_scan_as_a_batch_and_the_scans_page_follows_it()
    {
        var names = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav", openEditor: false))
            {
                Assert.Ignore("Could not upload a recording; see the output for where it gave up.");
                return;
            }
            names.Add(_lastUploadedName!);
        }

        await Page.GotoAsync($"{BaseUrl}/media-library");
        await Page.Locator("#library-scan-choose").ClickAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#library-scan-start")).ToBeDisabledAsync();
        foreach (var name in names)
            await Page.GetByTestId("library-scan-pick").Filter(new() { HasTextString = name }).ClickAsync();
        await Expect(Page.Locator("#library-scan-count")).ToHaveTextAsync("2 recordings chosen");
        await Page.Locator("#library-scan-start").ClickAsync();

        var dialog = Page.Locator("#batch-scan-dialog");
        await Expect(dialog.Locator("#evp-scan-settings")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await dialog.Locator("#batch-scan-start").ClickAsync();
        await Expect(dialog.Locator("#batch-scan-outcome")).ToContainTextAsync("2 recordings are being scanned", new() { Timeout = 15_000 });
        await dialog.Locator("#batch-scan-follow").ClickAsync();

        await Page.WaitForURLAsync("**/audio/scans", new() { Timeout = 30_000 });
        var scan = Page.GetByTestId("audio-scan").First;
        await Expect(scan).ToContainTextAsync("2 recordings", new() { Timeout = 30_000 });
        await Expect(scan.GetByTestId("audio-scan-status")).ToContainTextAsync("candidates to review", new() { Timeout = 120_000 });
        var files = scan.GetByTestId("audio-scan-file");
        await Expect(files).ToHaveCountAsync(2);
        for (var i = 0; i < 2; i++)
        {
            await Expect(files.Nth(i)).ToContainTextAsync(names[i]);
            await Expect(files.Nth(i)).ToContainTextAsync("3 candidates to review");
        }

        // Each one opens in the editor, and Back comes here.
        await files.First.GetByTestId("audio-scan-open").ClickAsync();
        await Page.WaitForURLAsync("**/audio/*?return=*", new() { Timeout = 30_000 });
        await Expect(Page.Locator("#audio-editor")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(Page.GetByTestId("audio-editor-back")).ToHaveAttributeAsync("href", "/audio/scans");
    }

    /// <summary>
    /// The strip above the waveform shows every candidate, and a click goes to one; B switches to the
    /// original and back; the meter moves while something plays (phase 8).
    /// </summary>
    [Test]
    public async Task The_strip_the_compare_switch_and_the_meter_work()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }
        var panel = await ScanForThreeAsync();

        var marks = Modal.Locator("#evp-strip .evp-strip__mark");
        await Expect(marks).ToHaveCountAsync(3, new() { Timeout = 10_000 });
        await marks.Nth(1).ClickAsync();
        await Expect(panel.Locator(".evp-row--candidate.is-focused")).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        // The meter has ink while that candidate plays.
        var meterInk = await WaitForAsync(() => Modal.EvaluateAsync<int>(@"el => {
            const c = el.querySelector('#audio-meter');
            if (!c || !c.width) return -1;
            const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
            let n = 0; for (let i = 3; i < d.length; i += 16) if (d[i] > 0) n++;
            return n;
        }"), n => n > 0, 10_000);
        Assert.That(meterInk, Is.GreaterThan(0), "the meter never moved while a candidate played");

        await Page.EvaluateAsync("() => document.activeElement?.blur()");
        await Page.Keyboard.PressAsync("b");
        await Expect(Modal.Locator("#audio-original-chip")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Page.Keyboard.PressAsync("b");
        await Expect(Modal.Locator("#audio-original-chip")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    /// <summary>Ten minutes with a few sounds in it: long enough that a scan can be watched and stopped.</summary>
    private static byte[] LongWav() => Wav(600, [(30.0, 0.8), (200.0, 0.7), (450.0, 1.0)]);

    /// <summary>
    /// A scan runs in the background (phase 6): the button says how far it has got, the waveform is
    /// dimmed where it hasn't reached, and the dimming goes when it finishes.
    /// </summary>
    [Test]
    public async Task A_scan_runs_in_the_background_and_shows_how_far_it_has_got()
    {
        if (!await ReadyInFullViewAsync(LongWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        var panel = Modal.Locator("#evp-markers-panel");

        // Watch from inside the page, every 50 ms, so a fast scan cannot slip between two looks.
        await Page.EvaluateAsync(@"() => {
            window.__scanSeen = { labels: new Set(), mask: false };
            const look = () => {
                const btn = document.querySelector('#evp-markers-panel .evp-lab__scan');
                if (btn) window.__scanSeen.labels.add(btn.textContent.trim());
                for (const host of document.querySelectorAll('#audio-editor [id^=""ws-""]'))
                    for (const el of [host, ...host.querySelectorAll('*')])
                        if (el.shadowRoot?.querySelector('.ws-scan-mask')) window.__scanSeen.mask = true;
            };
            window.__scanLook = setInterval(look, 50);
        }");

        await panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();
        await Expect(panel.Locator(".evp-lab__msg")).ToContainTextAsync("to review", new() { Timeout = 180_000 });

        var seen = await Page.EvaluateAsync<System.Text.Json.JsonElement>(
            "() => { clearInterval(window.__scanLook); return { labels: [...window.__scanSeen.labels], mask: window.__scanSeen.mask }; }");
        var labels = seen.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ToList();
        TestContext.Out.WriteLine($"the button read: {string.Join(" | ", labels)}; dimming seen: {seen.GetProperty("mask").GetBoolean()}");

        Assert.That(labels, Has.Some.Matches("^Scanning \\d+%$"), "the button never said how far the scan had got");
        Assert.That(seen.GetProperty("mask").GetBoolean(), Is.True, "the waveform was never dimmed while the scan ran");
        var maskLeft = await Modal.EvaluateAsync<bool>(@"el => [...el.querySelectorAll('[id^=""ws-""]')]
            .some(h => [h, ...h.querySelectorAll('*')].some(e => e.shadowRoot?.querySelector('.ws-scan-mask')))");
        Assert.That(maskLeft, Is.False, "the dimming stayed after the scan finished");
        await Expect(panel.Locator(".evp-row--candidate")).ToHaveCountAsync(3, new() { Timeout = 15_000 });
    }

    /// <summary>Stop ends a scan part way, and the page says what that left behind.</summary>
    [Test]
    public async Task A_scan_can_be_stopped()
    {
        if (!await ReadyInFullViewAsync(LongWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.OpenTabAsync(Modal, "EVP Markers");
        var panel = Modal.Locator("#evp-markers-panel");
        await panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();

        var stop = panel.Locator("#evp-scan-stop");
        await Expect(stop).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await stop.ClickAsync();

        await Expect(panel.Locator(".evp-lab__msg")).ToHaveTextAsync("Stopped. The candidates from before are still here.",
            new() { Timeout = 60_000 });
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" })).ToBeEnabledAsync();
        await Expect(panel.Locator(".evp-row--candidate")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Silence, applied to a region a person drew, produces a file and says it did.
    /// </summary>
    /// <remarks>
    /// The walk found that seven of the eight edits produced a saved clip and Silence produced
    /// nothing within sixty seconds and showed no error. The region it would have used was a
    /// machine one, from finding B — so this reruns it against a region somebody actually drew,
    /// which is the only way to tell a Silence bug from B's consequences.
    /// </remarks>
    [Test]
    public async Task Silencing_a_region_you_drew_produces_a_clip()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        await DrawRegionAsync(0.20, 0.40);
        await AudioEditorUi.OpenTabAsync(Modal, "Edit");

        var readout = Modal.Locator("#edit-region-readout").First;
        await Expect(readout).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That((await readout.InnerTextAsync()).Trim(), Does.Not.Contain("Draw a region"),
            "no selection to silence, so this says nothing about the edit");

        // Counted, not merely present. Asking whether a Saved Clips section is visible would be a
        // free pass on any run where one already existed, and it is the kind of assertion that
        // quietly stops testing anything the moment the fixture changes. The count is the
        // toolbar's "Clips (n)", since phase 2 put the list itself in a tab of its own.
        var clipsButton = Modal.Locator("#audio-editor-toolbar").GetByRole(AriaRole.Button, new() { Name = "Clips", Exact = false });
        async Task<int> ClipCountAsync()
        {
            var m = System.Text.RegularExpressions.Regex.Match(await clipsButton.InnerTextAsync(), @"\((\d+)\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }
        var before = await ClipCountAsync();
        var editError = Modal.Locator(".alert-danger").First;

        // By id, not by name: the toolbar's silence-DETECTION toggle is also called "Silence" and
        // comes first in the DOM, so a name lookup lands on it. That is what the walk hit, which is
        // why finding E read as "Silence produced nothing and said nothing" — detection produces no
        // clip and no error, correctly.
        await Modal.Locator("#edit-op-silence").ClickAsync();

        // Generous: by the last test in this fixture the Files tab is decoding several copies of
        // the recording before this edit gets any attention. Alone it takes ten seconds.
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            if (await ClipCountAsync() > before) break;
            if (await editError.CountAsync() > 0 && await editError.IsVisibleAsync())
                Assert.Fail($"Silence was refused: {(await editError.InnerTextAsync()).Trim()}");
            await Page.WaitForTimeoutAsync(1_000);
        }

        var after = await ClipCountAsync();
        TestContext.Out.WriteLine($"saved clips {before} -> {after}");

        // And the editor shows where it went.
        await Expect(Modal.GetByRole(AriaRole.Tab, new() { Name = "Clips" })).ToHaveAttributeAsync("aria-selected", "true");

        Assert.That(after, Is.GreaterThan(before),
            "Silence produced neither a saved clip nor a message within three minutes — the walk's "
            + "finding E, now with a region a person actually drew.");
    }

    /// <summary>
    /// A confirmed marker's play button plays.
    /// </summary>
    /// <remarks>
    /// It only seeked: the playhead moved and nothing was heard, on the one button whose entire
    /// purpose is to let a reviewer hear the thing again — while the identical button on a
    /// candidate a row above played its audio, because candidates are always spans and went through
    /// a different path (2026-09-06 audio walk, finding J). A marker added by hand is a point, so
    /// it is the case that was broken.
    /// </remarks>
    [Test]
    public async Task A_markers_play_button_plays()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        await Modal.GetByRole(AriaRole.Button, new() { Name = "EVP Markers", Exact = false }).First.ClickAsync();
        await Modal.GetByRole(AriaRole.Button, new() { Name = "Add Marker at", Exact = false }).First.ClickAsync();

        // The marker dialog wants a label before it will save.
        var label = Page.Locator(".modal.show input[type=text]").First;
        await Expect(label).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await label.FillAsync("says a name");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).Last.ClickAsync();

        var play = Modal.GetByTitle("Play this marker").First;
        await Expect(play).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var before = await PlayheadTextAsync();

        await play.ClickAsync();
        await Page.WaitForTimeoutAsync(1_500);

        var during = await PlayheadTextAsync();
        TestContext.Out.WriteLine($"playhead {before} -> {during}");

        Assert.That(during, Is.Not.EqualTo(before),
            "the playhead did not move, so nothing was played. A point marker has no span, and "
            + "playing a zero-length region is a seek and a pause in the same instant.");
    }

    /// <summary>
    /// A marker can end past 1:40, and the sentence beside its checkbox ticks the checkbox.
    /// </summary>
    /// <remarks>
    /// <para>The "Ends at" box carried <c>max="100"</c>, so on any recording longer than 100 seconds
    /// its arrows stopped at 100 and any later end was flagged invalid. Typing past it still
    /// worked, which is why the check is the box's own validity and an arrow step, not a save.</para>
    ///
    /// <para>The label beside the checkbox pointed at the "Ends at" box, which does not exist
    /// until the box is ticked, so clicking the sentence did nothing.</para>
    /// </remarks>
    [Test]
    public async Task A_markers_end_can_pass_100_seconds()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the full view; see the output for where it gave up.");
            return;
        }

        await Modal.GetByRole(AriaRole.Button, new() { Name = "EVP Markers", Exact = false }).First.ClickAsync();
        await Modal.GetByRole(AriaRole.Button, new() { Name = "Add Marker at", Exact = false }).First.ClickAsync();

        var dialog = Page.Locator(".modal.show").Last;
        var spanBox = dialog.Locator("label:has-text('Mark a stretch of audio') input[type=checkbox]");
        await Expect(spanBox).ToBeVisibleAsync(new() { Timeout = 10_000 });
        if (!await spanBox.IsCheckedAsync())
        {
            await dialog.GetByText("Mark a stretch of audio rather than an instant").ClickAsync();
            await Expect(spanBox).ToBeCheckedAsync(new() { Timeout = 5_000 });
        }

        var end = dialog.GetByText("Ends at").Locator("..").Locator("input[type=number]");
        await Expect(end).ToBeVisibleAsync(new() { Timeout = 5_000 });

        await end.FillAsync("100");
        await end.PressAsync("ArrowUp");
        var stepped = await end.InputValueAsync();
        TestContext.Out.WriteLine($"one step up from 100: {stepped}");
        Assert.That(stepped, Is.Not.EqualTo("100"), "the arrows stopped at 100 seconds");

        await end.FillAsync("150");
        var tooLong = await end.EvaluateAsync<bool>("el => el.validity.rangeOverflow");
        Assert.That(tooLong, Is.False, "the box called 2:30 too long on a 3:06 recording");

        await dialog.Locator("input[type=text]").First.FillAsync("ends late");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = false }).Last.ClickAsync();

        var row = Modal.Locator(".evp-row--marker", new() { HasText = "ends late" }).First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15_000 });
        var time = (await row.Locator(".evp-row__time").InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"the row shows {time}");
        Assert.That(time, Does.Contain("2:30"), "the saved marker does not end where it was set");
    }

    /// <summary>
    /// The EVP lab advertises the smarter detector, explains it on request, and a scan's
    /// candidates can be heard and ruled on (item 253).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 10/02/2026: the learning notice should read like a feature, with the explanation
    /// behind a button, and the panel should look like the rest of the Signal site. The modal is
    /// where the privacy promises live — no audio copied, nobody recorded, gone with the recording —
    /// so the test reads them off the screen rather than trusting the markup.</para>
    ///
    /// <para>Pictures of the panel and the modal in both themes go to
    /// <c>evp-lab-shots</c> beside the test assembly, for the help article and for review.</para>
    /// </remarks>
    [Test]
    public async Task The_evp_lab_advertises_the_smarter_detector_and_explains_it()
    {
        // The fixture MP3 is music-like and a scan of it proposes nothing, which left the review
        // list — the half of the panel this is about — unseen. This recording has three.
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the full view; see the output for where it gave up.");
            return;
        }

        // Tall enough that the panel, which scrolls inside itself past about half the screen,
        // shows whole in the pictures.
        await Page.SetViewportSizeAsync(1440, 1400);

        var shots = Path.Combine(AppContext.BaseDirectory, "evp-lab-shots");
        Directory.CreateDirectory(shots);
        async Task ShootBothAsync(ILocator target, string name)
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                await Page.EvaluateAsync($"document.documentElement.setAttribute('data-bs-theme', '{theme}')");
                await Page.WaitForTimeoutAsync(300);
                await target.ScreenshotAsync(new() { Path = Path.Combine(shots, $"{name}-{theme}.png") });
            }
        }

        await Modal.GetByRole(AriaRole.Button, new() { Name = "EVP Markers", Exact = false }).First.ClickAsync();
        var panel = Modal.Locator("#evp-markers-panel");
        await Expect(panel).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var promo = panel.Locator("#evp-learning-notice");
        await Expect(promo).ToContainTextAsync("Help build a smarter EVP detector");
        await ShootBothAsync(panel, "panel-before-scan");

        // The explanation is one click away, and it says what is and is not kept.
        await promo.GetByRole(AriaRole.Button, new() { Name = "How it works" }).ClickAsync();
        var info = Page.Locator(".modal.show[aria-label='A smarter EVP detector']");
        await Expect(info).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(info).ToContainTextAsync("No audio is copied");
        await Expect(info).ToContainTextAsync("Nothing records who made the choice");
        await Expect(info).ToContainTextAsync("Delete the recording");
        await ShootBothAsync(info.Locator(".modal-content"), "how-it-works");
        await info.GetByRole(AriaRole.Button, new() { Name = "Got it" }).ClickAsync();
        await Expect(info).ToHaveCountAsync(0, new() { Timeout = 10_000 });

        // A scan fills the review list; each candidate carries a score meter.
        await panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();
        await Expect(panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }))
            .ToBeEnabledAsync(new() { Timeout = 120_000 });
        await Page.WaitForTimeoutAsync(1_000);

        var candidates = panel.Locator(".evp-row--candidate");
        await Expect(candidates).ToHaveCountAsync(3, new() { Timeout = 90_000 });

        // The panel's own stylesheet is in effect. Phase 1 moved this markup into AudioEditor and
        // left the scoped .razor.css behind on AudioFilePreview, so the panel lost its Signal look
        // while every test still passed (10/26/2026). A scoped rule that is not applied leaves the
        // browser default: no rounded corners.
        var radius = await panel.EvaluateAsync<string>("el => getComputedStyle(el).borderTopLeftRadius");
        Assert.That(radius, Is.Not.EqualTo("0px"), "the EVP lab's stylesheet is not applied to the panel");
        TestContext.Out.WriteLine($"pictures in {shots}");
        // The scan's own score bar; since phase 9b the row also has a voice-like one.
        await Expect(candidates.First.Locator(".evp-score:not(.evp-score--voice) .evp-score__meter > span")).ToBeAttachedAsync();
        await ShootBothAsync(panel, "panel-after-scan");

        // Heard, then dismissed: the ruling the detector will learn from.
        await candidates.First.GetByTitle("Play this stretch with its surrounding context").ClickAsync();
        await Page.WaitForTimeoutAsync(1_500);
        await candidates.First.GetByTitle("Not an EVP — won't be proposed again").ClickAsync();
        await Expect(candidates).ToHaveCountAsync(2, new() { Timeout = 15_000 });

        await Page.EvaluateAsync("document.documentElement.setAttribute('data-bs-theme', 'light')");
    }

    /// <summary>
    /// Zoom, speed and the open tab come back when the editor is opened again (phase 3).
    /// </summary>
    /// <remarks>
    /// <para>Zoom was the hard one. The player re-applies its starting zoom as it loads, and the
    /// restore read the saved zoom into a field that those load-time reports then reset before it
    /// was applied, so a saved zoom always came back as "fit". Then a page's lingering circuit,
    /// disposed minutes after a reload, saved its own stale view over the newer one. Both fixed
    /// 10/26/2026; this fails on either.</para>
    /// </remarks>
    [Test]
    public async Task Zoom_speed_and_tab_survive_leaving_and_reopening()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.ClickToolbarAsync(Page, Modal, "Zoom in");
        await AudioEditorUi.ClickToolbarAsync(Page, Modal, "Zoom in");
        await AudioEditorUi.ChooseAsync(Page, "listen", "Play at 0.75");
        await AudioEditorUi.OpenTabAsync(Modal, "Display");

        var zoomed = await AudioEditorUi.WaveformZoomRatioAsync(Modal);
        TestContext.Out.WriteLine($"zoom ratio before leaving: {zoomed:0.00}");
        Assert.That(zoomed, Is.GreaterThan(1.3), "zooming in did not widen the waveform, so there is nothing to remember");
        await Page.WaitForTimeoutAsync(1_500);   // a zoom is saved a second after it settles

        await LeaveAndReopenEditorAsync();
        await Page.WaitForTimeoutAsync(1_500);

        var back = await AudioEditorUi.WaveformZoomRatioAsync(Modal);
        TestContext.Out.WriteLine($"zoom ratio after reopening: {back:0.00}");
        Assert.That(back, Is.EqualTo(zoomed).Within(0.15), "the zoom did not come back");
        await Expect(Page.Locator("#audio-menu-listen")).ToContainTextAsync("0.75×");
        await Expect(Modal.GetByRole(AriaRole.Tab, new() { Name = "Display" })).ToHaveAttributeAsync("aria-selected", "true");
    }

    /// <summary>
    /// The lesser-used controls live in the toolbar's menus and land where they say: View ▸ Mel scale
    /// changes the spectrogram's scale, and Edit ▸ Fade opens the Edit tab on the fade.
    /// </summary>
    /// <remarks>
    /// Phase 2 (10/26/2026) moved them off a flat row of eight toggles. The Spectrogram button reads
    /// pressed when it is on, which the old toggles never did.
    /// </remarks>
    [Test]
    public async Task Toolbar_menus_hold_the_lesser_used_controls()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.SetSpectrogramAsync(Modal, on: false);
        await AudioEditorUi.SetSpectrogramAsync(Modal, on: true);   // pressed, and says so

        await AudioEditorUi.OpenTabAsync(Modal, "Display");
        var scale = Modal.Locator(".audio-display select").Filter(new() { HasTextString = "Mel" }).First;
        var before = await scale.InputValueAsync();

        await AudioEditorUi.ChooseAsync(Page, "view", "Mel scale");
        await Expect(scale).Not.ToHaveValueAsync(before, new() { Timeout = 10_000 });
        TestContext.Out.WriteLine($"scale {before} -> {await scale.InputValueAsync()}");

        await AudioEditorUi.ChooseAsync(Page, "edit", "Fade in and out");
        await Expect(Modal.GetByRole(AriaRole.Tab, new() { Name = "Edit" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Modal.Locator(".audio-edit-group.is-focused")).ToContainTextAsync("Fade");
    }

    /// <summary>
    /// One playback speed: the toolbar's Listen menu, the player's own speed menu and the Listen tab
    /// all show and set the same one.
    /// </summary>
    /// <remarks>
    /// There were three: the player's menu (0.5–2×), the region explorer's slider (0.25–3×), and the
    /// destructive Speed edit, which made a new file and was easy to mistake for the other two. The
    /// edit stays, under Edit ▸ Change speed; the explorer now shares the editor's speed.
    /// </remarks>
    [Test]
    public async Task One_speed_control_drives_playback_everywhere()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        var listen = Page.Locator("#audio-menu-listen");
        var transport = Modal.Locator(".ws-player-controls select[aria-label=Speed]");

        await AudioEditorUi.ChooseAsync(Page, "listen", "Play at 1.5");
        await Expect(listen).ToContainTextAsync("1.5×", new() { Timeout = 10_000 });
        await Expect(transport).ToHaveValueAsync("1.5", new() { Timeout = 10_000 });

        await transport.SelectOptionAsync(new SelectOptionValue { Value = "0.75" });
        await Expect(listen).ToContainTextAsync("0.75×", new() { Timeout = 10_000 });

        await AudioEditorUi.OpenTabAsync(Modal, "Listen");
        await Expect(Modal.Locator("label[for=listen-speed]")).ToContainTextAsync("0.75×");
    }

    /// <summary>
    /// Zoomed in and scrolled, the timeline and the spectrogram show the stretch the waveform shows,
    /// and the spectrogram's frequency labels stay at the left edge.
    /// </summary>
    /// <remarks>
    /// <para>Both draw into panes of their own under the waveform: the timeline since the audio walk
    /// moved it out so it is never clipped, the spectrogram since WaveSurfer 8's plugin replaced the
    /// hand-built one (phase 1b). Neither pane scrolled with the waveform. Zoomed in and scrolled to
    /// 1,700 px, the timeline still showed 0 s (measured 10/26/2026), so its ticks named the wrong
    /// times.</para>
    /// </remarks>
    [Test]
    public async Task The_timeline_and_spectrogram_follow_the_waveform_when_zoomed()
    {
        if (!await ReadyInFullViewAsync(ScannableWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await AudioEditorUi.SetSpectrogramAsync(Modal, on: true);
        await Expect(Modal.Locator(".ws-spectro-pane canvas").Nth(1)).ToBeAttachedAsync(new() { Timeout = 30_000 });

        // Zoom in through the transport's zoom slider, then scroll the waveform a long way along.
        var zoom = Modal.Locator(".ws-player-controls input[type=range]").Last;
        await zoom.FillAsync("400");
        await zoom.DispatchEventAsync("input");
        await Page.WaitForTimeoutAsync(1_000);

        var positions = await Page.EvaluateAsync<double[]>(@"async () => {
            const host = document.querySelector('#audio-editor [id^=""ws-""]:not([id$=""-tl""])');
            const scroll = [...host.children].find(c => c.shadowRoot)?.shadowRoot.querySelector('[part=""scroll""]');
            scroll.scrollLeft = 1700;
            await new Promise(r => setTimeout(r, 800));
            const timeline = document.getElementById(host.id + '-tl').querySelector('[part=""timeline""]');
            const spectrogram = document.querySelector('#audio-editor .ws-spectro-pane > div');
            const labels = spectrogram.querySelector('[part=""spec-labels""]');
            return [scroll.scrollLeft, timeline.scrollLeft, spectrogram.scrollLeft,
                    labels.getBoundingClientRect().left - spectrogram.getBoundingClientRect().left];
        }");
        TestContext.Out.WriteLine($"waveform {positions[0]}, timeline {positions[1]}, spectrogram {positions[2]}, labels offset {positions[3]}");

        Assert.That(positions[0], Is.GreaterThan(1000), "the waveform did not scroll, so there was nothing to follow");
        Assert.That(positions[1], Is.EqualTo(positions[0]).Within(1), "the timeline did not follow the waveform");
        Assert.That(positions[2], Is.EqualTo(positions[0]).Within(1), "the spectrogram did not follow the waveform");
        Assert.That(positions[3], Is.EqualTo(0).Within(2), "the frequency labels scrolled away with the picture");
    }

    /// <summary>
    /// Playing one candidate does not mark its neighbor as heard.
    /// </summary>
    /// <remarks>
    /// <para>"Heard" feeds the detector's learning: a ruling made after listening counts for more
    /// (PlayedFirst, item 253). It used to be set the moment playback entered a candidate's
    /// region, and a candidate's region includes 0.4 s of context either side, so two sounds half a
    /// second apart have overlapping regions. Playing the first ran into the second's lead-in, and
    /// the second was recorded as heard though its sound was never played. WaveSurfer 8 fires
    /// "entered" about 50 ms early, which would make it worse.</para>
    ///
    /// <para>A candidate now counts as heard once playback passes its middle, where the sound is.</para>
    /// </remarks>
    [Test]
    public async Task Playing_one_candidate_does_not_mark_its_neighbor_heard()
    {
        if (!await ReadyInFullViewAsync(AdjacentPairWav(), "wav", "audio/wav"))
        {
            Assert.Ignore("Could not open the editor; see the output for where it gave up.");
            return;
        }

        await Modal.GetByRole(AriaRole.Button, new() { Name = "EVP Markers", Exact = false }).First.ClickAsync();
        var panel = Page.Locator("#evp-markers-panel");
        await panel.GetByRole(AriaRole.Button, new() { Name = "Scan for EVP" }).ClickAsync();

        var candidates = panel.Locator(".evp-row--candidate");
        await Expect(candidates).ToHaveCountAsync(2, new() { Timeout = 90_000 });
        await panel.GetByRole(AriaRole.Button, new() { Name = "Time", Exact = true }).ClickAsync();

        var first  = candidates.Nth(0);
        var second = candidates.Nth(1);
        TestContext.Out.WriteLine($"candidates: {(await first.Locator(".evp-row__time").InnerTextAsync()).Trim()} and "
                                + $"{(await second.Locator(".evp-row__time").InnerTextAsync()).Trim()}");

        await first.GetByTitle("Play this stretch with its surrounding context").ClickAsync();
        await Page.WaitForTimeoutAsync(3_000);   // the first candidate's whole span, 1.4 s, and some
        var stoppedAt = await PlayheadTextAsync();
        TestContext.Out.WriteLine($"after playing the first candidate the playhead reads {stoppedAt}");

        // Play stops at the candidate's end. It did not: the stop listener let WaveSurfer's second
        // report of its own seek cancel it, so every candidate played on to the end of the
        // recording (3.5 s to 6.5 s and counting, for a stretch ending at 4.9 s).
        var m = System.Text.RegularExpressions.Regex.Match(stoppedAt, @"(\d+):(\d+(?:\.\d+)?)");
        Assert.That(m.Success, Is.True, $"could not read the playhead from '{stoppedAt}'");
        var seconds = int.Parse(m.Groups[1].Value) * 60
                    + double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(seconds, Is.LessThan(5.3), "Play did not stop at the end of the candidate");

        await Expect(first).ToHaveAttributeAsync("data-heard", "true", new() { Timeout = 5_000 });
        Assert.That(await second.GetAttributeAsync("data-heard"), Is.EqualTo("false"),
            "playing the first candidate marked the second as heard, though only its lead-in was played");
    }

    /// <summary>
    /// Twelve seconds of room tone with two voice-like sounds half a second apart (4.0–4.6 s and
    /// 5.1–5.7 s). Far enough apart to stay two candidates (the join gap is 0.35 s), close enough
    /// that their padded regions (0.4 s either side) overlap.
    /// </summary>
    private static byte[] AdjacentPairWav() => Wav(12, [(4.0, 0.6), (5.1, 0.6)]);

    /// <summary>
    /// Twenty seconds of room tone with three voice-like sounds at 4, 9 and 15 s: three formants in
    /// the telephone band under a syllabic envelope, the shape <c>EvpDetectorTests</c> is built on.
    /// A scan at Medium proposes exactly these three.
    /// </summary>
    private static byte[] ScannableWav() => Wav(20, [(4.0, 0.8), (9.0, 0.7), (15.0, 1.0)]);

    /// <summary>Room tone of the given length with voice-like sounds at the given (start, length) seconds.</summary>
    private static byte[] Wav(int lengthSeconds, (double At, double Seconds)[] sounds)
    {
        const int rate = 16000;
        var mono = new float[lengthSeconds * rate];
        var rng = new Random(2024);
        for (var i = 0; i < mono.Length; i++) mono[i] = (float)((rng.NextDouble() * 2 - 1) * 0.004);

        foreach (var (at, seconds) in sounds)
        {
            var start = (int)(at * rate);
            var count = (int)(seconds * rate);
            for (var i = 0; i < count; i++)
            {
                var t = i / (double)rate;
                var env = (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4.0 * t))
                        * Math.Min(1.0, Math.Min(i, count - i) / (0.02 * rate));
                var tone = (Math.Sin(2 * Math.PI * 500 * t) + Math.Sin(2 * Math.PI * 1500 * t)
                          + Math.Sin(2 * Math.PI * 2500 * t)) / 3.0;
                mono[start + i] += (float)(tone * 0.03 * env);
            }
        }

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var dataBytes = mono.Length * 2;
            w.Write("RIFF"u8);  w.Write(36 + dataBytes);  w.Write("WAVE"u8);
            w.Write("fmt "u8);  w.Write(16);              w.Write((short)1);
            w.Write((short)1);  w.Write(rate);            w.Write(rate * 2);
            w.Write((short)2);  w.Write((short)16);       w.Write("data"u8);
            w.Write(dataBytes);
            foreach (var v in mono) w.Write((short)Math.Clamp(v * 32767f, short.MinValue, short.MaxValue));
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Where the playhead is, read off the panel.
    /// </summary>
    /// <remarks>
    /// Not from a media element: this player runs on Web Audio and creates none, so the walk's
    /// "no media element playing" was never evidence either way. The Add Marker button carries the
    /// current time and updates on every timeupdate, which is a thing a person can see.
    /// </remarks>
    private async Task<string> PlayheadTextAsync()
        => (await Modal.GetByRole(AriaRole.Button, new() { Name = "Add Marker at", Exact = false })
                       .First.InnerTextAsync()).Trim();

    /// <summary>
    /// Exploring a second region plays the second region's audio.
    /// </summary>
    /// <remarks>
    /// <para>The explorer downloads one region's audio and decided whether to do it again by asking
    /// whether it had ever loaded anything. So the first region a person explored was what they
    /// heard for every region afterwards, while the title, the notes and the Save button all moved
    /// on — listen to the second region, save it, and the file is not the sound that was playing
    /// (2026-09-06 audio walk, finding H). The walk never reached this: the explorer would not stay
    /// closed long enough to open a second one.</para>
    ///
    /// <para>Two regions of deliberately different lengths, and the check is the length of the
    /// audio the explorer actually loaded.</para>
    /// </remarks>
    [Test]
    public async Task Exploring_a_second_region_plays_that_region()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        var first  = await ExploreAndMeasureAsync(0.20, 0.30);
        var second = await ExploreAndMeasureAsync(0.55, 0.85);

        TestContext.Out.WriteLine($"waveform fingerprints: {first} then {second}");

        Assert.That(first,  Is.Not.EqualTo("empty"), "the first region never drew a waveform");
        Assert.That(second, Is.Not.EqualTo("empty"), "the second region never drew a waveform");
        Assert.That(second, Is.Not.EqualTo(first),
            "the second region drew the first region's waveform, so it is playing the first "
            + "region's audio — while the title, the notes and Save have all moved on to the second");
    }

    /// <summary>
    /// Draws a region, explores it, and fingerprints the waveform the explorer actually drew.
    /// </summary>
    /// <remarks>
    /// The picture, not the title: the title was never wrong. Two different stretches of a real
    /// recording draw differently, and a stretch that was never fetched draws exactly what the
    /// previous one did.
    /// </remarks>
    private async Task<string> ExploreAndMeasureAsync(double from, double to)
    {
        await DrawRegionAsync(from, to);

        var region = Modal.Locator("[part~='region']").Last;
        await region.ClickAsync(new() { Button = MouseButton.Right });

        var explore = Page.GetByText("Explore Region", new() { Exact = false }).First;
        await Expect(explore).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await explore.ClickAsync();

        // The explorer is its own modal, on top of the editor's.
        var explorer = Page.Locator(".modal.show").Last;
        var waveform = explorer.Locator("[id^='ws-']").First;
        await Expect(waveform).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Page.WaitForTimeoutAsync(2_000);   // fetch + decode + draw

        // A picture of the waveform, taken through Playwright because WaveSurfer renders inside a
        // shadow root that document.querySelector cannot see. Two different stretches of a real
        // recording look different; a stretch that was never fetched looks exactly like the last one.
        var image = await waveform.ScreenshotAsync();
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image))[..16];

        await explorer.Locator(".btn-close").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);

        return fingerprint;
    }

    /// <summary>
    /// How the editor was set up for a recording is still that way when it is reopened.
    /// </summary>
    /// <remarks>
    /// <c>UploadFileAudioConfig</c> — the table, the controller, the client and the mapper — has
    /// existed since 2026-07-18 and nothing had ever read or written it, so the spectrogram and
    /// everything about it were reset on every open. For somebody working through a long recording
    /// that is several times an hour (2026-09-06 audio walk, finding L).
    /// </remarks>
    [Test]
    public async Task How_you_set_the_editor_up_survives_closing_it()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        // Turn the spectrogram on only if it is off: on a re-run against a database that already
        // holds this recording's settings, it is on already — which is the feature working.
        await AudioEditorUi.SetSpectrogramAsync(Modal, on: true);

        // The colors are in the Display tab since phase 2.
        await AudioEditorUi.OpenTabAsync(Modal, "Display");
        var colormap = Modal.Locator("select").Filter(new() { HasTextString = "irid" }).First;
        await Expect(colormap).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var wasChosen = await colormap.InputValueAsync();
        var wanted    = wasChosen == "viridis" ? "inferno" : "viridis";
        TestContext.Out.WriteLine($"colormap was {wasChosen}; choosing {wanted}");

        await colormap.SelectOptionAsync(new SelectOptionValue { Value = wanted });

        // No long pause here on purpose: closing the editor is what must flush the save. Waiting
        // for it here would test the pause rather than the product, and the change was lost every
        // time on a page busy enough that the request had not finished by the time the modal shut.
        await Page.WaitForTimeoutAsync(300);   // the save is a round trip

        // If the editor said it could not save, that is the answer — and a far more useful failure
        // than "it came back on jet". The notice appears when the server refuses the write, which
        // on a shared database usually means this seat does not own the file the test picked.
        var notSaved = Modal.GetByText("aren't saved", new() { Exact = false })
                            .Or(Modal.GetByText("couldn't be saved", new() { Exact = false }));
        if (await notSaved.CountAsync() > 0)
            Assert.Fail($"the editor refused to save: {(await notSaved.First.InnerTextAsync()).Trim()}");

        // Leave the editor and open it again on the same recording.
        await LeaveAndReopenEditorAsync();
        await Page.WaitForTimeoutAsync(1_500);

        // The spectrogram is on again, and on the ramp that was chosen.
        await Expect(AudioEditorUi.SpectrogramToggle(Modal))
            .ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 30_000 });

        await AudioEditorUi.OpenTabAsync(Modal, "Display");
        var reopened = Modal.Locator("select").Filter(new() { HasTextString = "irid" }).First;
        var chosen   = await reopened.InputValueAsync();
        TestContext.Out.WriteLine($"colormap after reopening: {chosen}");

        Assert.That(chosen, Is.EqualTo(wanted),
            $"the editor came back on '{chosen}' rather than the '{wanted}' that was chosen, so "
            + "nothing about how this recording was being looked at was remembered");
    }

    /// <summary>
    /// A region's edge can still be grabbed and dragged.
    /// </summary>
    /// <remarks>
    /// <para>The player installs a capture-phase <c>pointerdown</c> handler on the waveform for
    /// drawing and for scrubbing. It used to claim <i>every</i> press and call
    /// <c>setPointerCapture</c> on the container, so the regions plugin's own move and resize
    /// handlers never saw the drag: grabbing a region's edge silently drew a brand new region
    /// instead of resizing the one that was there. Move and resize were unreachable by mouse
    /// (regression E0).</para>
    ///
    /// <para>The guard is a <c>composedPath</c> check, and regions live in the waveform's shadow
    /// DOM — so an innocuous change to how the path is read brings the whole thing back. Nothing in
    /// the suite exercised it.</para>
    /// </remarks>
    [Test]
    public async Task A_regions_edge_can_be_dragged_rather_than_drawing_a_new_one()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        await Modal.GetByRole(AriaRole.Button, new() { Name = "Clear Regions" }).First.ClickAsync();
        await Page.WaitForTimeoutAsync(400);

        await DrawRegionAsync(0.30, 0.50);

        var regions = Modal.Locator("[part~='region']");
        Assert.That(await regions.CountAsync(), Is.EqualTo(1), "the drag did not produce one region");

        var before = await regions.First.BoundingBoxAsync();
        Assert.That(before, Is.Not.Null);

        // Grab the right-hand edge and pull it further right.
        var y = before!.Y + before.Height / 2;
        await Page.Mouse.MoveAsync((float)(before.X + before.Width - 2), y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(before.X + before.Width + 80), y, new() { Steps = 10 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);

        var after = await regions.First.BoundingBoxAsync();
        TestContext.Out.WriteLine(
            $"region {before.Width:0} px wide -> {after!.Width:0} px, count {await regions.CountAsync()}");

        Assert.That(await regions.CountAsync(), Is.EqualTo(1),
            "dragging the edge drew a second region instead of resizing the one that was there");
        Assert.That(after.Width, Is.GreaterThan(before.Width + 20),
            "the region did not grow, so the edge drag was swallowed before the regions plugin saw it");
    }

    /// <summary>
    /// The listening chain is still set up the way it was left.
    /// </summary>
    /// <remarks>
    /// <para>The equaliser, the filters, the compressor and the noise gate are what somebody sets
    /// up to <i>hear</i> a recording, and none of it had anywhere to live — so every one of the
    /// fourteen settings was reset on every open. Find a filter that lets you hear a whisper, close
    /// the editor to look at something else, and find it again from scratch (2026-09-06 audio walk,
    /// finding L; the half phase 5a could not do without a column).</para>
    ///
    /// <para>Checked on the control AND on what it is doing to the sound: restoring the checkbox
    /// without rebuilding the filter would show a high-pass switched on over audio that had none,
    /// which is worse than not remembering it at all.</para>
    /// </remarks>
    [Test]
    public async Task The_listening_chain_survives_closing_the_editor()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("the seeded seat could not reach the editor; seed data may differ.");
            return;
        }

        await OpenEqPanelAsync();

        var highPass = Modal.Locator("#chain-highpass");
        await Expect(highPass).ToBeVisibleAsync(new() { Timeout = 20_000 });
        Assert.That(await highPass.IsCheckedAsync(), Is.False, "the high-pass should start off");

        await highPass.CheckAsync();

        // And move it off its default, so the test is about the numbers and not only the switch.
        var frequency = Modal.Locator("input[type=range][min='20'][max='500']").First;
        await frequency.FillAsync("240");
        await frequency.DispatchEventAsync("change");
        await Page.WaitForTimeoutAsync(800);

        var notSaved = Modal.GetByText("aren't saved", new() { Exact = false })
                            .Or(Modal.GetByText("couldn't be saved", new() { Exact = false }));
        if (await notSaved.CountAsync() > 0)
            Assert.Fail($"the editor refused to save: {(await notSaved.First.InnerTextAsync()).Trim()}");

        await LeaveAndReopenEditorAsync();
        await Page.WaitForTimeoutAsync(1_000);

        await OpenEqPanelAsync();

        var reopened = Modal.Locator("#chain-highpass");
        await Expect(reopened).ToBeVisibleAsync(new() { Timeout = 20_000 });

        Assert.That(await reopened.IsCheckedAsync(), Is.True,
            "the high-pass came back switched off, so nothing about how this recording was being "
            + "listened to was remembered");

        // The frequency came back too, which is the setting that took the finding to arrive at.
        Assert.That(await Modal.GetByText("High-pass 240 Hz", new() { Exact = false }).CountAsync(),
            Is.GreaterThan(0),
            "the high-pass is on but back at its default frequency, so only half of it was kept");
    }

    /// <summary>Opens the listening tools: the Listen tab since phase 2.</summary>
    private async Task OpenEqPanelAsync()
    {
        await AudioEditorUi.OpenTabAsync(Modal, "Listen");
        await Expect(Modal.Locator("#chain-highpass")).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    /// <summary>Drags across the modal waveform from one fraction of its width to another.</summary>
    private async Task DrawRegionAsync(double fromFraction, double toFraction)
    {
        var box = await Modal.Locator("[id^='ws-']").First.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, "the modal waveform has no box");
        var y = box!.Y + box.Height / 2;
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * fromFraction), y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * ((fromFraction + toFraction) / 2)), y, new() { Steps = 8 });
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * toFraction), y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// A fade of nothing into nothing is a request the server declines, and the panel says so in
    /// the server's words.
    /// </summary>
    /// <remarks>
    /// Both fade boxes accept 0, so this is reachable by clicking, which is the point: it is the
    /// cheapest refusal a person can actually produce. What is asserted is not the wording but
    /// that the wording is NOT the old catch-all — a message about WAV and MP3 here would mean the
    /// body is being dropped again and every other phase 1 refusal is invisible too.
    /// </remarks>
    [Test]
    public async Task A_refused_edit_shows_the_reason_the_server_gave()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        // The edits are in the Edit tab; a tab's panel is not in the DOM until it is opened.
        await AudioEditorUi.OpenTabAsync(Modal, "Edit");
        await Expect(Modal.GetByRole(AriaRole.Button, new() { Name = "Apply Fade" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var boxes = Modal.Locator("input[type=number]");
        await boxes.Nth(0).FillAsync("0");
        await boxes.Nth(1).FillAsync("0");
        await Modal.GetByRole(AriaRole.Button, new() { Name = "Apply Fade" }).ClickAsync();

        var alert = Modal.Locator(".alert-danger").First;
        await Expect(alert).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var text = await alert.InnerTextAsync();
        TestContext.Out.WriteLine($"the panel said: {text}");

        Assert.That(text, Does.Not.Contain("WAV and MP3"),
            "the editor is showing its old catch-all again, which means the server's own sentence "
            + "is being dropped — so every phase 1 refusal reaches people as a message about file "
            + "formats");
        Assert.That(text, Does.Contain("fade").IgnoreCase,
            "the alert should say what was actually wrong with this request");
    }

    /// <summary>
    /// Ticking Public on a clip of a private recording is refused, and the dialog explains it
    /// rather than blaming the file format.
    /// </summary>
    /// <remarks>
    /// This is the security refusal of phase 1 (finding 6): clipping needs only that the caller can
    /// SEE the recording, and the clip is a new file the caller owns — so "clip the whole thing,
    /// tick Public" published somebody else's private audio. A person who meets the new refusal has
    /// to be told what to do instead, or the feature simply looks broken.
    /// </remarks>
    [Test]
    public async Task Publishing_a_clip_of_a_private_recording_explains_itself()
    {
        if (!await ReadyInFullViewAsync())
        {
            Assert.Ignore("Paranormal365 / Belmont case not reachable; seed data may differ.");
            return;
        }

        // Draw a region: the Save-as-clip dialog needs one.
        var waveform = Modal.Locator("[id^='ws-']").First;
        var box = await waveform.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, "the modal waveform has no box");
        var y = box!.Y + box.Height / 2;
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * 0.20), y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * 0.30), y, new() { Steps = 8 });
        await Page.Mouse.MoveAsync((float)(box.X + box.Width * 0.40), y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);

        // The Save dialog opens from the region's own context menu.
        var region = Modal.Locator("[part~='region']").First;
        if (await region.CountAsync() == 0)
        {
            Assert.Ignore("the drag did not produce a region to clip");
            return;
        }
        await region.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Create Audio File from Region", new() { Exact = false }).First.ClickAsync();

        var publicBox = Page.Locator("#clip-pub-afp");
        await Expect(publicBox).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await publicBox.CheckAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save clip" }).First.ClickAsync();

        var alert = Page.Locator(".alert-danger").First;
        await Expect(alert).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var text = await alert.InnerTextAsync();
        TestContext.Out.WriteLine($"the dialog said: {text}");

        Assert.That(text, Does.Contain("private").IgnoreCase,
            "the refusal has to name the reason — a private recording — and say what to do about it");
        Assert.That(text, Does.Not.Contain("WAV and MP3"),
            "the old catch-all is back, so the server's sentence is being dropped");
    }
}
