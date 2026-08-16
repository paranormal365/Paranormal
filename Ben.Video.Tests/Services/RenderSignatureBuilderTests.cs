using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class RenderSignatureBuilderTests
{
    private static VideoClip BaseVideoClip() => new()
    {
        MemFsName = "source.mp4",
        Duration  = 10.0,
        StartTrim = 0.0,
        EndTrim   = 10.0,
        Speed     = 1.0,
    };

    // ── Stability ────────────────────────────────────────────────────────────

    [Fact]
    public void ForVideoClip_IdenticalInputs_ProduceIdenticalSignatures()
    {
        var a = BaseVideoClip();
        var b = BaseVideoClip();

        Assert.Equal(
            RenderSignatureBuilder.ForVideoClip(a, 640, 360),
            RenderSignatureBuilder.ForVideoClip(b, 640, 360));
    }

    [Fact]
    public void ForVideoClip_TimelinePositionAndOrder_DoNotAffectSignature()
    {
        // Deliberately excluded — repositioning/reordering a clip must not invalidate its
        // cached render.
        var a = BaseVideoClip() with { TimelinePosition = 0.0, Order = 0 };
        var b = BaseVideoClip() with { TimelinePosition = 42.0, Order = 3 };

        Assert.Equal(
            RenderSignatureBuilder.ForVideoClip(a, 640, 360),
            RenderSignatureBuilder.ForVideoClip(b, 640, 360));
    }

    [Fact]
    public void ForVideoClip_DifferentId_DoesNotAffectSignature()
    {
        // Id is a stable per-clip identifier, not content — two clips with identical content
        // fields but different Ids should hash the same (content-addressed, not identity-addressed).
        var a = BaseVideoClip();
        var b = BaseVideoClip() with { Id = Guid.NewGuid() };

        Assert.Equal(
            RenderSignatureBuilder.ForVideoClip(a, 640, 360),
            RenderSignatureBuilder.ForVideoClip(b, 640, 360));
    }

    // ── Sensitivity — each field that affects rendered bytes must flip the signature ────

    [Theory]
    [MemberData(nameof(VideoClipMutations))]
    public void ForVideoClip_ContentChange_ChangesSignature(Func<VideoClip, VideoClip> mutate)
    {
        var original = BaseVideoClip();
        var mutated  = mutate(BaseVideoClip());

        Assert.NotEqual(
            RenderSignatureBuilder.ForVideoClip(original, 640, 360),
            RenderSignatureBuilder.ForVideoClip(mutated, 640, 360));
    }

    public static IEnumerable<object[]> VideoClipMutations()
    {
        yield return [(Func<VideoClip, VideoClip>)(c => c with { MemFsName = "other.mp4" })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with { StartTrim = 1.0 })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with { EndTrim = 8.0 })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with { Speed = 2.0 })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with { MuteAudio = true })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with { Volume = 0.5 })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with { Effects = new ClipEffects { Brightness = 0.3 } })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with
        {
            AppliedEffects = [new AppliedEffect { EffectId = "grayscale" }],
        })];
        yield return [(Func<VideoClip, VideoClip>)(c => c with
        {
            VolumeAutomation = [new VolumeKeyframe { Position = 0.5, Volume = 0.2 }],
        })];
    }

    [Fact]
    public void ForVideoClip_DifferentPreviewResolution_ChangesSignature()
    {
        var clip = BaseVideoClip();

        Assert.NotEqual(
            RenderSignatureBuilder.ForVideoClip(clip, 640, 360),
            RenderSignatureBuilder.ForVideoClip(clip, 1280, 720));
    }

    [Fact]
    public void ForVideoClip_AppliedEffectOrder_ChangesSignature()
    {
        // Filter order matters for ffmpeg output — must not be normalized away.
        var a = BaseVideoClip() with
        {
            AppliedEffects =
            [
                new AppliedEffect { EffectId = "grayscale" },
                new AppliedEffect { EffectId = "sepia" },
            ],
        };
        var b = BaseVideoClip() with
        {
            AppliedEffects =
            [
                new AppliedEffect { EffectId = "sepia" },
                new AppliedEffect { EffectId = "grayscale" },
            ],
        };

        Assert.NotEqual(
            RenderSignatureBuilder.ForVideoClip(a, 640, 360),
            RenderSignatureBuilder.ForVideoClip(b, 640, 360));
    }

    [Fact]
    public void ForVideoClip_AppliedEffectParameterOrder_DoesNotAffectSignature()
    {
        // Parameter dictionary iteration order is not semantically meaningful — should be
        // normalized (sorted) so equal parameter sets hash equal regardless of insertion order.
        var a = BaseVideoClip() with
        {
            AppliedEffects =
            [
                new AppliedEffect
                {
                    EffectId = "grayscale",
                    Parameters = new Dictionary<string, double> { ["b"] = 2, ["a"] = 1 },
                },
            ],
        };
        var b = BaseVideoClip() with
        {
            AppliedEffects =
            [
                new AppliedEffect
                {
                    EffectId = "grayscale",
                    Parameters = new Dictionary<string, double> { ["a"] = 1, ["b"] = 2 },
                },
            ],
        };

        Assert.Equal(
            RenderSignatureBuilder.ForVideoClip(a, 640, 360),
            RenderSignatureBuilder.ForVideoClip(b, 640, 360));
    }

    // ── ImageClip ────────────────────────────────────────────────────────────

    [Fact]
    public void ForImageClip_IdenticalInputs_ProduceIdenticalSignatures()
    {
        ImageClip Make() => new() { MemFsName = "img.jpg", Duration = 5.0, Width = 800, Height = 600 };

        Assert.Equal(
            RenderSignatureBuilder.ForImageClip(Make(), 640, 360),
            RenderSignatureBuilder.ForImageClip(Make(), 640, 360));
    }

    [Fact]
    public void ForImageClip_DifferentDuration_ChangesSignature()
    {
        var a = new ImageClip { MemFsName = "img.jpg", Duration = 5.0 };
        var b = new ImageClip { MemFsName = "img.jpg", Duration = 6.0 };

        Assert.NotEqual(
            RenderSignatureBuilder.ForImageClip(a, 640, 360),
            RenderSignatureBuilder.ForImageClip(b, 640, 360));
    }

    [Fact]
    public void VideoAndImageClip_SamePreviewResolution_NeverCollide()
    {
        // "video|" and "image|" prefixes must keep the two signature spaces disjoint even if
        // every other field happened to coincide.
        var video = new VideoClip { MemFsName = "x", Duration = 5.0, EndTrim = 5.0 };
        var image = new ImageClip { MemFsName = "x", Duration = 5.0 };

        Assert.NotEqual(
            RenderSignatureBuilder.ForVideoClip(video, 640, 360),
            RenderSignatureBuilder.ForImageClip(image, 640, 360));
    }
}
