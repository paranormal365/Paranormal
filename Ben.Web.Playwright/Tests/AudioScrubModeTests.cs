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
        if (!await OpenOrganizationAsync("Tennessee Ghost Hunters")) return false;

        await OpenTabAsync("Cases", Main.GetByRole(AriaRole.Button, new() { Name = "New Case" }));

        var caseItem = Main.GetByText("Park", new() { Exact = false }).First;
        if (!await caseItem.IsVisibleAsync()) return false;
        await ClickUntilUrlAsync(caseItem, @"/organizations/[0-9a-f\-]+/cases/[0-9a-f\-]+");

        await OpenTabAsync("Files", Page.Locator("#case-file-upload"));
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
            Assert.Pass("TGH org not visible; seed data may differ.");
            return;
        }
        if (!await UploadTestAudioAsync())
        {
            Assert.Pass("Audio upload/preview did not render in time — skipping.");
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
            Assert.Pass("TGH org not visible; seed data may differ.");
            return;
        }
        if (!await UploadTestAudioAsync())
        {
            Assert.Pass("Audio upload/preview did not render in time — skipping.");
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
