using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Editing a title can be undone, like editing anything else on the timeline.
/// </summary>
/// <remarks>
/// Titles were the one thing whose edits pushed nothing onto the undo stack, so Ctrl+Z after
/// changing a title undid whatever had happened before it instead — worse than doing nothing
/// (2026-09-05 audit, titles-4).
/// </remarks>
public sealed class TitleEditTests
{
    private static (ClipStore Store, TextOverlay Title) Store()
    {
        var store = new ClipStore(Options.Create(
            new VideoEditorOptions { MultiTrack = true, TextOverlays = true }));

        var title = new TextOverlay { Name = "title", Text = "Basement", Duration = 4, FontSize = 48 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, title);
        return (store, title);
    }

    [Fact]
    public void A_title_edit_can_be_undone()
    {
        var (store, title) = Store();

        store.CommitTextOverlayUpdate(title.Id, "size",
            o => o.FontSize = 96, o => o.FontSize = 48);

        Assert.Equal(96, title.FontSize);
        store.Undo();
        Assert.Equal(48, title.FontSize);
    }

    [Fact]
    public void And_redone()
    {
        var (store, title) = Store();

        store.CommitTextOverlayUpdate(title.Id, "size",
            o => o.FontSize = 96, o => o.FontSize = 48);
        store.Undo();
        store.Redo();

        Assert.Equal(96, title.FontSize);
    }

    /// <summary>
    /// One step per edit, so undo walks back through what somebody did rather than collapsing a
    /// panel's worth of changes into a single opaque step.
    /// </summary>
    [Fact]
    public void Each_edit_is_its_own_step()
    {
        var (store, title) = Store();

        store.CommitTextOverlayUpdate(title.Id, "size", o => o.FontSize = 96, o => o.FontSize = 48);
        store.CommitTextOverlayUpdate(title.Id, "colour",
            o => o.FontColor = "#FF0000", o => o.FontColor = "#FFFFFF");

        store.Undo();
        Assert.Equal("#FFFFFF", title.FontColor);
        Assert.Equal(96, title.FontSize);

        store.Undo();
        Assert.Equal(48, title.FontSize);
    }

    /// <summary>A locked track refuses the edit rather than applying it and not recording it.</summary>
    [Fact]
    public void A_title_on_a_locked_track_is_not_edited()
    {
        var (store, title) = Store();
        store.PrimaryVideoTrack.IsLocked = true;

        store.CommitTextOverlayUpdate(title.Id, "size", o => o.FontSize = 96, o => o.FontSize = 48);

        Assert.Equal(48, title.FontSize);
    }
}
