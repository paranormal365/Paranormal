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
public class AudioEditorRefusalTests : BenTestBase
{
    private static readonly string TestAudioPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    private ILocator Modal => Page.Locator(".modal.show").First;

    /// <summary>Signs in, uploads the fixture to the seeded case, opens the full-view editor.</summary>
    private async Task<bool> ReadyInFullViewAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        if (!await OpenOrgCaseAsync("Paranormal365", "Belmont")) return false;

        // The upload input is display:none behind its label, so waiting for IT to be visible times
        // out on a tab that opened perfectly.
        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);
        await Expect(Page.Locator("#case-file-upload")).ToBeAttachedAsync(new() { Timeout = 15_000 });

        await Page.Locator("#case-file-upload").SetInputFilesAsync(TestAudioPath);
        try { await Expect(Page.Locator("[id^='ws-']").First).ToBeVisibleAsync(new() { Timeout = 60_000 }); }
        catch { return false; }

        await Page.Locator("[id^='afp-']").First.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Open Full View", new() { Exact = false }).ClickAsync();
        await Expect(Modal).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Modal.GetByRole(AriaRole.Button, new() { Name = "Clear Regions" }))
            .ToBeEnabledAsync(new() { Timeout = 90_000 });
        return true;
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

        await Modal.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = false }).First.ClickAsync();
        var readout = Modal.Locator("#edit-region-readout").First;
        await Expect(readout).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var before = (await readout.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"the panel showed: {before}");
        Assert.That(before, Does.Not.Contain("Draw a region"),
            "the drag did not produce a selection, so this test cannot say anything about B");

        await Modal.GetByRole(AriaRole.Button, new() { Name = "Silence", Exact = false }).First.ClickAsync();
        await Page.WaitForTimeoutAsync(2_000);   // detection walks the whole decoded buffer

        var after = (await readout.InnerTextAsync()).Trim();
        TestContext.Out.WriteLine($"after silence detection: {after}");

        Assert.That(after, Is.EqualTo(before),
            "silence detection moved the edit target: Cut would now destroy a stretch the machine "
            + "chose rather than the one the person drew");
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
        await Modal.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = false }).First.ClickAsync();

        var readout = Modal.Locator("#edit-region-readout").First;
        await Expect(readout).ToBeVisibleAsync(new() { Timeout = 15_000 });
        Assert.That((await readout.InnerTextAsync()).Trim(), Does.Not.Contain("Draw a region"),
            "no selection to silence, so this says nothing about the edit");

        // By id, not by name: the toolbar's silence-DETECTION toggle is also called "Silence" and
        // comes first in the DOM, so a name lookup lands on it. That is what the walk hit, which is
        // why finding E read as "Silence produced nothing and said nothing" — detection produces no
        // clip and no error, correctly.
        await Modal.Locator("#edit-op-silence").ClickAsync();

        // Either a saved clip appears or the panel explains itself. Silence did neither.
        var savedClips = Modal.GetByText("Saved Clips", new() { Exact = false }).First;
        var editError  = Modal.Locator(".alert-danger").First;

        try
        {
            await Expect(savedClips.Or(editError)).ToBeVisibleAsync(new() { Timeout = 90_000 });
        }
        catch (Exception)
        {
            Assert.Fail("Silence produced neither a saved clip nor a message within 90 seconds — "
                      + "the walk's finding E, now with a region a person actually drew.");
            return;
        }

        if (await editError.CountAsync() > 0 && await editError.IsVisibleAsync())
            Assert.Fail($"Silence was refused: {(await editError.InnerTextAsync()).Trim()}");

        await Expect(savedClips).ToBeVisibleAsync();
        TestContext.Out.WriteLine("Silence produced a saved clip.");
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

        // The edit panel is behind the toolbar's "Edit" toggle; it does not exist in the DOM
        // until it is opened.
        await Modal.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = false }).First.ClickAsync();
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

        await Page.GetByRole(AriaRole.Button, new() { Name = "Save as WAV" }).First.ClickAsync();

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
