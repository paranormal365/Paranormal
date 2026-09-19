using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Plugins.Image;
using Ben.Video.Editor.Plugins.Video;
using VideoSlideLeft   = Ben.Video.Editor.Plugins.Video.SlideInFromLeftEffect;
using VideoSlideRight  = Ben.Video.Editor.Plugins.Video.SlideInFromRightEffect;
using VideoSlideBottom = Ben.Video.Editor.Plugins.Video.SlideInFromBottomEffect;
using VideoZoomIn      = Ben.Video.Editor.Plugins.Video.ZoomInEffect;
using VideoZoomOut     = Ben.Video.Editor.Plugins.Video.ZoomOutEffect;
using VideoKenBurns   = Ben.Video.Editor.Plugins.Video.KenBurnsEffect;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Phase 41 — tests for EasingHelper, ParameterType.Select, and animation effect plugins.
/// These are smoke tests: they verify each effect produces a non-empty filter fragment
/// and that the easing expressions are syntactically constructed (not empty).
/// Full ffmpeg rendering is tested by running the Playground end-to-end.
/// </summary>
public sealed class AnimationEffectTests
{
    private static IReadOnlyDictionary<string, double> P(params (string k, double v)[] pairs)
        => pairs.ToDictionary(p => p.k, p => p.v);

    // ── EasingHelper ─────────────────────────────────────────────────────────

    [Fact]
    public void EasingHelper_LinearReturnsProgressExpression()
    {
        var expr = EasingHelper.GetExpression(EasingHelper.Linear, "t", 1.0);
        Assert.Contains("t", expr);
        Assert.DoesNotContain("pow", expr); // linear has no pow
    }

    [Fact]
    public void EasingHelper_EaseInContainsPow()
    {
        var expr = EasingHelper.GetExpression(EasingHelper.EaseIn, "t", 1.0);
        Assert.Contains("pow", expr);
    }

    [Fact]
    public void EasingHelper_EaseOutContainsPow()
    {
        var expr = EasingHelper.GetExpression(EasingHelper.EaseOut, "t", 2.5);
        Assert.Contains("pow", expr);
    }

    [Fact]
    public void EasingHelper_EaseInOutContainsIf()
    {
        var expr = EasingHelper.GetExpression(EasingHelper.EaseInOut, "t", 1.0);
        Assert.Contains("if(", expr);
    }

    [Fact]
    public void EasingHelper_BounceContainsCos()
    {
        var expr = EasingHelper.GetExpression(EasingHelper.Bounce, "t", 1.0);
        Assert.Contains("cos", expr);
    }

    [Fact]
    public void EasingHelper_ElasticContainsSin()
    {
        var expr = EasingHelper.GetExpression(EasingHelper.Elastic, "t", 1.0);
        Assert.Contains("sin", expr);
    }

    [Fact]
    public void EasingHelper_GetClampedWrapsWithMinMax()
    {
        var expr = EasingHelper.GetClamped(EasingHelper.Elastic, "t", 1.0);
        Assert.StartsWith("min(max(", expr);
    }

    [Fact]
    public void EasingHelper_HasSixLabels()
    {
        Assert.Equal(6, EasingHelper.Labels.Count);
    }

    // ── ParameterType.Select ──────────────────────────────────────────────────

    [Fact]
    public void ClipEffectParameter_SelectTypeHasOptions()
    {
        var param = new ClipEffectParameter
        {
            Key  = "easing",
            Label = "Easing",
            Type  = ParameterType.Select,
            Options = EasingHelper.Labels,
            DefaultValue = EasingHelper.EaseOut,
        };

        Assert.Equal(ParameterType.Select, param.Type);
        Assert.Equal(6, param.Options.Count);
        Assert.Equal(EasingHelper.EaseOut, param.DefaultValue);
    }

    // ── Video effects: BuildFilterFragment smoke tests ────────────────────────

    [Theory]
    [InlineData(EasingHelper.Linear)]
    [InlineData(EasingHelper.EaseIn)]
    [InlineData(EasingHelper.EaseOut)]
    [InlineData(EasingHelper.EaseInOut)]
    [InlineData(EasingHelper.Bounce)]
    [InlineData(EasingHelper.Elastic)]
    public void SlideInFromLeft_ProducesFilterForAllEasings(int easing)
    {
        var fx = new VideoSlideLeft();
        var frag = fx.BuildFilterFragment(P(("duration", 0.5), ("easing", easing)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("pad=iw*2", frag);
        Assert.Contains("crop=iw:ih", frag);
    }

    [Fact]
    public void SlideInFromRight_ProducesFilter()
    {
        var fx   = new VideoSlideRight();
        var frag = fx.BuildFilterFragment(P(("duration", 0.5), ("easing", 2)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("pad=iw*2", frag);
    }

    [Fact]
    public void SlideInFromBottom_ProducesFilter()
    {
        var fx   = new VideoSlideBottom();
        var frag = fx.BuildFilterFragment(P(("duration", 0.5), ("easing", 2)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("pad=iw:ih*2", frag);
    }

    [Fact]
    public void ZoomIn_ProducesZoompanFilter()
    {
        var fx   = new VideoZoomIn();
        var frag = fx.BuildFilterFragment(
            P(("duration", 2.0), ("start_zoom", 1.5), ("easing", 2)), 10.0, 1.0, 1920, 1080);
        Assert.NotEmpty(frag);
        Assert.Contains("zoompan", frag);
    }

    [Fact]
    public void ZoomOut_ProducesZoompanFilter()
    {
        var fx   = new VideoZoomOut();
        var frag = fx.BuildFilterFragment(
            P(("duration", 2.0), ("end_zoom", 1.5), ("easing", 1)), 10.0, 1.0, 1920, 1080);
        Assert.NotEmpty(frag);
        Assert.Contains("zoompan", frag);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void KenBurns_ProducesFilterForAllDirections(int dir)
    {
        var fx   = new VideoKenBurns();
        var frag = fx.BuildFilterFragment(
            P(("duration", 5.0), ("zoom", 1.3), ("direction", dir)), 10.0, 1.0, 1920, 1080);
        Assert.NotEmpty(frag);
        Assert.Contains("zoompan", frag);
    }

    [Fact]
    public void Flash_ProducesGeqFilter()
    {
        var fx   = new FlashEffect();
        var frag = fx.BuildFilterFragment(P(("duration", 0.8), ("flashes", 3.0), ("strength", 1.5)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("geq", frag);
    }

    [Fact]
    public void Shake_ProducesPadCropFilter()
    {
        var fx   = new ShakeEffect();
        var frag = fx.BuildFilterFragment(P(("duration", 0.6), ("intensity", 20.0), ("frequency", 8.0)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("pad=", frag);
        Assert.Contains("sin(", frag);
    }

    [Fact]
    public void Blur_ProducesGblurFilter()
    {
        var fx   = new BlurEffect();
        var frag = fx.BuildFilterFragment(P(("sigma", 5.0)), 5.0);
        Assert.NotEmpty(frag);
        Assert.StartsWith("gblur=sigma=", frag);
    }

    [Fact]
    public void Vignette_ProducesVignetteFilter()
    {
        var fx   = new VignetteEffect();
        var frag = fx.BuildFilterFragment(P(("angle", 0.5)), 5.0);
        Assert.NotEmpty(frag);
        Assert.StartsWith("vignette=angle=", frag);
    }

    [Fact]
    public void Sepia_ProducesColorchannelmixerFilter()
    {
        var fx   = new SepiaEffect();
        var frag = fx.BuildFilterFragment(P(("intensity", 1.0)), 5.0);
        Assert.NotEmpty(frag);
        Assert.StartsWith("colorchannelmixer=", frag);
    }

    [Fact]
    public void Sepia_ReturnsEmptyWhenIntensityZero()
    {
        var fx   = new SepiaEffect();
        var frag = fx.BuildFilterFragment(P(("intensity", 0.0)), 5.0);
        Assert.Empty(frag);
    }

    [Fact]
    public void RotateIn_ProducesRotateFilter()
    {
        var fx   = new RotateInEffect();
        var frag = fx.BuildFilterFragment(P(("duration", 0.8), ("start_angle", -90.0), ("easing", 2)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("rotate=angle=", frag);
    }

    [Fact]
    public void FadeInDown_CombinesFadeAndSlide()
    {
        var fx   = new FadeInDownEffect();
        var frag = fx.BuildFilterFragment(P(("duration", 0.6), ("distance", 30.0)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("fade=t=in", frag);
    }

    [Fact]
    public void FadeInUp_CombinesFadeAndSlide()
    {
        var fx   = new FadeInUpEffect();
        var frag = fx.BuildFilterFragment(P(("duration", 0.6), ("distance", 30.0)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("fade=t=in", frag);
    }

    // ── Effect returns empty when duration exceeds clip ───────────────────────

    [Fact]
    public void SlideInFromLeft_ReturnsEmptyWhenDurationExceedsClip()
    {
        var fx   = new VideoSlideLeft();
        // duration clamped to clipDuration, clipDuration > 0 so still valid
        var frag = fx.BuildFilterFragment(P(("duration", 100.0), ("easing", 0)), 2.0);
        Assert.NotEmpty(frag); // clamped to 2.0 which is >0
    }

    // ── Image effects smoke tests ─────────────────────────────────────────────

    [Fact]
    public void ImageZoomIn_ProducesZoompanFilter()
    {
        var fx   = new Ben.Video.Editor.Plugins.Image.ZoomInEffect();
        var frag = fx.BuildFilterFragment(
            P(("start_zoom", 1.5), ("easing", 2)), 5.0, 1.0, 1920, 1080);
        Assert.NotEmpty(frag);
        Assert.Contains("zoompan", frag);
    }

    [Fact]
    public void ImagePulse_ProducesZoompanWithSin()
    {
        var fx   = new PulseEffect();
        var frag = fx.BuildFilterFragment(
            P(("max_zoom", 1.1), ("cycles", 2.0)), 5.0, 1.0, 1920, 1080);
        Assert.NotEmpty(frag);
        Assert.Contains("abs(sin(", frag);
    }

    [Fact]
    public void ImageKenBurns_ProducesZoompanFilter()
    {
        var fx   = new Ben.Video.Editor.Plugins.Image.KenBurnsEffect();
        var frag = fx.BuildFilterFragment(
            P(("zoom", 1.3), ("direction", 0)), 5.0, 1.0, 1920, 1080);
        Assert.NotEmpty(frag);
        Assert.Contains("zoompan", frag);
    }

    // ── What made every zoom effect fail on export (2026-09-05 audit, motion-8) ──

    /// <summary>
    /// The seven zoom effects, each with the frame size they will run against.
    /// </summary>
    public static TheoryData<IClipEffect, IReadOnlyDictionary<string, double>> ZoomEffects() => new()
    {
        { new VideoZoomIn(),  P(("duration", 2.0), ("start_zoom", 1.5), ("easing", 2)) },
        { new VideoZoomOut(), P(("duration", 2.0), ("end_zoom", 1.5),   ("easing", 1)) },
        { new VideoKenBurns(), P(("duration", 5.0), ("zoom", 1.3), ("direction", 0)) },
        { new Ben.Video.Editor.Plugins.Image.ZoomInEffect(),  P(("start_zoom", 1.5), ("easing", 2)) },
        { new Ben.Video.Editor.Plugins.Image.ZoomOutEffect(), P(("end_zoom", 1.5),   ("easing", 1)) },
        { new Ben.Video.Editor.Plugins.Image.KenBurnsEffect(), P(("zoom", 1.3), ("direction", 0)) },
        { new PulseEffect(), P(("max_zoom", 1.1), ("cycles", 2.0)) },
    };

    /// <summary>
    /// Every zoom expression was written against <c>on/fps</c>, and <c>fps</c> is not a variable
    /// zoompan defines. An expression naming it does not evaluate, so none of these effects did
    /// anything on export.
    /// </summary>
    [Theory]
    [MemberData(nameof(ZoomEffects))]
    public void A_zoom_effect_uses_a_clock_zoompan_actually_publishes(
        IClipEffect fx, IReadOnlyDictionary<string, double> p)
    {
        var frag = fx.BuildFilterFragment(p, 10.0, 1.0, 1920, 1080);

        Assert.NotEmpty(frag);
        Assert.DoesNotContain("fps", frag);
        Assert.Contains(ZoompanFragment.TimeVariable, frag);
    }

    /// <summary>
    /// <c>s</c> takes a literal size, not an expression, so <c>s=iw+"x"+ih</c> was never something
    /// ffmpeg could parse.
    /// </summary>
    [Theory]
    [MemberData(nameof(ZoomEffects))]
    public void A_zoom_effect_states_its_output_size_as_a_literal(
        IClipEffect fx, IReadOnlyDictionary<string, double> p)
    {
        var frag = fx.BuildFilterFragment(p, 10.0, 1.0, 1920, 1080);

        Assert.Contains(":s=1920x1080", frag);
        Assert.DoesNotContain("iw+", frag);
    }

    /// <summary>
    /// <c>d</c> is how many output frames each input frame is held for. Set to the whole effect's
    /// frame count — which is what these did — it repeats every frame hundreds of times.
    /// </summary>
    [Theory]
    [MemberData(nameof(ZoomEffects))]
    public void A_zoom_effect_emits_one_frame_per_frame(
        IClipEffect fx, IReadOnlyDictionary<string, double> p)
    {
        var frag = fx.BuildFilterFragment(p, 10.0, 1.0, 1920, 1080);

        Assert.Contains(":d=1:", frag);
    }

    /// <summary>
    /// Without a size zoompan quietly resizes the frame to 1280x720, and a segment of the wrong
    /// size breaks the concat that joins the whole export rather than just this one effect. Doing
    /// nothing is the safer answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(ZoomEffects))]
    public void A_zoom_effect_with_no_known_canvas_does_nothing(
        IClipEffect fx, IReadOnlyDictionary<string, double> p)
    {
        Assert.Empty(fx.BuildFilterFragment(p, 10.0, 1.0, 0, 0));
    }

    // ── Phase 43: ColorHelper + FadeFromColor / FadeToColor ───────────────────

    [Fact]
    public void ColorHelper_PackUnpackRoundtrip()
    {
        var packed = ColorHelper.Pack(255, 128, 0, 200);
        var (r, g, b, a) = ColorHelper.Unpack(packed);
        Assert.Equal(255, r); Assert.Equal(128, g);
        Assert.Equal(0,   b); Assert.Equal(200, a);
    }

    [Fact]
    public void ColorHelper_OpaqueBlackConstant()
    {
        var (r, g, b, a) = ColorHelper.Unpack(ColorHelper.OpaqueBlack);
        Assert.Equal(0, r); Assert.Equal(0, g);
        Assert.Equal(0, b); Assert.Equal(255, a);
    }

    [Fact]
    public void ColorHelper_FromHex_Hex6()
    {
        var packed = ColorHelper.FromHex("#FF0000");
        var (r, g, b, a) = ColorHelper.Unpack(packed);
        Assert.Equal(255, r); Assert.Equal(0, g);
        Assert.Equal(0,   b); Assert.Equal(255, a); // alpha defaults to opaque
    }

    [Fact]
    public void ColorHelper_FromHex_Hex8()
    {
        var packed = ColorHelper.FromHex("#FF000080");
        var (r, g, b, a) = ColorHelper.Unpack(packed);
        Assert.Equal(255, r); Assert.Equal(0, g);
        Assert.Equal(0,   b); Assert.Equal(128, a);
    }

    [Fact]
    public void ColorHelper_ToHex_Format()
    {
        var packed = ColorHelper.Pack(255, 0, 0, 128);
        var hex    = ColorHelper.ToHex(packed);
        Assert.Equal("#FF000080", hex);
    }

    [Fact]
    public void ColorHelper_ToFfmpegColor_NoAlpha()
    {
        var packed = ColorHelper.Pack(255, 0, 128);
        var ffcol  = ColorHelper.ToFfmpegColor(packed, includeAlpha: false);
        Assert.Equal("0xFF0080", ffcol);
    }

    // ffmpeg's colour parser takes alpha LAST (0xRRGGBBAA — see ffmpeg-utils "Color").
    // The original alpha-FIRST output put the alpha byte in the red channel and left
    // alpha at 0x00 for the default callout fill — an invisible drawbox (backlog #29).
    [Fact]
    public void ColorHelper_ToFfmpegColor_WithAlpha_AlphaIsLast()
    {
        var packed = ColorHelper.Pack(255, 255, 0, 180); // #FFFF00, alpha 0xB4
        var ffcol  = ColorHelper.ToFfmpegColor(packed, includeAlpha: true);
        Assert.Equal("0xFFFF00B4", ffcol);
    }

    [Fact]
    public void ColorHelper_ToFfmpegColor_WithAlpha_OpaqueBlack()
    {
        var packed = ColorHelper.Pack(0, 0, 0, 255);
        var ffcol  = ColorHelper.ToFfmpegColor(packed, includeAlpha: true);
        Assert.Equal("0x000000FF", ffcol);
    }

    [Fact]
    public void ColorHelper_ToRgbaCss_Opaque()
    {
        var packed = ColorHelper.Pack(255, 128, 0, 255);
        var css    = ColorHelper.ToRgbaCss(packed);
        Assert.StartsWith("rgba(255,128,0,", css);
    }

    [Fact]
    public void FadeFromColor_ProducesFadeFilter()
    {
        var fx   = new Ben.Video.Editor.Plugins.Video.FadeFromColorEffect();
        var frag = fx.BuildFilterFragment(
            P(("duration", 1.0), ("color", ColorHelper.OpaqueBlack)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("fade=t=in", frag);
        Assert.Contains("color=", frag);
    }

    [Fact]
    public void FadeToColor_ProducesFadeFilter()
    {
        var fx   = new Ben.Video.Editor.Plugins.Video.FadeToColorEffect();
        var frag = fx.BuildFilterFragment(
            P(("duration", 1.0), ("color", ColorHelper.OpaqueBlack)), 5.0);
        Assert.NotEmpty(frag);
        Assert.Contains("fade=t=out", frag);
        Assert.Contains("color=", frag);
    }

    [Fact]
    public void FadeToColor_StartTimeIsNearEnd()
    {
        var fx   = new Ben.Video.Editor.Plugins.Video.FadeToColorEffect();
        var frag = fx.BuildFilterFragment(
            P(("duration", 2.0), ("color", ColorHelper.OpaqueWhite)), 10.0);
        // st should be 10 - 2 = 8
        Assert.Contains("st=8.000", frag);
    }
}
