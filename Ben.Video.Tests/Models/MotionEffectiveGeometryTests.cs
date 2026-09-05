using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Models;

public sealed class MotionEffectiveGeometryTests
{
    // ── Callout ──────────────────────────────────────────────────────────────

    [Fact]
    public void Effective_Callout_NoPath_ReturnsClipUnchanged()
    {
        var motion = new MotionKeyframeService();
        var clip   = new CalloutClip { X = 0.1, Y = 0.2, Width = 0.3, Height = 0.4 };

        var effective = MotionEffectiveGeometry.Effective(motion, clip, 1.0);

        Assert.Same(clip, effective);
    }

    [Fact]
    public void Effective_Callout_WithPath_UsesInterpolatedPosition()
    {
        var motion = new MotionKeyframeService();
        var clip   = new CalloutClip { Id = Guid.NewGuid(), X = 0.1, Y = 0.1, Width = 0.2, Height = 0.2 };
        motion.UpsertKeyframe(clip.Id, "CalloutClip", new MotionKeyframe { Time = 0.0, X = 0.0, Y = 0.0, Scale = 1.0, Alpha = 1.0 });
        motion.UpsertKeyframe(clip.Id, "CalloutClip", new MotionKeyframe { Time = 2.0, X = 1.0, Y = 1.0, Scale = 1.0, Alpha = 1.0 });

        var effective = MotionEffectiveGeometry.Effective(motion, clip, 1.0); // midpoint

        Assert.Equal(0.5, effective.X, precision: 5);
        Assert.Equal(0.5, effective.Y, precision: 5);
        // Static Width/Height untouched by a Scale=1.0 path
        Assert.Equal(0.2, effective.Width,  precision: 5);
        Assert.Equal(0.2, effective.Height, precision: 5);
    }

    // ── ClipArt ──────────────────────────────────────────────────────────────

    [Fact]
    public void Effective_ClipArt_NoPath_ReturnsClipUnchanged()
    {
        var motion = new MotionKeyframeService();
        var clip   = new ClipArtClip { X = 0.1, Y = 0.2, Width = 0.3 };

        Assert.Same(clip, MotionEffectiveGeometry.Effective(motion, clip, 1.0));
    }

    [Fact]
    public void Effective_ClipArt_WithPath_UsesInterpolatedPosition()
    {
        var motion = new MotionKeyframeService();
        var clip   = new ClipArtClip { Id = Guid.NewGuid(), X = 0.0, Y = 0.0, Width = 0.2 };
        motion.UpsertKeyframe(clip.Id, "ClipArtClip", new MotionKeyframe { Time = 0.0, X = 0.2, Y = 0.2, Scale = 1.0, Alpha = 1.0 });
        motion.UpsertKeyframe(clip.Id, "ClipArtClip", new MotionKeyframe { Time = 1.0, X = 0.8, Y = 0.8, Scale = 1.0, Alpha = 1.0 });

        var effective = MotionEffectiveGeometry.Effective(motion, clip, 0.5);

        Assert.Equal(0.5, effective.X, precision: 5);
        Assert.Equal(0.5, effective.Y, precision: 5);
    }

    /// <summary>
    /// A set height is used as given; the sentinel resolves against the canvas.
    /// </summary>
    /// <remarks>
    /// This used to expect the sentinel to fall back to the width as a fraction, which on a 16:9
    /// frame is a wide rectangle — while the export fell back to the width in pixels, a square. The
    /// same artwork was drawn, selected and rendered at three different shapes (2026-09-05 audit,
    /// callouts-10).
    /// </remarks>
    [Fact]
    public void EffectiveClipArtHeight_UsesTheHeightWhenThereIsOne()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = 0.3 };

        Assert.Equal(0.3, MotionEffectiveGeometry.EffectiveClipArtHeight(clip, 1920, 1080), precision: 5);
    }

    [Fact]
    public void EffectiveClipArtHeight_WithNoHeightAndNoAssetSize_IsSquareOnScreen()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = -1 };

        // 0.2 of 1920 is 384px; square means 384px tall, which is 0.3555… of 1080.
        Assert.Equal(384.0 / 1080.0,
            MotionEffectiveGeometry.EffectiveClipArtHeight(clip, 1920, 1080), precision: 5);
    }

    [Fact]
    public void EffectiveClipArtHeight_WithNoHeight_FollowsTheArtworks_OwnProportions()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = -1, NativeWidth = 400, NativeHeight = 200 };

        // 384px wide artwork that is half as tall as it is wide: 192px, of 1080.
        Assert.Equal(192.0 / 1080.0,
            MotionEffectiveGeometry.EffectiveClipArtHeight(clip, 1920, 1080), precision: 5);
    }

    // ── TextOverlay ──────────────────────────────────────────────────────────

    [Fact]
    public void Effective_TextOverlay_NoPath_ReturnsOverlayUnchanged()
    {
        var motion  = new MotionKeyframeService();
        var overlay = new TextOverlay { OverrideX = 0.4 };

        Assert.Same(overlay, MotionEffectiveGeometry.Effective(motion, overlay, 1.0));
    }

    [Fact]
    public void Effective_TextOverlay_WithPath_OverridesXY()
    {
        var motion  = new MotionKeyframeService();
        var overlay = new TextOverlay { Id = Guid.NewGuid() };
        motion.UpsertKeyframe(overlay.Id, "TextOverlay", new MotionKeyframe { Time = 0.0, X = 0.0, Y = 0.0, Scale = 1.0, Alpha = 1.0 });
        motion.UpsertKeyframe(overlay.Id, "TextOverlay", new MotionKeyframe { Time = 1.0, X = 1.0, Y = 0.0, Scale = 1.0, Alpha = 1.0 });

        var effective = MotionEffectiveGeometry.Effective(motion, overlay, 0.5);

        Assert.Equal(0.5, effective.OverrideX!.Value, precision: 5);
    }

    [Fact]
    public void TextAnchorX_OverrideSet_UsesOverrideRegardlessOfAlignment()
    {
        var overlay = new TextOverlay { OverrideX = 0.75, HorizontalAlign = TextHorizontalAlign.Left, OffsetX = 999 };
        Assert.Equal(0.75, MotionEffectiveGeometry.TextAnchorX(overlay, 1920), precision: 5);
    }

    [Theory]
    [InlineData(TextHorizontalAlign.Left,   100, 1000, 0.1)]
    [InlineData(TextHorizontalAlign.Right,  100, 1000, 0.9)]
    [InlineData(TextHorizontalAlign.Center, 100, 1000, 0.5)]
    public void TextAnchorX_NoOverride_ApproximatesFromAlignment(TextHorizontalAlign align, int offsetX, int canvasWidth, double expected)
    {
        var overlay = new TextOverlay { HorizontalAlign = align, OffsetX = offsetX };
        Assert.Equal(expected, MotionEffectiveGeometry.TextAnchorX(overlay, canvasWidth), precision: 5);
    }

    [Theory]
    [InlineData(TextVerticalAlign.Top,    100, 1000, 0.1)]
    [InlineData(TextVerticalAlign.Bottom, 100, 1000, 0.9)]
    [InlineData(TextVerticalAlign.Middle, 100, 1000, 0.5)]
    public void TextAnchorY_NoOverride_ApproximatesFromAlignment(TextVerticalAlign align, int offsetY, int canvasHeight, double expected)
    {
        var overlay = new TextOverlay { VerticalAlign = align, OffsetY = offsetY };
        Assert.Equal(expected, MotionEffectiveGeometry.TextAnchorY(overlay, canvasHeight), precision: 5);
    }

    [Fact]
    public void TextAnchorX_ZeroCanvasWidth_DoesNotThrow()
    {
        var overlay = new TextOverlay { HorizontalAlign = TextHorizontalAlign.Left, OffsetX = 10 };
        Assert.Equal(0.0, MotionEffectiveGeometry.TextAnchorX(overlay, 0), precision: 5);
    }

    // ── StaticSeed (item #57, phase P2 — first-keyframe seeding) ────────────────

    [Fact]
    public void StaticSeed_Callout_CopiesPositionAndAppearance_ScaleAlphaAreOne()
    {
        var clip = new CalloutClip
        {
            X = 0.3, Y = 0.4, FillColor = 111, StrokeColor = 222,
            ShadowColor = 333, ShadowOffsetX = 5, ShadowOffsetY = 6, ShadowBlur = 7,
            ControlPointValues = new() { ["cp1"] = 0.9 },
        };

        var kf = MotionEffectiveGeometry.StaticSeed(clip, 2.5);

        Assert.Equal(2.5, kf.Time);
        Assert.Equal(0.3, kf.X);
        Assert.Equal(0.4, kf.Y);
        Assert.Equal(1.0, kf.Scale);
        Assert.Equal(1.0, kf.Alpha);
        Assert.Equal(111, kf.FillColor);
        Assert.Equal(222, kf.StrokeColor);
        Assert.Equal(333, kf.ShadowColor);
        Assert.Equal(5,   kf.ShadowOffsetX);
        Assert.Equal(6,   kf.ShadowOffsetY);
        Assert.Equal(7,   kf.ShadowBlur);
        Assert.Equal(0.9, kf.ControlPointValues["cp1"]);
    }

    [Fact]
    public void StaticSeed_Callout_ControlPointValues_IsACopy_NotSharedReference()
    {
        var clip = new CalloutClip { ControlPointValues = new() { ["cp1"] = 1.0 } };
        var kf   = MotionEffectiveGeometry.StaticSeed(clip, 0.0);

        kf.ControlPointValues["cp1"] = 99.0;

        Assert.Equal(1.0, clip.ControlPointValues["cp1"]);
    }

    [Fact]
    public void StaticSeed_ClipArt_CopiesPositionOnly_ScaleAlphaAreOne()
    {
        var clip = new ClipArtClip { X = 0.6, Y = 0.7 };

        var kf = MotionEffectiveGeometry.StaticSeed(clip, 1.5);

        Assert.Equal(1.5, kf.Time);
        Assert.Equal(0.6, kf.X);
        Assert.Equal(0.7, kf.Y);
        Assert.Equal(1.0, kf.Scale);
        Assert.Equal(1.0, kf.Alpha);
        Assert.Null(kf.ScaleX);
        Assert.Null(kf.ScaleY);
    }

    [Fact]
    public void StaticSeed_ClipArt_SeedsRotationFromCurrentStaticValue()
    {
        var clip = new ClipArtClip { Rotation = 42.0 };

        var kf = MotionEffectiveGeometry.StaticSeed(clip, 0.0);

        Assert.Equal(42.0, kf.Rotation);
    }

    [Fact]
    public void StaticSeed_TextOverlay_UsesAnchorPosition_AndShadowFields()
    {
        var overlay = new TextOverlay
        {
            OverrideX = 0.25, OverrideY = 0.35,
            ShadowColor = 42, ShadowOffsetX = 1, ShadowOffsetY = 2, ShadowBlur = 3,
        };

        var kf = MotionEffectiveGeometry.StaticSeed(overlay, 4.0, 1920, 1080);

        Assert.Equal(4.0, kf.Time);
        Assert.Equal(0.25, kf.X);
        Assert.Equal(0.35, kf.Y);
        Assert.Equal(1.0, kf.Scale);
        Assert.Equal(1.0, kf.Alpha);
        Assert.Equal(42, kf.ShadowColor);
        Assert.Equal(1,  kf.ShadowOffsetX);
        Assert.Equal(2,  kf.ShadowOffsetY);
        Assert.Equal(3,  kf.ShadowBlur);
    }

    [Fact]
    public void StaticSeed_TextOverlay_NoOverride_UsesAlignmentApproximation()
    {
        var overlay = new TextOverlay { HorizontalAlign = TextHorizontalAlign.Left, OffsetX = 96, VerticalAlign = TextVerticalAlign.Top, OffsetY = 108 };

        var kf = MotionEffectiveGeometry.StaticSeed(overlay, 0.0, 960, 1080);

        Assert.Equal(0.1, kf.X, precision: 5);
        Assert.Equal(0.1, kf.Y, precision: 5);
    }
}
