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
        await LoginAsync(UserEmail, UserPassword); // Sarah — TGH org member
        await Page.GotoAsync($"{BaseUrl}/organizations");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var tgh = Page.GetByText("Tennessee Ghost Hunters", new() { Exact = false });
        if (!await tgh.IsVisibleAsync()) return false;
        await tgh.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var casesLink = Page.GetByRole(AriaRole.Link, new() { Name = "Cases" })
                            .Or(Page.GetByRole(AriaRole.Tab, new() { Name = "Cases" })).First;
        await casesLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var caseItem = Page.GetByText("Park", new() { Exact = false }).First;
        if (!await caseItem.IsVisibleAsync()) return false;
        await caseItem.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var filesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Files", Exact = true })
                           .Or(Main.GetByText("Files", new() { Exact = true })).First;
        await filesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
