using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A callout, title or piece of artwork can be copied.
/// </summary>
/// <remarks>
/// Only video and audio clips could be duplicated, so making three matching callouts meant
/// building each from scratch and matching every colour, size and font by hand — the thing
/// Camtasia's Ctrl+D exists for (2026-09-05 audit, callouts-15).
/// </remarks>
public sealed class OverlayDuplicateTests
{
    private static ClipStore Store() => new(Options.Create(
        new VideoEditorOptions { MultiTrack = true, TextOverlays = true }));

    private static CalloutClip Callout() => new()
    {
        Name = "here", Shape = ShapeType.Arrow, Duration = 4,
        ControlPointValues = new Dictionary<string, double> { ["startX"] = 0.25 },
    };

    [Fact]
    public void A_callout_can_be_duplicated()
    {
        var store = Store();
        var callout = Callout();
        store.AddCallout(callout);

        store.DuplicateClip(callout.Id);

        Assert.Equal(2, store.AllCalloutClips.Count());
    }

    /// <summary>
    /// The copy is a second callout, not a second reference to the first. A shared control-point
    /// dictionary would make editing either of them edit both.
    /// </summary>
    [Fact]
    public void The_copy_has_its_own_shape_points()
    {
        var store = Store();
        var callout = Callout();
        store.AddCallout(callout);
        store.DuplicateClip(callout.Id);

        var copy = store.AllCalloutClips.Single(c => c.Id != callout.Id);
        copy.ControlPointValues["startX"] = 0.9;

        Assert.Equal(0.25, callout.ControlPointValues["startX"]);
    }

    [Fact]
    public void The_copy_follows_the_original_on_the_timeline()
    {
        var store = Store();
        var callout = Callout();
        callout.TimelinePosition = 2.0;
        store.AddCallout(callout);
        store.DuplicateClip(callout.Id);

        var copy = store.AllCalloutClips.Single(c => c.Id != callout.Id);

        Assert.True(copy.TimelinePosition > callout.TimelinePosition);
    }

    /// <summary>Row order encodes which overlay draws on top, so a copy goes above its original.</summary>
    [Fact]
    public void The_copy_gets_its_own_layer()
    {
        var store = Store();
        var callout = Callout();
        store.AddCallout(callout);
        store.DuplicateClip(callout.Id);

        var copy = store.AllCalloutClips.Single(c => c.Id != callout.Id);

        Assert.True(copy.LayerIndex > callout.LayerIndex);
    }

    [Fact]
    public void A_title_can_be_duplicated_too()
    {
        var store = Store();
        var title = new TextOverlay { Name = "title", Text = "Basement", Duration = 3 };
        store.AddClipToTrack(store.PrimaryVideoTrack.Id, title);

        store.DuplicateClip(title.Id);

        Assert.Equal(2, store.AllTextOverlays.Count());
    }

    [Fact]
    public void And_a_piece_of_artwork()
    {
        var store = Store();
        var art = new ClipArtClip { Name = "arrow", AssetId = Guid.NewGuid().ToString(), Duration = 3 };
        store.AddClipArtClip(art);

        store.DuplicateClip(art.Id);

        Assert.Equal(2, store.AllClipArtClips.Count());
    }

    [Fact]
    public void Duplicating_can_be_undone()
    {
        var store = Store();
        var callout = Callout();
        store.AddCallout(callout);
        store.DuplicateClip(callout.Id);
        store.Undo();

        Assert.Single(store.AllCalloutClips);
    }
}
