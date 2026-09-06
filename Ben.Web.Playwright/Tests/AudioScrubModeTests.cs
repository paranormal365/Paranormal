using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for backlog item #7 — the drag-mode toggle on the full-view audio player
/// (<c>AudioFilePreview.razor</c>/<c>WaveSurferPlayer.razor.js</c>): click-and-drag on the
/// waveform either draws a selection region (default) or scrubs the playhead, switchable
/// via a toolbar toggle button. Uploads a real audio fixture to a case's Files tab to reach
/// the player, since no audio file exists in dev seed data.
/// </summary>
[TestFixture]
[Category("AudioPlayer")]
public class AudioScrubModeTests : BenTestBase
{
    private static readonly string TestAudioPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "test-audio.mp3");

    // ── Helper: navigate to Daniel Park's case Files tab as Sarah, upload the fixture ──

    private async Task<bool> NavigateToTghCaseFilesTabAsync()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah — TGH administrator

        // Through the maintained helpers, not a hand-rolled walk: the old version clicked the
        // organisation's *name*, which is a plain grid cell on this site and navigates nowhere —
        // the exact trap OpenOrganizationAsync exists to avoid. The walk then stalled waiting for
        // a Cases tab on a page it had never left.
        if (!await OpenOrganizationAsync("Paranormal365")) return false;

        await OpenTabAsync("Cases", Main.GetByRole(AriaRole.Button, new() { Name = "New Case" }));

        // The LINK, not any text mentioning Belmont: GetByText.First used to land on the
        // card's "4512 Belmont Blvd" address line, which takes the click and goes nowhere —
        // ClickUntilUrlAsync then times out against a perfectly healthy page.
        var caseItem = Main.GetByRole(AriaRole.Link)
            .Filter(new() { HasTextString = "Belmont" }).First;
        if (!await caseItem.IsVisibleAsync()) return false;
        await ClickUntilUrlAsync(caseItem, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");

        // The upload input is display:none behind its "Upload File" label, and OpenTabAsync waits
        // for the expected element to be VISIBLE — so this waited on a hidden input and timed out on
        // a Files tab that had opened perfectly. Found by the 2026-09-06 audio walk, the first time
        // this test ran under the harness at all.
        await OpenTabAsync("Files", Main.GetByText("Upload File", new() { Exact = false }).First);
        await Expect(Page.Locator("#case-file-upload")).ToBeAttachedAsync(new() { Timeout = 15_000 });
        return true;
    }

    /// <summary>Uploads the fixture MP3 and waits for its compact waveform preview to render.</summary>
    private async Task<bool> UploadTestAudioAsync()
    {
        await Page.Locator("#case-file-upload").SetInputFilesAsync(TestAudioPath);

        // Upload (SignalR round-trip) + fetch-back + client-side decode for a ~7MB file
        // can take a while — generous timeout to avoid flaking on a slow CI runner.
        var waveform = Page.Locator("[id^='ws-']").First;
        try { await Expect(waveform).ToBeVisibleAsync(new() { Timeout = 45_000 }); }
        catch { return false; }
        return true;
    }

    /// <summary>Right-clicks the compact preview and opens the full-view modal.</summary>
    private async Task OpenFullViewAsync()
    {
        var wrapper = Page.Locator("[id^='afp-']").First;
        await wrapper.ClickAsync(new() { Button = MouseButton.Right });
        await Page.GetByText("Open Full View", new() { Exact = false }).ClickAsync();
        await Page.WaitForTimeoutAsync(300); // modal open animation
    }

    [Test]
    public async Task DragModeToggle_DefaultsToCreateRegion_AndTogglesToScrub()
    {
        if (!await NavigateToTghCaseFilesTabAsync())
        {
            // A precondition of the environment, not a result: Ignore leaves the run honest
            // about what was not exercised. Assert.Pass reported a green test for a browser that
            // never reached the page (2026-09-05 audit, F19).
            Assert.Ignore("TGH org not visible; seed data may differ.");
            return;
        }
        if (!await UploadTestAudioAsync())
        {
            // Not a precondition: uploading the file and getting a player is the behaviour under
            // test, so failing to do it is a failure.
            Assert.Fail("Audio upload/preview did not render in time.");
            return;
        }

        await OpenFullViewAsync();

        // Defaults to region-draw mode
        var createRegionBtn = Page.GetByTitle("Create Region");
        await Expect(createRegionBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Toggling switches the tooltip/state to scrub mode
        await createRegionBtn.ClickAsync();
        var scrubBtn = Page.GetByTitle("Scrub Playhead");
        await Expect(scrubBtn).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // And back again
        await scrubBtn.ClickAsync();
        await Expect(Page.GetByTitle("Create Region")).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task DragModeToggle_UpdatesHintText()
    {
        if (!await NavigateToTghCaseFilesTabAsync())
        {
            // A precondition of the environment, not a result: Ignore leaves the run honest
            // about what was not exercised. Assert.Pass reported a green test for a browser that
            // never reached the page (2026-09-05 audit, F19).
            Assert.Ignore("TGH org not visible; seed data may differ.");
            return;
        }
        if (!await UploadTestAudioAsync())
        {
            // Not a precondition: uploading the file and getting a player is the behaviour under
            // test, so failing to do it is a failure.
            Assert.Fail("Audio upload/preview did not render in time.");
            return;
        }

        await OpenFullViewAsync();

        await Expect(Page.GetByText("Click & drag to mark a region", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.GetByTitle("Create Region").ClickAsync();

        await Expect(Page.GetByText("Click & drag the waveform to scrub", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 5_000 });
    }
}
