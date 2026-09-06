using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Recording where a clip's picture sits in the frame.
/// </summary>
public sealed class ClipPlacementTests
{
    private static (ClipStore Store, VideoClip Clip) Store()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true }));
        var clip  = new VideoClip { Name = "second camera", Duration = 10 };
        store.AddClip(clip);
        return (store, clip);
    }

    [Fact]
    public void A_clip_starts_out_filling_the_frame()
        => Assert.Null(Store().Clip.Transform);

    [Fact]
    public void It_can_be_placed_in_a_corner()
    {
        var (store, clip) = Store();

        store.CommitClipTransform(clip.Id,
            new ClipTransform { X = 0.68, Y = 0.66, Width = 0.3, Height = 0.3 }, null);

        Assert.Equal(0.68, clip.Transform!.X, precision: 6);
    }

    [Fact]
    public void And_put_back()
    {
        var (store, clip) = Store();

        store.CommitClipTransform(clip.Id, new ClipTransform { Width = 0.5 }, null);
        store.CommitClipTransform(clip.Id, null, clip.Transform);

        Assert.Null(clip.Transform);
    }

    [Fact]
    public void Placing_can_be_undone()
    {
        var (store, clip) = Store();

        store.CommitClipTransform(clip.Id, new ClipTransform { Width = 0.5 }, null);
        store.Undo();

        Assert.Null(clip.Transform);
    }

    /// <summary>
    /// The command holds values, so redoing after further edits restores what it recorded rather
    /// than whatever the object has become since.
    /// </summary>
    [Fact]
    public void Undo_puts_back_the_placement_that_was_there()
    {
        var (store, clip) = Store();
        var first = new ClipTransform { X = 0.1, Width = 0.5 };

        store.CommitClipTransform(clip.Id, first, null);
        store.CommitClipTransform(clip.Id, new ClipTransform { X = 0.8, Width = 0.5 }, first);
        store.Undo();

        Assert.Equal(0.1, clip.Transform!.X, precision: 6);
    }

    [Fact]
    public void A_clip_on_a_locked_track_is_not_moved()
    {
        var (store, clip) = Store();
        store.PrimaryVideoTrack.IsLocked = true;

        store.CommitClipTransform(clip.Id, new ClipTransform { Width = 0.5 }, null);

        Assert.Null(clip.Transform);
    }

    [Fact]
    public void An_image_can_be_placed_as_well()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true }));
        var image = new ImageClip { Name = "photo", Duration = 5 };
        store.AddImageClip(image);

        store.CommitClipTransform(image.Id, new ClipTransform { Width = 0.4 }, null);

        Assert.NotNull(image.Transform);
    }
}
