using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Recording which parts of a clip must not be shown.
/// </summary>
/// <remarks>
/// Undo matters more here than elsewhere: silently losing a hidden area leaves somebody exporting
/// a face they believe they covered.
/// </remarks>
public sealed class ClipRedactionTests
{
    private static (ClipStore Store, VideoClip Clip) Store()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true }));
        var clip  = new VideoClip { Name = "porch", Duration = 10 };
        store.AddClip(clip);
        return (store, clip);
    }

    private static RedactionRegion Region(double x = 0.1) =>
        new() { X = x, Y = 0.2, Width = 0.3, Height = 0.4 };

    [Fact]
    public void An_area_can_be_hidden()
    {
        var (store, clip) = Store();

        store.CommitClipRedactions(clip.Id, [Region()], []);

        Assert.Single(clip.Redactions);
    }

    [Fact]
    public void And_the_hiding_undone()
    {
        var (store, clip) = Store();

        store.CommitClipRedactions(clip.Id, [Region()], []);
        store.Undo();

        Assert.Empty(clip.Redactions);
    }

    [Fact]
    public void Removing_an_area_can_be_undone_too()
    {
        var (store, clip) = Store();
        var region = Region();

        store.CommitClipRedactions(clip.Id, [region], []);
        store.CommitClipRedactions(clip.Id, [], [region]);
        Assert.Empty(clip.Redactions);

        store.Undo();
        Assert.Single(clip.Redactions);
    }

    /// <summary>
    /// The command holds values, not the objects the panel is editing. Otherwise moving an area
    /// after adding it would quietly rewrite what undo puts back.
    /// </summary>
    [Fact]
    public void Undo_restores_where_the_area_was_not_where_it_ended_up()
    {
        var (store, clip) = Store();
        var first = Region(x: 0.1);

        store.CommitClipRedactions(clip.Id, [first], []);
        store.CommitClipRedactions(clip.Id, [Region(x: 0.8)], [first]);

        store.Undo();

        Assert.Equal(0.1, Assert.Single(clip.Redactions).X, precision: 6);
    }

    [Fact]
    public void A_clip_on_a_locked_track_keeps_what_it_had()
    {
        var (store, clip) = Store();
        store.PrimaryVideoTrack.IsLocked = true;

        store.CommitClipRedactions(clip.Id, [Region()], []);

        Assert.Empty(clip.Redactions);
    }

    [Fact]
    public void An_image_can_have_areas_hidden_as_well()
    {
        var store = new ClipStore(Options.Create(new VideoEditorOptions { MultiTrack = true }));
        var image = new ImageClip { Name = "photo", Duration = 5 };
        store.AddImageClip(image);

        store.CommitClipRedactions(image.Id, [Region()], []);

        Assert.Single(image.Redactions);
    }
}
