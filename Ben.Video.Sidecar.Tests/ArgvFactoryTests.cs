using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Ben.Video.Sidecar.Jobs;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Pure unit tests on <see cref="ArgvFactory"/> — the exact function that turns a validated
/// <see cref="SegmentRenderSpec"/> into the real ffmpeg command line, shared with
/// <c>SegmentJobRunner</c>. Proving these properties by construction here is cheaper and more
/// precise than round-tripping through HTTP + a fake process for every case.
/// </summary>
public sealed class ArgvFactoryTests
{
    private static readonly ClipEffectRegistry Registry = DefaultEffectRegistry.CreateDefault();

    private static SegmentRenderSpec VideoSpec(
        RenderPassKind pass = RenderPassKind.Fine,
        double startTrim = 1.0, double endTrim = 3.0, double speed = 1.0,
        int width = 320, int height = 180,
        IReadOnlyList<AppliedEffectDto>? appliedEffects = null,
        IReadOnlyList<VolumeKeyframeDto>? volumeAutomation = null) => new(
        Kind: SegmentKind.Video,
        ClipId: Guid.NewGuid(),
        SourceExt: ".mp4",
        Pass: pass,
        Duration: 5.0,
        StartTrim: startTrim,
        EndTrim: endTrim,
        Speed: speed,
        MuteAudio: false,
        Gain: 1.0,
        OutputWidth: width,
        OutputHeight: height,
        Effects: null,
        AppliedEffects: appliedEffects ?? [],
        VolumeAutomation: volumeAutomation ?? []);

    [Fact]
    public void Build_Video_StartsWithInputFlag()
    {
        var args = ArgvFactory.Build(VideoSpec(), "/sidecar/sources/abc.mp4", "output.mp4", Registry);
        Assert.Equal("-i", args[0]);
        Assert.Equal("/sidecar/sources/abc.mp4", args[1]);
    }

    [Fact]
    public void Build_Video_EndsWithOutputName()
    {
        var args = ArgvFactory.Build(VideoSpec(), "/sidecar/sources/abc.mp4", "output.mp4", Registry);
        Assert.Equal("output.mp4", args[^1]);
    }

    private static ExportQualityDto ExportQuality(
        ExportVideoCodec videoCodec = ExportVideoCodec.H264,
        ExportPresetKind preset = ExportPresetKind.Slow,
        int crf = 18, int fps = 30) => new(
        VideoCodec: videoCodec, AudioCodec: ExportAudioCodec.Aac,
        Bitrate: 4000, UseCrf: true, Crf: crf,
        IncludeAudio: true, AudioBitrate: 192, Preset: preset, Fps: fps);

    [Fact]
    public void Build_ExportPass_UsesExplicitQualityNotHardcodedPreset()
    {
        var spec = VideoSpec(pass: RenderPassKind.Export) with { ExportQuality = ExportQuality(preset: ExportPresetKind.Slow, crf: 18) };
        var args = ArgvFactory.Build(spec, "in.mp4", "out.mp4", Registry);
        var presetIndex = Array.IndexOf(args, "-preset");
        var crfIndex = Array.IndexOf(args, "-crf");
        Assert.Equal("slow", args[presetIndex + 1]);
        Assert.Equal("18", args[crfIndex + 1]);
    }

    [Theory]
    [InlineData(ExportVideoCodec.H264, "libx264")]
    [InlineData(ExportVideoCodec.H265, "libx265")]
    [InlineData(ExportVideoCodec.Vp9, "libvpx-vp9")]
    public void Build_ExportPass_MapsVideoCodecCorrectly(ExportVideoCodec codec, string expectedFfmpegCodec)
    {
        var spec = VideoSpec(pass: RenderPassKind.Export) with { ExportQuality = ExportQuality(videoCodec: codec) };
        var args = ArgvFactory.Build(spec, "in.mp4", "out.mp4", Registry);
        var codecIndex = Array.IndexOf(args, "-c:v");
        Assert.Equal(expectedFfmpegCodec, args[codecIndex + 1]);
    }

    [Fact]
    public void Build_ExportPass_NoScaleFilterWhenOutputDimensionsAreZero()
    {
        // Matches ExportService.TrimSegmentsAsync's own BuildTrimArgs call for video clips: no
        // outputWidth/outputHeight passed at all, scaling happens later at composite time.
        var spec = VideoSpec(pass: RenderPassKind.Export, width: 0, height: 0) with { ExportQuality = ExportQuality() };
        var args = ArgvFactory.Build(spec, "in.mp4", "out.mp4", Registry);
        Assert.DoesNotContain("-filter:v", args);
    }

    [Fact]
    public void Build_ExportPass_UsesRealTrimArgsNotBackgroundRenderVariant()
    {
        // BuildTrimArgs (export) emits "-an" for a fully-muted clip with no real audio; the
        // background-render variant would instead synthesize an anullsrc silent track. This is
        // the one observable difference between the two builders — asserting it proves ArgvFactory
        // actually routed to BuildTrimArgs for the Export pass, not BuildBackgroundRenderVideoArgs.
        var spec = VideoSpec(pass: RenderPassKind.Export) with
        {
            MuteAudio = true,
            ExportQuality = ExportQuality() with { IncludeAudio = false },
        };
        var args = ArgvFactory.Build(spec, "in.mp4", "out.mp4", Registry);
        Assert.Contains("-an", args);
        Assert.DoesNotContain(args, a => a.Contains("anullsrc"));
    }

    [Fact]
    public void Build_RoughPass_UsesUltrafastPresetAndHighCrf()
    {
        var args = ArgvFactory.Build(VideoSpec(pass: RenderPassKind.Rough), "in.mp4", "out.mp4", Registry);
        var presetIndex = Array.IndexOf(args, "-preset");
        var crfIndex = Array.IndexOf(args, "-crf");
        Assert.Equal("ultrafast", args[presetIndex + 1]);
        Assert.Equal("35", args[crfIndex + 1]);
    }

    [Fact]
    public void Build_FinePass_UsesDefaultPresetAndCrf()
    {
        var args = ArgvFactory.Build(VideoSpec(pass: RenderPassKind.Fine), "in.mp4", "out.mp4", Registry);
        var presetIndex = Array.IndexOf(args, "-preset");
        var crfIndex = Array.IndexOf(args, "-crf");
        Assert.Equal("medium", args[presetIndex + 1]);
        Assert.Equal("23", args[crfIndex + 1]);
    }

    [Fact]
    public void Build_Video_TrimBoundsMatchSpec()
    {
        var args = ArgvFactory.Build(VideoSpec(startTrim: 1.5, endTrim: 4.5), "in.mp4", "out.mp4", Registry);
        var ssIndex = Array.IndexOf(args, "-ss");
        var toIndex = Array.IndexOf(args, "-to");
        Assert.Equal("1.500", args[ssIndex + 1]);
        Assert.Equal("4.500", args[toIndex + 1]);
    }

    [Fact]
    public void Build_MutedVideo_SynthesizesASilentAudioTrack()
    {
        // BuildBackgroundRenderVideoArgs' whole point (item #36 phase C): every background segment
        // must have consistent stream layout so -c copy concat works across a mix of segments —
        // this asserts the native path inherits that contract, not just the wasm one.
        var spec = VideoSpec() with { MuteAudio = true };
        var args = ArgvFactory.Build(spec, "in.mp4", "out.mp4", Registry);
        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000", args);
    }

    [Fact]
    public void Build_ScalesToRequestedOutputDimensions()
    {
        var args = ArgvFactory.Build(VideoSpec(width: 640, height: 360), "in.mp4", "out.mp4", Registry);
        var filterIndex = Array.IndexOf(args, "-filter:v");
        Assert.Contains("scale=640:360:force_original_aspect_ratio=decrease", args[filterIndex + 1]);
    }

    [Fact]
    public void Build_KnownAppliedEffect_ProducesItsFilterFragment()
    {
        var effects = new[] { new AppliedEffectDto("grayscale", new Dictionary<string, double>()) };
        var args = ArgvFactory.Build(VideoSpec(appliedEffects: effects), "in.mp4", "out.mp4", Registry);
        var filterIndex = Array.IndexOf(args, "-filter:v");
        Assert.Contains("hue=s=0", args[filterIndex + 1]);
    }

    [Fact]
    public void Build_VolumeKeyframes_ProduceAnEvalFrameExpression()
    {
        var keyframes = new[] { new VolumeKeyframeDto(0.0, 0.0), new VolumeKeyframeDto(1.0, 1.0) };
        var args = ArgvFactory.Build(VideoSpec(volumeAutomation: keyframes), "in.mp4", "out.mp4", Registry);
        var filterAIndex = Array.IndexOf(args, "-filter:a");
        Assert.Contains("volume=eval=frame", args[filterAIndex + 1]);
    }

    [Fact]
    public void Build_HostileEffectParameterKey_IsIgnoredNotInjected()
    {
        // A key that isn't in the effect's declared ParameterSchema is silently dropped by
        // IClipEffect.BuildFilterFragment (it only reads keys it knows about) — SpecValidator is
        // the actual gate that rejects this before it ever reaches ArgvFactory in the real
        // pipeline, but this proves ArgvFactory itself has no injection surface here either.
        var effects = new[] { new AppliedEffectDto("grayscale", new Dictionary<string, double> { ["'; rm -rf /"] = 1.0 }) };
        var args = ArgvFactory.Build(VideoSpec(appliedEffects: effects), "in.mp4", "out.mp4", Registry);
        Assert.DoesNotContain(args, a => a.Contains("rm -rf"));
    }

    [Fact]
    public void Build_Image_UsesLoopAndFramerateNotTrim()
    {
        var spec = new SegmentRenderSpec(
            Kind: SegmentKind.Image, ClipId: Guid.NewGuid(), SourceExt: ".png", Pass: RenderPassKind.Fine,
            Duration: 5.0, StartTrim: 0, EndTrim: 0, Speed: 1, MuteAudio: false, Gain: 1,
            OutputWidth: 320, OutputHeight: 180, Effects: null, AppliedEffects: [], VolumeAutomation: []);

        var args = ArgvFactory.Build(spec, "in.png", "out.mp4", Registry);

        Assert.Equal("-loop", args[0]);
        Assert.DoesNotContain("-ss", args);
        Assert.DoesNotContain("-to", args);
    }

    [Fact]
    public void Build_Image_FallsBackToFiveSeconds_WhenDurationIsZero()
    {
        var spec = new SegmentRenderSpec(
            Kind: SegmentKind.Image, ClipId: Guid.NewGuid(), SourceExt: ".png", Pass: RenderPassKind.Fine,
            Duration: 0, StartTrim: 0, EndTrim: 0, Speed: 1, MuteAudio: false, Gain: 1,
            OutputWidth: 320, OutputHeight: 180, Effects: null, AppliedEffects: [], VolumeAutomation: []);

        var args = ArgvFactory.Build(spec, "in.png", "out.mp4", Registry);

        var tIndex = Array.IndexOf(args, "-t");
        Assert.Equal("5.000", args[tIndex + 1]);
    }
}
