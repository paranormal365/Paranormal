using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Tests.Services;

public sealed class CalloutClipTests
{
    private static ClipStore CreateStore()
        => new(Options.Create(new VideoEditorOptions()));

    // ── Model defaults ────────────────────────────────────────────────────────

    [Fact]
    public void CalloutClip_DefaultShapeIsRectangle()
    {
        var clip = new CalloutClip { Name = "box" };
        Assert.Equal(ShapeType.Rectangle, clip.Shape);
    }

    [Fact]
    public void CalloutClip_DefaultOpacityIsOne()
    {
        var clip = new CalloutClip { Name = "box" };
        Assert.Equal(1.0, clip.Opacity);
    }

    [Fact]
    public void CalloutClip_FillColorPackedCorrectly()
    {
        var clip = new CalloutClip { Name = "box" };
        var (r, g, b, a) = ColorHelper.Unpack(clip.FillColor);
        Assert.Equal(255, r); // yellow
        Assert.Equal(255, g);
        Assert.Equal(0, b);
    }

    // ── ClipStore integration ─────────────────────────────────────────────────

    [Fact]
    public void AddCallout_AddsToPrimaryVideoTrack()
    {
        var store = CreateStore();
        var clip  = new CalloutClip { Name = "arrow", Duration = 3.0 };

        store.AddCallout(clip);

        Assert.Single(store.AllCalloutClips);
    }

    [Fact]
    public void AddCallout_RaisesOnChange()
    {
        var store = CreateStore();
        var fired = false;
        store.OnChange += () => fired = true;

        store.AddCallout(new CalloutClip { Name = "star" });

        Assert.True(fired);
    }

    [Fact]
    public void RemoveCallout_RemovesClip()
    {
        var store = CreateStore();
        var clip  = new CalloutClip { Name = "box", Duration = 2.0 };
        store.AddCallout(clip);

        store.RemoveCallout(clip.Id);

        Assert.Empty(store.AllCalloutClips);
    }

    [Fact]
    public void RemoveCallout_SupportsUndo()
    {
        var store = CreateStore();
        var clip  = new CalloutClip { Name = "box", Duration = 2.0 };
        store.AddCallout(clip);
        store.RemoveCallout(clip.Id);

        store.Undo();

        Assert.Single(store.AllCalloutClips);
    }

    [Fact]
    public void UpdateCallout_MutatesInPlace()
    {
        var store = CreateStore();
        var clip  = new CalloutClip { Name = "box", X = 0.1 };
        store.AddCallout(clip);

        store.UpdateCallout(clip.Id, c => c.X = 0.5);

        Assert.Equal(0.5, store.AllCalloutClips.First().X);
    }

    [Fact]
    public void UpdateCallout_NoOpOnLockedTrack()
    {
        var store = CreateStore();
        var clip  = new CalloutClip { Name = "box", X = 0.1 };
        store.AddCallout(clip);
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.UpdateCallout(clip.Id, c => c.X = 0.9);

        Assert.Equal(0.1, store.AllCalloutClips.First().X);
    }

    [Fact]
    public void AddCallout_LockedTrack_IsNoOp()
    {
        var store = CreateStore();
        store.LockTrack(store.PrimaryVideoTrack.Id, true);

        store.AddCallout(new CalloutClip { Name = "box" });

        Assert.Empty(store.AllCalloutClips);
    }

    // ── AllCalloutClips computed property ─────────────────────────────────────

    [Fact]
    public void AllCalloutClips_EmptyInitially()
    {
        var store = CreateStore();
        Assert.Empty(store.AllCalloutClips);
    }

    [Fact]
    public void AllCalloutClips_CountsAcrossAdditions()
    {
        var store = CreateStore();
        store.AddCallout(new CalloutClip { Name = "a" });
        store.AddCallout(new CalloutClip { Name = "b" });

        Assert.Equal(2, store.AllCalloutClips.Count());
    }

    // ── ColorHelper used in callout defaults ──────────────────────────────────

    [Fact]
    public void CalloutClip_ShadowColorHasAlpha()
    {
        var clip = new CalloutClip { Name = "test" };
        var (_, _, _, a) = ColorHelper.Unpack(clip.ShadowColor);
        Assert.True(a < 255, "Shadow should be semi-transparent");
    }
}
