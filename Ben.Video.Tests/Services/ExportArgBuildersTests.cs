using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Tests for ExportArgBuilders — the pure-static ffmpeg argument helpers extracted
/// from ExportService. Every decision branch is exercised without any mocking.
/// </summary>
public sealed class ExportArgBuildersTests
{
    // ── BuildTrimArgs ────────────────────────────────────────────────────────

    [Fact]
    public void BuildTrimArgs_CrfMode_ContainsCrfFlag()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s);

        Assert.Contains("-crf", args);
        Assert.Contains("18", args);
        Assert.DoesNotContain("-b:v", args);
    }

    [Fact]
    public void BuildTrimArgs_BitrateMode_ContainsBitrateFlag()
    {
        var s    = new ExportSettings { UseCrf = false, Bitrate = 4000, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s);

        Assert.Contains("-b:v", args);
        Assert.Contains("4000k", args);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void BuildTrimArgs_Preset_AddedForX264()
    {
        var s    = new ExportSettings { UseCrf = true, VideoCodec = "libx264", Preset = "fast" };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 1.0, s);

        Assert.Contains("-preset", args);
        Assert.Contains("fast", args);
    }

    [Fact]
    public void BuildTrimArgs_Preset_NotAddedForVp9()
    {
        var s    = new ExportSettings { UseCrf = true, VideoCodec = "libvpx-vp9", Preset = "fast" };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 1.0, s);

        Assert.DoesNotContain("-preset", args);
    }

    [Fact]
    public void BuildTrimArgs_IncludeAudio_AddsAudioCodec()
    {
        var s    = new ExportSettings { IncludeAudio = true, AudioCodec = "aac", AudioBitrate = 128 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 1.0, s);

        Assert.Contains("-c:a", args);
        Assert.Contains("aac", args);
        Assert.Contains("-b:a", args);
        Assert.Contains("128k", args);
        Assert.DoesNotContain("-an", args);
    }

    [Fact]
    public void BuildTrimArgs_NoAudio_AddsAnFlag()
    {
        var s    = new ExportSettings { IncludeAudio = false };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 1.0, s);

        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void BuildTrimArgs_ContainsTimecodeArgs()
    {
        var s    = new ExportSettings();
        var args = ExportArgBuilders.BuildTrimArgs("src.mp4", "dst.mp4", 1.5, 9.75, 1.0, s);

        Assert.Contains("-ss", args);
        Assert.Contains("1.500", args);
        Assert.Contains("-to", args);
        Assert.Contains("9.750", args);
    }

    [Fact]
    public void BuildTrimArgs_FirstArgIsInput_LastArgIsOutput()
    {
        var s    = new ExportSettings();
        var args = ExportArgBuilders.BuildTrimArgs("input.mp4", "output.mp4", 0, 5, 1.0, s);

        Assert.Equal("-i",        args[0]);
        Assert.Equal("input.mp4", args[1]);
        Assert.Equal("output.mp4", args[^1]);
    }

    // ── QualityArgs ──────────────────────────────────────────────────────────

    [Fact]
    public void QualityArgs_CrfMode_YieldsCrfArgs()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.QualityArgs(s).ToArray();

        Assert.Contains("-crf", args);
        Assert.Contains("23",   args);
    }

    [Fact]
    public void QualityArgs_BitrateMode_YieldsBitrateArgs()
    {
        var s    = new ExportSettings { UseCrf = false, Bitrate = 6000, VideoCodec = "libx264" };
        var args = ExportArgBuilders.QualityArgs(s).ToArray();

        Assert.Contains("-b:v",  args);
        Assert.Contains("6000k", args);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void QualityArgs_WithPresetForX265_IncludesPreset()
    {
        var s    = new ExportSettings { UseCrf = true, VideoCodec = "libx265", Preset = "slow" };
        var args = ExportArgBuilders.QualityArgs(s).ToArray();

        Assert.Contains("-preset", args);
        Assert.Contains("slow",    args);
    }

    [Fact]
    public void QualityArgs_WithPresetForWebm_ExcludesPreset()
    {
        var s    = new ExportSettings { UseCrf = true, VideoCodec = "libvpx-vp9", Preset = "slow" };
        var args = ExportArgBuilders.QualityArgs(s).ToArray();

        Assert.DoesNotContain("-preset", args);
    }

    // ── AudioOutputArgs ──────────────────────────────────────────────────────

    [Fact]
    public void AudioOutputArgs_WhenEnabled_YieldsCodecAndBitrate()
    {
        var s    = new ExportSettings { IncludeAudio = true, AudioCodec = "libopus", AudioBitrate = 192 };
        var args = ExportArgBuilders.AudioOutputArgs(s).ToArray();

        Assert.Contains("-c:a",   args);
        Assert.Contains("libopus", args);
        Assert.Contains("-b:a",   args);
        Assert.Contains("192k",   args);
    }

    [Fact]
    public void AudioOutputArgs_WhenDisabled_YieldsOnlyAnFlag()
    {
        var s    = new ExportSettings { IncludeAudio = false };
        var args = ExportArgBuilders.AudioOutputArgs(s).ToArray();

        Assert.Equal(["-an"], args);
    }

    // ── AudioPassthroughArgs ─────────────────────────────────────────────────

    [Fact]
    public void AudioPassthroughArgs_WhenEnabled_YieldsMapAndCopy()
    {
        var s    = new ExportSettings { IncludeAudio = true };
        var args = ExportArgBuilders.AudioPassthroughArgs(s).ToArray();

        Assert.Contains("-map",  args);
        Assert.Contains("0:a?",  args);
        Assert.Contains("-c:a",  args);
        Assert.Contains("copy",  args);
    }

    [Fact]
    public void AudioPassthroughArgs_WhenDisabled_YieldsOnlyAnFlag()
    {
        var s    = new ExportSettings { IncludeAudio = false };
        var args = ExportArgBuilders.AudioPassthroughArgs(s).ToArray();

        Assert.Equal(["-an"], args);
    }

    // ── XfadeStyle ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TransitionStyle.Fade,      "fade")]
    [InlineData(TransitionStyle.Dissolve,  "dissolve")]
    [InlineData(TransitionStyle.WipeLeft,  "wipeleft")]
    [InlineData(TransitionStyle.WipeRight, "wiperight")]
    [InlineData(TransitionStyle.SlideLeft, "slideleft")]
    [InlineData(TransitionStyle.Zoom,      "zoom")]
    [InlineData(TransitionStyle.CircleOpen,  "circleopen")]
    [InlineData(TransitionStyle.CircleClose, "circleclose")]
    [InlineData(TransitionStyle.Radial,      "radial")]
    [InlineData(TransitionStyle.SmoothLeft,  "smoothleft")]
    [InlineData(TransitionStyle.SmoothRight, "smoothright")]
    [InlineData(TransitionStyle.SmoothUp,    "smoothup")]
    [InlineData(TransitionStyle.SmoothDown,  "smoothdown")]
    [InlineData(TransitionStyle.Pixelize,    "pixelize")]
    [InlineData(TransitionStyle.FadeBlack,   "fadeblack")]
    [InlineData(TransitionStyle.FadeWhite,   "fadewhite")]
    [InlineData(TransitionStyle.Cut,       "fade")]   // Cut falls through to default
    public void XfadeStyle_ReturnsExpectedString(TransitionStyle style, string expected)
    {
        Assert.Equal(expected, ExportArgBuilders.XfadeStyle(style));
    }

    // ── BuildXfadeFilterComplex ──────────────────────────────────────────────

    [Fact]
    public void BuildXfadeFilterComplex_TwoSegments_ProducesValidFilter()
    {
        var segs = new List<string> { "a.mp4", "b.mp4" };
        var durs = new List<double> { 5.0, 5.0 };
        var trs  = new List<Transition>();

        var filter = ExportArgBuilders.BuildXfadeFilterComplex(segs, durs, trs);

        Assert.Contains("xfade", filter);
        Assert.Contains("[vout]", filter);
        Assert.Contains("[0:v][1:v]", filter);
        Assert.False(filter.EndsWith(";"), "Filter should not end with a semicolon");
    }

    [Fact]
    public void BuildXfadeFilterComplex_ThreeSegments_TwoXfadeSteps()
    {
        var segs = new List<string> { "a.mp4", "b.mp4", "c.mp4" };
        var durs = new List<double> { 5.0, 5.0, 5.0 };
        var trs  = new List<Transition>();

        var filter = ExportArgBuilders.BuildXfadeFilterComplex(segs, durs, trs);

        Assert.Equal(2, filter.Split("xfade").Length - 1);
    }

    [Fact]
    public void BuildXfadeFilterComplex_WithTransition_UsesSpecifiedStyle()
    {
        var segs = new List<string> { "a.mp4", "b.mp4" };
        var durs = new List<double> { 5.0, 5.0 };
        var trs  = new List<Transition>
        {
            new() { Style = TransitionStyle.WipeLeft, Duration = 0.75 }
        };

        var filter = ExportArgBuilders.BuildXfadeFilterComplex(segs, durs, trs);

        Assert.Contains("wipeleft", filter);
        Assert.Contains("duration=0.75", filter);
    }

    [Fact]
    public void BuildXfadeFilterComplex_SingleSegment_ReturnsEmpty()
    {
        var segs   = new List<string> { "only.mp4" };
        var filter = ExportArgBuilders.BuildXfadeFilterComplex(segs, [5.0], []);

        Assert.Equal(string.Empty, filter);
    }

    [Fact]
    public void BuildXfadeFilterComplex_MismatchedDurationsCount_Throws()
    {
        var segs = new List<string> { "a.mp4", "b.mp4" };
        var durs = new List<double> { 5.0 };  // one short

        Assert.Throws<ArgumentException>(() => ExportArgBuilders.BuildXfadeFilterComplex(segs, durs, []));
    }

    // Pinned regression coverage for the real-duration offset fix: before this fix, every
    // segment was assumed to be exactly 5 seconds (`cumOffset += 5.0 - dur`), so any clip with a
    // different length produced a wrong junction offset. These pin the correct chained-xfade
    // recurrence: offset_i = offset_{i-1} + segmentDurations[i] - transitionDuration_i.

    [Fact]
    public void BuildXfadeFilterComplex_TwoSegments_NonFiveSecondDuration_UsesRealOffset()
    {
        var segs = new List<string> { "a.mp4", "b.mp4" };
        var durs = new List<double> { 2.5, 10.0 };
        var trs  = new List<Transition> { new() { Style = TransitionStyle.Fade, Duration = 2.0 } };

        var filter = ExportArgBuilders.BuildXfadeFilterComplex(segs, durs, trs);

        // offset = durs[0] - transitionDuration = 2.5 - 2.0 = 0.5 (NOT 5.0 - 2.0 = 3.0, the old
        // hardcoded-5s answer).
        Assert.Contains("offset=0.50", filter);
    }

    [Fact]
    public void BuildXfadeFilterComplex_ThreeSegments_MixedDurations_AccumulatesRealOffsets()
    {
        var segs = new List<string> { "a.mp4", "b.mp4", "c.mp4" };
        var durs = new List<double> { 3.0, 4.0, 2.0 };
        var trs  = new List<Transition>
        {
            new() { Style = TransitionStyle.Fade,     Duration = 0.5 },
            new() { Style = TransitionStyle.Dissolve, Duration = 1.0 },
        };

        var filter = ExportArgBuilders.BuildXfadeFilterComplex(segs, durs, trs);

        // offset_0 = durs[0] - dur_0            = 3.0 - 0.5 = 2.5
        // offset_1 = offset_0 + durs[1] - dur_1  = 2.5 + 4.0 - 1.0 = 5.5
        Assert.Contains("offset=2.50", filter);
        Assert.Contains("offset=5.50", filter);
    }

    // ── BuildCrossTrackXfadeFilter ────────────────────────────────────────────

    [Fact]
    public void BuildCrossTrackXfadeFilter_ProducesExpectedFilterString()
    {
        var filter = ExportArgBuilders.BuildCrossTrackXfadeFilter(TransitionStyle.Fade, 1.5, 2.5);

        Assert.Equal("[0:v][1:v]xfade=transition=fade:duration=1.50:offset=2.50[vout]", filter);
    }

    [Fact]
    public void BuildCrossTrackXfadeFilter_UsesSpecifiedStyle()
    {
        var filter = ExportArgBuilders.BuildCrossTrackXfadeFilter(TransitionStyle.WipeRight, 1.0, 0.0);

        Assert.Contains("transition=wiperight", filter);
    }

    [Fact]
    public void BuildCrossTrackXfadeFilter_MapsToTwoInputsAndSingleOutput()
    {
        var filter = ExportArgBuilders.BuildCrossTrackXfadeFilter(TransitionStyle.Dissolve, 2.0, 0.0);

        Assert.StartsWith("[0:v][1:v]", filter);
        Assert.EndsWith("[vout]", filter);
    }

    // ── ProgressInRange ──────────────────────────────────────────────────────

    [Fact]
    public void ProgressInRange_FirstOfThree_ReturnsRangeStart()
    {
        Assert.Equal(0, ExportArgBuilders.ProgressInRange(0, 3, 0, 45));
    }

    [Fact]
    public void ProgressInRange_LastOfThree_ReturnsNearRangeEnd()
    {
        var result = ExportArgBuilders.ProgressInRange(2, 3, 0, 45);
        Assert.InRange(result, 25, 44);
    }

    [Fact]
    public void ProgressInRange_SingleTotal_ReturnsRangeStart()
    {
        Assert.Equal(10, ExportArgBuilders.ProgressInRange(0, 1, 10, 90));
    }

    // ── MimeType ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("webm", "video/webm")]
    [InlineData("mov",  "video/quicktime")]
    [InlineData("mp4",  "video/mp4")]
    [InlineData("mkv",  "video/mp4")]   // unknown → mp4 MIME
    public void MimeType_ReturnsExpected(string format, string expected)
    {
        Assert.Equal(expected, ExportArgBuilders.MimeType(format));
    }

    // ── SanitiseFilename ─────────────────────────────────────────────────────

    [Fact]
    public void SanitiseFilename_ValidName_Unchanged()
    {
        Assert.Equal("my-video_01", ExportArgBuilders.SanitiseFilename("my-video_01"));
    }

    [Fact]
    public void SanitiseFilename_InvalidChars_Stripped()
    {
        var result = ExportArgBuilders.SanitiseFilename("out<>put|file");
        Assert.DoesNotContain("<",  result);
        Assert.DoesNotContain(">",  result);
        Assert.DoesNotContain("|",  result);
    }

    [Fact]
    public void SanitiseFilename_EmptyResult_FallsBackToOutput()
    {
        // All chars invalid on Windows — use a path separator string
        var result = ExportArgBuilders.SanitiseFilename(new string(Path.GetInvalidFileNameChars()));
        Assert.Equal("output", result);
    }

    [Fact]
    public void SanitiseFilename_WhitespaceOnly_FallsBackToOutput()
    {
        Assert.Equal("output", ExportArgBuilders.SanitiseFilename("   "));
    }

    // ── BuildTrimArgs speed / setpts ──────────────────────────────────────────────

    [Fact]
    public void BuildTrimArgs_SpeedOne_DoesNotAddSetptsFilter()
    {
        var s    = new ExportSettings { VideoCodec = "libx264", UseCrf = true };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s);

        Assert.DoesNotContain("-filter:v", args);
    }

    [Fact]
    public void BuildTrimArgs_SpeedTwo_AddsSetptsFilter()
    {
        var s    = new ExportSettings { VideoCodec = "libx264", UseCrf = true };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 2.0, s);

        Assert.Contains("-filter:v", args);
        var idx = Array.IndexOf(args, "-filter:v");
        // setpts multiplier for 2x = 1/2 = 0.5
        Assert.Contains("setpts=", args[idx + 1]);
        Assert.Contains("0.5", args[idx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_HalfSpeed_AddsSetptsFilter()
    {
        var s    = new ExportSettings { VideoCodec = "libx264", UseCrf = true };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 0.5, s);

        var idx = Array.IndexOf(args, "-filter:v");
        Assert.True(idx >= 0, "Expected -filter:v in args");
        // setpts multiplier for 0.5x = 1/0.5 = 2.0
        Assert.Contains("2.0", args[idx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_SpeedTwo_WithAudio_AddsAtempoFilter()
    {
        var s    = new ExportSettings { VideoCodec = "libx264", UseCrf = true, IncludeAudio = true, AudioCodec = "aac", AudioBitrate = 128 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 2.0, s);

        Assert.Contains("-filter:a", args);
        var idx = Array.IndexOf(args, "-filter:a");
        Assert.Contains("atempo=", args[idx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_SpeedOne_WithAudio_NoAtempoFilter()
    {
        var s    = new ExportSettings { VideoCodec = "libx264", UseCrf = true, IncludeAudio = true, AudioCodec = "aac", AudioBitrate = 128 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s);

        Assert.DoesNotContain("-filter:a", args);
    }

    // ── BuildAtempoChain ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildAtempoChain_SpeedOne_ReturnsSingleAtempo()
    {
        var chain = ExportArgBuilders.BuildAtempoChain(1.0);
        Assert.Single(chain.Split(','));
        Assert.StartsWith("atempo=", chain);
    }

    [Fact]
    public void BuildAtempoChain_SpeedFour_ChainsMultipleAtempo()
    {
        // 4x = atempo=2.0,atempo=2.0
        var chain = ExportArgBuilders.BuildAtempoChain(4.0);
        var parts = chain.Split(',');
        Assert.True(parts.Length >= 2, $"Expected chained atempo for 4x but got: {chain}");
        Assert.All(parts, p => Assert.StartsWith("atempo=", p));
    }

    [Fact]
    public void BuildAtempoChain_QuarterSpeed_ChainsMultipleAtempo()
    {
        // 0.25x = atempo=0.5,atempo=0.5
        var chain = ExportArgBuilders.BuildAtempoChain(0.25);
        var parts = chain.Split(',');
        Assert.True(parts.Length >= 2, $"Expected chained atempo for 0.25x but got: {chain}");
    }

    [Fact]
    public void BuildAtempoChain_NormalSpeed_ValueCloseToOne()
    {
        var chain  = ExportArgBuilders.BuildAtempoChain(1.0);
        var value  = double.Parse(chain.Replace("atempo=", ""), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1.0, value, precision: 3);
    }

    // ── VideoClip.EffectiveDuration ───────────────────────────────────────────────

    [Fact]
    public void VideoClip_EffectiveDuration_SpeedOne_EqualsTrimmedDuration()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 8.0, Speed = 1.0 };
        Assert.Equal(clip.TrimmedDuration, clip.EffectiveDuration, precision: 9);
    }

    [Fact]
    public void VideoClip_EffectiveDuration_SpeedTwo_IsHalfTrimmedDuration()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 8.0, Speed = 2.0 };
        Assert.Equal(4.0, clip.EffectiveDuration, precision: 9);
    }

    [Fact]
    public void VideoClip_EffectiveDuration_HalfSpeed_IsDoubleTrimmedDuration()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 6.0, Speed = 0.5 };
        Assert.Equal(12.0, clip.EffectiveDuration, precision: 9);
    }

    // ── BuildVolumeAutomationFilter ───────────────────────────────────────────

    [Fact]
    public void BuildVolumeAutomationFilter_NoKeyframes_ReturnsScalar()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 10.0, Volume = 1.5 };
        var filter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, 10.0);
        Assert.Equal("volume=1.500000", filter);
    }

    [Fact]
    public void BuildVolumeAutomationFilter_OneKeyframe_ReturnsScalar()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 10.0, Volume = 0.8 };
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.5, Volume = 1.2 });
        var filter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, 10.0);
        // < 2 keyframes → scalar fallback
        Assert.StartsWith("volume=", filter);
        Assert.DoesNotContain("eval=frame", filter);
    }

    [Fact]
    public void BuildVolumeAutomationFilter_TwoKeyframes_ReturnsIfExpression()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 10.0 };
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.0, Volume = 0.0 });
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 1.0, Volume = 2.0 });
        var filter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, 10.0);
        Assert.Contains("eval=frame", filter);
        Assert.Contains("if(", filter);
        Assert.Contains("lt(t", filter);
    }

    [Fact]
    public void BuildVolumeAutomationFilter_TwoKeyframes_ContainsAbsoluteTimestamps()
    {
        var clip = new VideoClip { Duration = 10.0, EndTrim = 10.0 };
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.0, Volume = 1.0 });
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.5, Volume = 0.5 });
        var filter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, 10.0);
        // Position 0.5 * 10.0 = 5.0 seconds
        Assert.Contains("5.000000", filter);
    }

    [Fact]
    public void BuildVolumeAutomationFilter_UnityScalar_ReturnsVolumeOne()
    {
        var clip = new VideoClip { Duration = 5.0, EndTrim = 5.0, Volume = 1.0 };
        var filter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, 5.0);
        Assert.Equal("volume=1.000000", filter);
    }

    [Fact]
    public void BuildVolumeAutomationFilter_SilenceScalar_ReturnsVolumeZero()
    {
        var clip = new VideoClip { Duration = 5.0, EndTrim = 5.0, Volume = 0.0 };
        var filter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, 5.0);
        Assert.Equal("volume=0.000000", filter);
    }

    // ── BuildChannelBalanceFilter / BuildAudioFadeFilter / BuildAudioClipFilterChain (item #10) ──

    [Fact]
    public void BuildChannelBalanceFilter_BothUnity_ReturnsNull()
    {
        Assert.Null(ExportArgBuilders.BuildChannelBalanceFilter(1.0, 1.0));
    }

    [Fact]
    public void BuildChannelBalanceFilter_LeftReduced_ReturnsPanFilter()
    {
        var filter = ExportArgBuilders.BuildChannelBalanceFilter(0.5, 1.0);
        Assert.Equal("pan=stereo|c0=0.500000*c0|c1=1.000000*c1", filter);
    }

    [Fact]
    public void BuildChannelBalanceFilter_RightMuted_ReturnsPanFilter()
    {
        var filter = ExportArgBuilders.BuildChannelBalanceFilter(1.0, 0.0);
        Assert.Equal("pan=stereo|c0=1.000000*c0|c1=0.000000*c1", filter);
    }

    [Fact]
    public void BuildAudioFadeFilter_BothZero_ReturnsNull()
    {
        Assert.Null(ExportArgBuilders.BuildAudioFadeFilter(0, 0, 10.0));
    }

    [Fact]
    public void BuildAudioFadeFilter_FadeInOnly_ReturnsSingleAfade()
    {
        var filter = ExportArgBuilders.BuildAudioFadeFilter(2.0, 0, 10.0);
        Assert.Equal("afade=t=in:st=0:d=2.000", filter);
    }

    [Fact]
    public void BuildAudioFadeFilter_FadeOutOnly_StartsRelativeToClipEnd()
    {
        var filter = ExportArgBuilders.BuildAudioFadeFilter(0, 3.0, 10.0);
        Assert.Equal("afade=t=out:st=7.000:d=3.000", filter);
    }

    [Fact]
    public void BuildAudioFadeFilter_BothSet_JoinsWithComma()
    {
        var filter = ExportArgBuilders.BuildAudioFadeFilter(1.0, 2.0, 10.0);
        Assert.Equal("afade=t=in:st=0:d=1.000,afade=t=out:st=8.000:d=2.000", filter);
    }

    [Fact]
    public void BuildAudioClipFilterChain_DefaultClip_IsJustVolume()
    {
        var clip = new AudioClip { Duration = 10.0 };
        var chain = ExportArgBuilders.BuildAudioClipFilterChain(clip, 10.0);
        Assert.Equal("volume=1.000000", chain);
    }

    [Fact]
    public void BuildAudioClipFilterChain_WithChannelBalanceAndFade_CombinesAllParts()
    {
        var clip = new AudioClip
        {
            Duration = 10.0,
            Volume = 0.8,
            LeftVolume = 0.5,
            RightVolume = 1.0,
            FadeInSeconds = 1.0,
        };
        var chain = ExportArgBuilders.BuildAudioClipFilterChain(clip, 10.0);
        Assert.Equal(
            "volume=0.800000,pan=stereo|c0=0.500000*c0|c1=1.000000*c1,afade=t=in:st=0:d=1.000",
            chain);
    }

    [Fact]
    public void BuildAudioClipTrimArgs_ContainsTrimAndFilterAndNoVideoFlag()
    {
        var s = new ExportSettings();
        var args = ExportArgBuilders.BuildAudioClipTrimArgs("in.mp3", "out.mp4", 1.5, 4.5, "volume=1.000000", s);

        Assert.Contains("-vn", args);
        AssertSubsequence(args, ["-ss", "1.500"]);
        AssertSubsequence(args, ["-to", "4.500"]);
        AssertSubsequence(args, ["-filter:a", "volume=1.000000"]);
        Assert.Equal("out.mp4", args[^1]);
    }

    /// <summary>
    /// Item #70 phase 174 — the seek must be an INPUT option. A subsequence check can't express
    /// this: <c>-ss</c>/<c>-to</c> were present and correctly valued before the fix too, just on
    /// the wrong side of <c>-i</c>, where ffmpeg applies them after the filter graph instead of
    /// before it. That one difference truncated every timeline-positioned audio clip by its own
    /// delay and made fade-in/volume automation inert on every trimmed clip — found by measuring
    /// real ffmpeg output, not by reading argv. Assert the position, not just the presence.
    /// </summary>
    [Fact]
    public void BuildAudioClipTrimArgs_SeeksOnTheInputSoTheFilterChainSeesClipRelativeTime()
    {
        var s = new ExportSettings();
        var args = ExportArgBuilders.BuildAudioClipTrimArgs("in.mp3", "out.mp4", 1.5, 4.5, "volume=1.000000", s);

        var inputIndex = Array.IndexOf(args, "-i");
        var ssIndex    = Array.IndexOf(args, "-ss");
        var toIndex    = Array.IndexOf(args, "-to");

        Assert.True(ssIndex >= 0 && toIndex >= 0 && inputIndex >= 0);
        Assert.True(ssIndex < inputIndex, "-ss must precede -i (input-side seek), not follow it.");
        Assert.True(toIndex < inputIndex, "-to must precede -i (input-side seek), not follow it.");
        Assert.Equal("in.mp3", args[inputIndex + 1]);
    }

    // ── BuildTrimArgs with volume filter ─────────────────────────────────────

    [Fact]
    public void BuildTrimArgs_WithVolumeFilter_AppendsToAudioFilterChain()
    {
        var s = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 1.0, s,
            audioVolumeFilter: "volume=0.500000");
        // Should find -filter:a containing the volume filter
        var idx = Array.IndexOf(args, "-filter:a");
        Assert.True(idx >= 0, "-filter:a not found");
        Assert.Contains("volume=0.500000", args[idx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_WithSpeedAndVolumeFilter_ChainsAtempoAndVolume()
    {
        var s = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 2.0, s,
            audioVolumeFilter: "volume=1.500000");
        var idx = Array.IndexOf(args, "-filter:a");
        Assert.True(idx >= 0, "-filter:a not found");
        var chain = args[idx + 1];
        Assert.Contains("atempo=", chain);
        Assert.Contains("volume=1.500000", chain);
        // atempo must appear before volume
        Assert.True(chain.IndexOf("atempo=") < chain.IndexOf("volume="));
    }

    [Fact]
    public void BuildTrimArgs_NoVolumeFilter_NoAudioFilterWhenSpeedIsUnity()
    {
        var s = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 5, 1.0, s);
        Assert.DoesNotContain("-filter:a", args);
    }

    // ── GetVolumeAt interpolation ─────────────────────────────────────────────

    [Fact]
    public void VideoClip_GetVolumeAt_NoKeyframes_ReturnsScalar()
    {
        var clip = new VideoClip { Volume = 0.75 };
        Assert.Equal(0.75, clip.GetVolumeAt(0.5), precision: 9);
    }

    [Fact]
    public void VideoClip_GetVolumeAt_TwoKeyframes_InterpolatesLinearly()
    {
        var clip = new VideoClip { Volume = 1.0 };
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.0, Volume = 0.0 });
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 1.0, Volume = 2.0 });
        // Midpoint should be 1.0
        Assert.Equal(1.0, clip.GetVolumeAt(0.5), precision: 9);
    }

    [Fact]
    public void VideoClip_GetVolumeAt_BeforeFirstKeyframe_HoldsFirstVolume()
    {
        var clip = new VideoClip();
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.4, Volume = 0.6 });
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.8, Volume = 1.4 });
        Assert.Equal(0.6, clip.GetVolumeAt(0.0), precision: 9);
    }

    [Fact]
    public void VideoClip_GetVolumeAt_AfterLastKeyframe_HoldsLastVolume()
    {
        var clip = new VideoClip();
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.2, Volume = 0.5 });
        clip.VolumeAutomation.Add(new VolumeKeyframe { Position = 0.6, Volume = 1.8 });
        Assert.Equal(1.8, clip.GetVolumeAt(1.0), precision: 9);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static ExportSettings DefaultSettings() => new()
    {
        VideoCodec   = "libx264",
        AudioCodec   = "aac",
        AudioBitrate = 128,
        Bitrate      = 4000,
        UseCrf       = true,
        Crf          = 23,
        PixelFormat  = "yuv420p",
        IncludeAudio = true,
        Preset       = "fast",
    };

    // ── Chapter metadata tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildChapterMetadata_NoMarkers_ReturnsEmpty()
    {
        var result = ExportArgBuilders.BuildChapterMetadata([], 120.0);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildChapterMetadata_SingleMarker_ContainsFfmetadataHeader()
    {
        var markers = new List<TimelineMarker>
        {
            new() { Label = "Intro", TimeSeconds = 0.0 }
        };
        var result = ExportArgBuilders.BuildChapterMetadata(markers, 60.0);

        Assert.Contains(";FFMETADATA1", result);
        Assert.Contains("[CHAPTER]", result);
        Assert.Contains("TIMEBASE=1/1000", result);
    }

    [Fact]
    public void BuildChapterMetadata_SingleMarker_StartIsZeroEndIsTotal()
    {
        var markers = new List<TimelineMarker>
        {
            new() { Label = "Intro", TimeSeconds = 0.0 }
        };
        var result = ExportArgBuilders.BuildChapterMetadata(markers, 90.0);

        Assert.Contains("START=0", result);
        Assert.Contains("END=90000", result);
    }

    [Fact]
    public void BuildChapterMetadata_TwoMarkers_SecondChapterStartsAtFirstMarkerEnd()
    {
        var markers = new List<TimelineMarker>
        {
            new() { Label = "Part 1", TimeSeconds = 0.0 },
            new() { Label = "Part 2", TimeSeconds = 30.0 },
        };
        var result = ExportArgBuilders.BuildChapterMetadata(markers, 60.0);

        // First chapter: START=0, END=30000
        Assert.Contains("START=0",     result);
        Assert.Contains("END=30000",   result);
        // Second chapter: START=30000, END=60000
        Assert.Contains("START=30000", result);
        Assert.Contains("END=60000",   result);
    }

    [Fact]
    public void BuildChapterMetadata_MarkersAreUnsorted_OutputIsSortedByTime()
    {
        var markers = new List<TimelineMarker>
        {
            new() { Label = "B", TimeSeconds = 20.0 },
            new() { Label = "A", TimeSeconds = 5.0  },
        };
        var result = ExportArgBuilders.BuildChapterMetadata(markers, 60.0);
        var aIdx   = result.IndexOf("title=A", StringComparison.Ordinal);
        var bIdx   = result.IndexOf("title=B", StringComparison.Ordinal);

        Assert.True(aIdx < bIdx, "Marker A (t=5) should appear before marker B (t=20)");
    }

    [Fact]
    public void BuildChapterMetadata_LabelWithSpecialChars_IsEscaped()
    {
        var markers = new List<TimelineMarker>
        {
            new() { Label = "A=B#C", TimeSeconds = 0.0 }
        };
        var result = ExportArgBuilders.BuildChapterMetadata(markers, 10.0);

        Assert.Contains(@"title=A\=B\#C", result);
    }

    // Item #38 phase 121: EscapeMetadataValue escaped \, =, #, and ; but not newlines — a chapter
    // title containing "\n[CHAPTER]\nSTART=..." could inject a whole extra directive block into
    // the ffmetadata stream. ffmpeg's own spec requires newlines to be backslash-escaped too.
    [Fact]
    public void BuildChapterMetadata_LabelWithNewline_IsEscapedNotInjected()
    {
        var markers = new List<TimelineMarker>
        {
            new() { Label = "Intro\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1\ntitle=Injected", TimeSeconds = 0.0 }
        };

        var result = ExportArgBuilders.BuildChapterMetadata(markers, 10.0);

        // Exactly one real [CHAPTER] block header LINE (nothing else on it) — the injected one,
        // embedded inside the title value, must read as "[CHAPTER]\" (continuation-escaped, part
        // of the title's own value) rather than a bare "[CHAPTER]" line ffmpeg would parse as a
        // second, genuine section header.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            result, @"^\[CHAPTER\]$", System.Text.RegularExpressions.RegexOptions.Multiline));
        // The newline immediately before the embedded "[CHAPTER]" text is escaped (a literal
        // backslash then the newline), not bare — this is what makes it a continuation of the
        // title value instead of a new line ffmpeg's parser would act on.
        Assert.Contains("\\\n[CHAPTER]", result);
        Assert.Contains("\\\nTIMEBASE", result);
    }

    [Fact]
    public void BuildChapterEmbedArgs_ReturnsStreamCopyArgs()
    {
        var args = ExportArgBuilders.BuildChapterEmbedArgs("in.mp4", "meta.ffmeta", "out.mp4");

        Assert.Contains("-c",         args);
        Assert.Contains("copy",       args);
        Assert.Contains("-i",         args);
        Assert.Contains("in.mp4",     args);
        Assert.Contains("meta.ffmeta",args);
        Assert.Contains("out.mp4",    args);
        Assert.Contains("-map_metadata", args);
        Assert.Contains("-map_chapters", args);
    }

    [Fact]
    public void ExportSettings_EmbedChapters_DefaultIsTrue()
    {
        var s = new ExportSettings();
        Assert.True(s.EmbedChapters);
    }

    // ── BuildVideoEffectsFilter ──────────────────────────────────────────────

    [Fact]
    public void BuildVideoEffectsFilter_NeutralEffects_ReturnsEmptyString()
    {
        var result = ExportArgBuilders.BuildVideoEffectsFilter(new ClipEffects(), clipDuration: 10);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_Brightness_ContainsEqFilter()
    {
        var fx     = new ClipEffects { Brightness = 0.3 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        Assert.Contains("eq=brightness=", result);
        Assert.Contains("0.3000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_Contrast_ContainsEqFilter()
    {
        var fx     = new ClipEffects { Contrast = 1.5 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        Assert.Contains("contrast=1.5000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_Saturation_ContainsEqFilter()
    {
        var fx     = new ClipEffects { Saturation = 0.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        Assert.Contains("saturation=0.0000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_FadeIn_ContainsFadeFilter()
    {
        var fx     = new ClipEffects { FadeInSeconds = 2.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        Assert.Contains("fade=t=in:st=0:d=2.000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_FadeOut_ContainsFadeFilter()
    {
        var fx     = new ClipEffects { FadeOutSeconds = 2.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        Assert.Contains("fade=t=out:", result);
        Assert.Contains(":d=2.000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_FadeOut_StartsAtCorrectPosition()
    {
        // 10s clip, 2s fade-out: should start at 8s
        var fx     = new ClipEffects { FadeOutSeconds = 2.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        Assert.Contains("st=8.000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_FadeIn_ClampedToHalfDuration()
    {
        // FadeIn > duration should be clamped to duration
        var fx     = new ClipEffects { FadeInSeconds = 20.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 4);
        Assert.Contains("d=4.000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_SpeedScalesFadeDuration()
    {
        // 10s trimmed, speed=2: effective duration=5s; FadeOut=2 starts at 3s wall-clock
        var fx     = new ClipEffects { FadeOutSeconds = 2.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10, speed: 2.0);
        Assert.Contains("st=3.000", result);
    }

    [Fact]
    public void BuildVideoEffectsFilter_AllFiltersChainedWithComma()
    {
        var fx     = new ClipEffects { Brightness = 0.1, FadeInSeconds = 1.0, FadeOutSeconds = 1.0 };
        var result = ExportArgBuilders.BuildVideoEffectsFilter(fx, clipDuration: 10);
        var parts  = result.Split(',');
        Assert.True(parts.Length >= 3, $"Expected >=3 comma-separated parts, got: {result}");
    }

    [Fact]
    public void BuildTrimArgs_WithEffects_AddsFilterV()
    {
        var s  = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var fx = new ClipEffects { Brightness = 0.2 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s, effects: fx);

        Assert.Contains("-filter:v", args);
        var filterIdx = Array.IndexOf(args, "-filter:v");
        Assert.Contains("eq=", args[filterIdx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_NeutralEffects_NoFilterV()
    {
        var s  = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var fx = new ClipEffects(); // neutral
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s, effects: fx);

        Assert.DoesNotContain("-filter:v", args);
    }

    [Fact]
    public void BuildTrimArgs_SpeedAndEffects_ChainsSetptsAndEq()
    {
        var s  = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var fx = new ClipEffects { Contrast = 1.5 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 2.0, s, effects: fx);

        var filterIdx = Array.IndexOf(args, "-filter:v");
        Assert.True(filterIdx >= 0, "Expected -filter:v");
        var chain = args[filterIdx + 1];
        Assert.Contains("setpts=", chain);
        Assert.Contains("eq=", chain);
    }

    // ── BuildOverlayFilterComplex ──────────────────────────────────────────

    [Fact]
    public void BuildOverlayFilterComplex_TwoLayers_ContainsOverlayFilter()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264", PixelFormat = "yuv420p" };
        var args = ExportArgBuilders.BuildOverlayFilterComplex(
            ["base.mp4", "top.mp4"], "out.mp4", alphaCompositing: false, settings: s);

        Assert.Contains("-filter_complex", args);
        var fcIdx = Array.IndexOf(args, "-filter_complex");
        Assert.Contains("overlay", args[fcIdx + 1]);
        Assert.Contains("-map", args);
        Assert.Contains("[vout]", args);
    }

    [Fact]
    public void BuildOverlayFilterComplex_AlphaCompositing_ContainsYuva420p()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264", PixelFormat = "yuva420p" };
        var args = ExportArgBuilders.BuildOverlayFilterComplex(
            ["base.mp4", "top.mp4"], "out.mp4", alphaCompositing: true, settings: s);

        var fcIdx = Array.IndexOf(args, "-filter_complex");
        Assert.Contains("yuva420p", args[fcIdx + 1]);
    }

    [Fact]
    public void BuildOverlayFilterComplex_ThreeLayers_ChainsOverlays()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264", PixelFormat = "yuv420p" };
        var args = ExportArgBuilders.BuildOverlayFilterComplex(
            ["a.mp4", "b.mp4", "c.mp4"], "out.mp4", alphaCompositing: false, settings: s);

        var fcIdx  = Array.IndexOf(args, "-filter_complex");
        var graph  = args[fcIdx + 1];
        // Two overlay operations expected for three layers
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(graph, "overlay").Count);
    }

    [Fact]
    public void BuildOverlayFilterComplex_FewerThanTwoLayers_Throws()
    {
        var s = new ExportSettings { VideoCodec = "libx264", PixelFormat = "yuv420p" };
        Assert.Throws<ArgumentException>(() =>
            ExportArgBuilders.BuildOverlayFilterComplex(["only.mp4"], "out.mp4", false, s));
    }

    [Fact]
    public void BuildOverlayFilterComplex_IncludesInputsForEachLayer()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264", PixelFormat = "yuv420p" };
        var args = ExportArgBuilders.BuildOverlayFilterComplex(
            ["a.mp4", "b.mp4", "c.mp4"], "out.mp4", alphaCompositing: false, settings: s);

        // Three -i flags expected
        Assert.Equal(3, args.Count(a => a == "-i"));
    }

    // ── BuildTrimArgs + MuteAudio ────────────────────────────────────────

    [Fact]
    public void BuildTrimArgs_MuteAudio_True_ContainsAnFlag()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264",
                                        IncludeAudio = true, AudioCodec = "aac", AudioBitrate = 128 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s,
                                                   muteAudio: true);

        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void BuildTrimArgs_MuteAudio_False_ContainsAudioCodec()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264",
                                        IncludeAudio = true, AudioCodec = "aac", AudioBitrate = 128 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s,
                                                   muteAudio: false);

        Assert.DoesNotContain("-an", args);
        Assert.Contains("-c:a", args);
    }

    [Fact]
    public void BuildTrimArgs_MuteAudio_OverridesIncludeAudio()
    {
        // Even when IncludeAudio = true, MuteAudio should force -an
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264",
                                        IncludeAudio = true, AudioCodec = "aac", AudioBitrate = 128 };
        var args = ExportArgBuilders.BuildTrimArgs("in.mp4", "out.mp4", 0, 10, 1.0, s,
                                                   muteAudio: true);

        Assert.Contains("-an", args);
    }

    // ── BuildImageSegmentArgs (Phase 28) ─────────────────────────────────────────

    [Fact]
    public void BuildImageSegmentArgs_ContainsLoopFlag()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s);

        Assert.Contains("-loop", args);
        Assert.Contains("1", args);
    }

    [Fact]
    public void BuildImageSegmentArgs_ContainsDuration()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 7.5, s);

        Assert.Contains("-t", args);
        var tIdx = Array.IndexOf(args, "-t");
        Assert.Equal("7.500", args[tIdx + 1]);
    }

    [Fact]
    public void BuildImageSegmentArgs_ContainsInputAndOutput()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("photo.jpg", "seg.mp4", 5.0, s);

        Assert.Contains("photo.jpg", args);
        Assert.Contains("seg.mp4", args);
    }

    [Fact]
    public void BuildImageSegmentArgs_CrfMode_ContainsCrf()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s);

        Assert.Contains("-crf", args);
        Assert.Contains("18", args);
        Assert.DoesNotContain("-b:v", args);
    }

    [Fact]
    public void BuildImageSegmentArgs_BitrateMode_ContainsBitrate()
    {
        var s    = new ExportSettings { UseCrf = false, Bitrate = 3000, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s);

        Assert.Contains("-b:v", args);
        Assert.Contains("3000k", args);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void BuildImageSegmentArgs_AlwaysContainsAnFlag()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s);

        Assert.Contains("-an", args);
    }

    [Fact]
    public void BuildImageSegmentArgs_WithDimensions_ContainsScaleFilter()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s,
                                                           outputWidth: 1920, outputHeight: 1080);

        Assert.Contains("-vf", args);
        var vfIdx = Array.IndexOf(args, "-vf");
        Assert.Contains("scale=1920:1080", args[vfIdx + 1]);
    }

    [Fact]
    public void BuildImageSegmentArgs_WithoutDimensions_NoScaleFilter()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s,
                                                           outputWidth: 0, outputHeight: 0);

        // -vf may still be present if effects are applied, but scale should not be
        var vfIdx = Array.IndexOf(args, "-vf");
        if (vfIdx >= 0)
            Assert.DoesNotContain("scale=", args[vfIdx + 1]);
    }

    [Fact]
    public void BuildImageSegmentArgs_Libx265_ContainsPreset()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 28, VideoCodec = "libx265", Preset = "fast" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s);

        Assert.Contains("-preset", args);
        Assert.Contains("fast", args);
    }

    [Fact]
    public void BuildImageSegmentArgs_ContainsPixFmt()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 23, VideoCodec = "libx264", PixelFormat = "yuv420p" };
        var args = ExportArgBuilders.BuildImageSegmentArgs("img.png", "out.mp4", 5.0, s);

        Assert.Contains("-pix_fmt", args);
        Assert.Contains("yuv420p", args);
    }

    // ── ApplyMotionFrame ─────────────────────────────────────────────────────

    [Fact]
    public void ApplyMotionFrame_OverridesPositionFromFrame()
    {
        var clip  = new CalloutClip { Name = "box", X = 0.1, Y = 0.2 };
        var frame = new MotionFrame(X: 0.6, Y: 0.7, Scale: 1.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.6, animated.X);
        Assert.Equal(0.7, animated.Y);
    }

    [Fact]
    public void ApplyMotionFrame_ScalesWidthAndHeight_ByFrameScale()
    {
        var clip  = new CalloutClip { Name = "box", Width = 0.2, Height = 0.1 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 2.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Width,  precision: 6);
        Assert.Equal(0.2, animated.Height, precision: 6);
    }

    [Fact]
    public void ApplyMotionFrame_MultipliesOpacity_ByFrameAlpha()
    {
        var clip  = new CalloutClip { Name = "box", Opacity = 0.8 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 0.5);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Opacity, precision: 6); // 0.8 * 0.5, not a straight override
    }

    [Fact]
    public void ApplyMotionFrame_PreservesShapeAndRotation()
    {
        var clip = new CalloutClip
        {
            Name     = "arrow",
            Shape    = ShapeType.Arrow,
            Rotation = 15.0,
        };
        var frame = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.5, Alpha: 0.9);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(ShapeType.Arrow, animated.Shape);
        Assert.Equal(15.0, animated.Rotation);
    }

    [Fact]
    public void ApplyMotionFrame_OverridesFillAndStrokeColor_FromFrame()
    {
        var clip  = new CalloutClip { Name = "box", FillColor = 123.0, StrokeColor = 456.0 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 1.0)
        {
            FillColor   = 111.0,
            StrokeColor = 222.0,
        };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(111.0, animated.FillColor);
        Assert.Equal(222.0, animated.StrokeColor);
    }

    [Fact]
    public void ApplyMotionFrame_MergesControlPointValues_OverStaticOnes()
    {
        var clip = new CalloutClip
        {
            Name = "arrow",
            Shape = ShapeType.Arrow,
            ControlPointValues = new() { ["startX"] = 0.3, ["endX"] = 0.8 },
        };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 1.0)
        {
            ControlPointValues = new Dictionary<string, double> { ["startX"] = 0.9 },
        };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.9, animated.ControlPointValues["startX"]); // animated key wins
        Assert.Equal(0.8, animated.ControlPointValues["endX"]);   // un-animated key kept from static clip
    }

    // ── ApplyMotionFrame (TextOverlay) ───────────────────────────────────────

    [Fact]
    public void ApplyMotionFrame_TextOverlay_OverridesPositionFromFrame()
    {
        var overlay = new TextOverlay { Name = "title" };
        var frame   = new MotionFrame(X: 0.6, Y: 0.7, Scale: 1.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);

        Assert.Equal(0.6, animated.OverrideX);
        Assert.Equal(0.7, animated.OverrideY);
    }

    [Fact]
    public void ApplyMotionFrame_TextOverlay_ScalesFontSize_ByFrameScale()
    {
        var overlay = new TextOverlay { Name = "title", FontSize = 40 };
        var frame   = new MotionFrame(X: 0.5, Y: 0.5, Scale: 2.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);

        Assert.Equal(80, animated.FontSize);
    }

    [Fact]
    public void ApplyMotionFrame_TextOverlay_MultipliesOpacity_ByFrameAlpha()
    {
        var overlay = new TextOverlay { Name = "title", Opacity = 0.8 };
        var frame   = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 0.5);

        var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);

        Assert.Equal(0.4, animated.Opacity, precision: 6); // 0.8 * 0.5, not a straight override
    }

    [Fact]
    public void ApplyMotionFrame_TextOverlay_OverridesShadow_FromFrame()
    {
        var overlay = new TextOverlay
        {
            Name          = "title",
            ShadowColor   = 111.0,
            ShadowOffsetX = 1.0,
            ShadowOffsetY = 1.0,
            ShadowBlur    = 2.0,
        };
        var frame = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 1.0)
        {
            ShadowColor   = 999.0,
            ShadowOffsetX = 10.0,
            ShadowOffsetY = 20.0,
            ShadowBlur    = 30.0,
        };

        var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);

        Assert.Equal(999.0, animated.ShadowColor);
        Assert.Equal(10.0,  animated.ShadowOffsetX);
        Assert.Equal(20.0,  animated.ShadowOffsetY);
        Assert.Equal(30.0,  animated.ShadowBlur);
    }

    [Fact]
    public void ApplyMotionFrame_TextOverlay_PreservesTextAndFont()
    {
        var overlay = new TextOverlay { Name = "title", Text = "Hello", FontFamily = "Georgia" };
        var frame   = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);

        Assert.Equal("Hello",   animated.Text);
        Assert.Equal("Georgia", animated.FontFamily);
    }

    [Fact]
    public void ApplyMotionFrame_TextOverlay_PreservesRuns()
    {
        // Item #16, phase 115 — Runs is a static per-clip field, not part of MotionFrame; the
        // `with { ... }` expression must carry it through untouched, same as ClipArt's
        // Rotation/TintColor already do.
        var runs    = new List<TextRun> { new() { Text = "Hi", Bold = true } };
        var overlay = new TextOverlay { Name = "title", Runs = runs };
        var frame   = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(overlay, frame);

        Assert.Same(runs, animated.Runs);
    }

    // ── ApplyMotionFrame (ClipArtClip) ───────────────────────────────────────

    [Fact]
    public void ApplyMotionFrame_ClipArt_OverridesPositionFromFrame()
    {
        var clip  = new ClipArtClip { Name = "sticker", X = 0.1, Y = 0.2 };
        var frame = new MotionFrame(X: 0.6, Y: 0.7, Scale: 1.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.6, animated.X);
        Assert.Equal(0.7, animated.Y);
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_ScalesWidthAndHeight_ByFrameScale()
    {
        var clip  = new ClipArtClip { Name = "sticker", Width = 0.2, Height = 0.1 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 2.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Width,  precision: 6);
        Assert.Equal(0.2, animated.Height, precision: 6);
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_PreservesAspectRatioSentinel_WhenHeightIsMinusOne()
    {
        var clip  = new ClipArtClip { Name = "sticker", Width = 0.2, Height = -1.0 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 2.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Width, precision: 6);
        Assert.Equal(-1.0, animated.Height); // sentinel, never scaled
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_MultipliesOpacity_ByFrameAlpha()
    {
        var clip  = new ClipArtClip { Name = "sticker", Opacity = 0.8 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 0.5);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Opacity, precision: 6); // 0.8 * 0.5, not a straight override
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_PreservesRotationAndTintColor()
    {
        var clip = new ClipArtClip
        {
            Name      = "sticker",
            Rotation  = 25.0,
            TintColor = 999.0,
        };
        var frame = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.5, Alpha: 0.9);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(25.0, animated.Rotation);
        Assert.Equal(999.0, animated.TintColor);
    }

    // ── ApplyMotionFrame — per-axis scale + ClipArt rotation keyframes (item #57 P3) ─────────

    [Fact]
    public void ApplyMotionFrame_Callout_ScalesWidthAndHeightIndependently_ByScaleXY()
    {
        var clip  = new CalloutClip { Name = "box", Width = 0.2, Height = 0.1 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 1.0)
        {
            ScaleX = 3.0,
            ScaleY = 0.5,
        };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.6,  animated.Width,  precision: 6); // 0.2 * 3.0, NOT the legacy Scale=1.0
        Assert.Equal(0.05, animated.Height, precision: 6); // 0.1 * 0.5
    }

    [Fact]
    public void ApplyMotionFrame_Callout_ScaleXY_DefaultToLegacyScale_WhenUnset()
    {
        // MotionFrame's ScaleX/ScaleY init to Scale when the caller never sets them — every
        // pre-P3 construction site (`new MotionFrame(x, y, scale, alpha)`) must keep working
        // identically.
        var clip  = new CalloutClip { Name = "box", Width = 0.2, Height = 0.1 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 2.0, Alpha: 1.0);

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Width,  precision: 6);
        Assert.Equal(0.2, animated.Height, precision: 6);
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_ScalesWidthAndHeightIndependently_ByScaleXY()
    {
        var clip  = new ClipArtClip { Name = "sticker", Width = 0.2, Height = 0.1 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 1.0)
        {
            ScaleX = 2.0,
            ScaleY = 4.0,
        };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Width,  precision: 6);
        Assert.Equal(0.4, animated.Height, precision: 6);
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_ScaleY_NeverAppliedToAspectRatioSentinel()
    {
        var clip  = new ClipArtClip { Name = "sticker", Width = 0.2, Height = -1.0 };
        var frame = new MotionFrame(X: clip.X, Y: clip.Y, Scale: 1.0, Alpha: 1.0) { ScaleX = 2.0, ScaleY = 5.0 };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(0.4, animated.Width, precision: 6);
        Assert.Equal(-1.0, animated.Height); // sentinel, never scaled regardless of ScaleY
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_FrameRotation_OverridesStaticRotation()
    {
        var clip  = new ClipArtClip { Name = "sticker", Rotation = 25.0 };
        var frame = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 1.0) { Rotation = 90.0 };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(90.0, animated.Rotation);
    }

    [Fact]
    public void ApplyMotionFrame_ClipArt_NullFrameRotation_FallsBackToStaticRotation()
    {
        var clip  = new ClipArtClip { Name = "sticker", Rotation = 25.0 };
        var frame = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 1.0); // Rotation left null

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(25.0, animated.Rotation);
    }

    [Fact]
    public void ApplyMotionFrame_Callout_IgnoresFrameRotation_NoRotationSupportForCallouts()
    {
        // Locked scope decision: rotation keyframes are ClipArt-only. Even if a frame somehow
        // carried a Rotation value, CalloutClip's overload must never read it.
        var clip  = new CalloutClip { Name = "box", Rotation = 15.0 };
        var frame = new MotionFrame(X: 0.5, Y: 0.5, Scale: 1.0, Alpha: 1.0) { Rotation = 90.0 };

        var animated = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(15.0, animated.Rotation); // unchanged from the clip's own static value
    }

    // ── BuildClipArtTintMixer (backlog #56) ──────────────────────────────────

    [Fact]
    public void BuildClipArtTintMixer_NullTint_ReturnsNull()
    {
        Assert.Null(ExportArgBuilders.BuildClipArtTintMixer(null));
    }

    [Fact]
    public void BuildClipArtTintMixer_ZeroAlphaTint_ReturnsNull()
    {
        var packed = ColorHelper.Pack(255, 0, 0, a: 0);
        Assert.Null(ExportArgBuilders.BuildClipArtTintMixer(packed));
    }

    [Fact]
    public void BuildClipArtTintMixer_FullAlphaRed_KeepsNothingOfOriginal()
    {
        var packed = ColorHelper.Pack(255, 0, 0, a: 255); // opaque red, full-strength tint
        var mixer  = ExportArgBuilders.BuildClipArtTintMixer(packed);

        Assert.NotNull(mixer);
        Assert.Contains("rr=0.0000", mixer);
        Assert.Contains("ra=1.0000", mixer); // red channel derived entirely from source alpha
        Assert.Contains("gg=0.0000", mixer);
        Assert.Contains("ga=0.0000", mixer); // no green in the tint
        Assert.Contains("bb=0.0000", mixer);
        Assert.Contains("ba=0.0000", mixer);
    }

    [Fact]
    public void BuildClipArtTintMixer_HalfAlpha_BlendsOriginalAndTint()
    {
        var packed = ColorHelper.Pack(255, 255, 255, a: 128); // ~50% white tint
        var mixer  = ExportArgBuilders.BuildClipArtTintMixer(packed);

        Assert.NotNull(mixer);
        Assert.Contains("rr=0.4980", mixer); // keep = 1 - 128/255
        Assert.Contains("gg=0.4980", mixer);
        Assert.Contains("bb=0.4980", mixer);
    }

    // ── ComputeRotatedBounds ──────────────────────────────────────────────────

    [Fact]
    public void ComputeRotatedBounds_ZeroDegrees_ReturnsOriginalSize()
    {
        var (w, h) = ExportArgBuilders.ComputeRotatedBounds(200, 100, 0.0);
        Assert.Equal(200, w);
        Assert.Equal(100, h);
    }

    [Fact]
    public void ComputeRotatedBounds_90Degrees_SwapsWidthAndHeight()
    {
        var (w, h) = ExportArgBuilders.ComputeRotatedBounds(200, 100, 90.0);
        Assert.Equal(100, w);
        Assert.Equal(200, h);
    }

    [Fact]
    public void ComputeRotatedBounds_45Degrees_ExpandsBothDimensions()
    {
        var (w, h) = ExportArgBuilders.ComputeRotatedBounds(200, 100, 45.0);
        Assert.True(w > 200);
        Assert.True(h > 100);
    }

    // ── BuildClipArtStaticOverlayFilter ───────────────────────────────────────

    [Fact]
    public void BuildClipArtStaticOverlayFilter_NoRotationNoTint_MatchesPlainScaleAndOverlay()
    {
        var clip = new ClipArtClip
        {
            X = 0.1, Y = 0.2, Width = 0.2, Height = 0.1,
            TimelinePosition = 2.0, Duration = 3.0,
        };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 500);

        Assert.Contains("scale=200:50", filter);
        Assert.Contains("format=rgba", filter);
        Assert.DoesNotContain("colorchannelmixer", filter);
        Assert.DoesNotContain("rotate=", filter);
        Assert.Contains("overlay=100:100:enable='between(t,2.000,5.000)'", filter);
    }

    [Fact]
    public void BuildClipArtStaticOverlayFilter_WithTint_IncludesColorChannelMixer()
    {
        var clip = new ClipArtClip
        {
            Width = 0.2, Height = 0.1,
            TintColor = ColorHelper.Pack(0, 255, 0, a: 255),
        };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 500);

        Assert.Contains("colorchannelmixer=", filter);
        Assert.Contains("ga=1.0000", filter); // full-strength green tint
    }

    [Fact]
    public void BuildClipArtStaticOverlayFilter_WithRotation_AddsRotateAndRecentersOverlay()
    {
        var clip = new ClipArtClip { X = 0.4, Y = 0.4, Width = 0.2, Height = 0.2, Rotation = 90.0 };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 1000);

        Assert.Contains("rotate=", filter);
        Assert.Contains("ow=rotw(", filter);
        Assert.Contains("oh=roth(", filter);
        // 200x200 square rotated 90° stays 200x200 — overlay position should be unchanged (same center).
        Assert.Contains("overlay=400:400:enable=", filter);
    }

    [Fact]
    public void BuildClipArtStaticOverlayFilter_RotationTinyButNonZero_BelowThreshold_SkipsRotate()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = 0.1, Rotation = 0.0001 };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 500);

        Assert.DoesNotContain("rotate=", filter);
    }

    [Fact]
    public void BuildClipArtStaticOverlayFilter_OpacityBelowOne_IncludesAlphaMixer()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = 0.1, Opacity = 0.4 };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 500);

        Assert.Contains("colorchannelmixer=", filter);
        Assert.Contains("aa=0.4000", filter);
    }

    [Fact]
    public void BuildClipArtStaticOverlayFilter_OpacityFull_OmitsAlphaMixer()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = 0.1, Opacity = 1.0 };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 500);

        Assert.DoesNotContain("colorchannelmixer", filter);
    }

    [Fact]
    public void BuildClipArtStaticOverlayFilter_TintAndOpacity_ShareOneColorChannelMixerCall()
    {
        var clip = new ClipArtClip
        {
            Width = 0.2, Height = 0.1,
            TintColor = ColorHelper.Pack(0, 255, 0, a: 255),
            Opacity = 0.5,
        };
        var filter = ExportArgBuilders.BuildClipArtStaticOverlayFilter(clip, 1000, 500);

        Assert.Contains("ga=1.0000", filter);
        Assert.Contains("aa=0.5000", filter);
        // Both tint and opacity fold into a single colorchannelmixer= invocation, not two chained ones.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(filter, "colorchannelmixer="));
    }

    // ── BuildAppliedEffectsFilter ────────────────────────────────────────────

    private sealed class FakeEffect(string id, string fragment) : IClipEffect
    {
        public string EffectId => id;
        public string DisplayName => id;
        public IReadOnlyList<ClipEffectParameter> ParameterSchema => [];
        public AppliedEffect CreateDefault() => new() { EffectId = id };
        public string BuildFilterFragment(IReadOnlyDictionary<string, double> parameters, double clipDuration, double speed = 1.0)
            => fragment;
    }

    [Fact]
    public void BuildAppliedEffectsFilter_EmptyList_ReturnsEmptyString()
    {
        var registry = new ClipEffectRegistry();
        var result   = ExportArgBuilders.BuildAppliedEffectsFilter([], registry, clipDuration: 10);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_SingleEffect_ReturnsItsFragment()
    {
        var registry = new ClipEffectRegistry();
        registry.Register(new FakeEffect("shake", "shake=strength=5"));
        var effects = new List<AppliedEffect> { new() { EffectId = "shake" } };

        var result = ExportArgBuilders.BuildAppliedEffectsFilter(effects, registry, clipDuration: 10);

        Assert.Equal("shake=strength=5", result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_MultipleEffects_CommaJoined()
    {
        var registry = new ClipEffectRegistry();
        registry.Register(new FakeEffect("shake", "shake=strength=5"));
        registry.Register(new FakeEffect("vignette", "vignette=PI/4"));
        var effects = new List<AppliedEffect>
        {
            new() { EffectId = "shake" },
            new() { EffectId = "vignette" },
        };

        var result = ExportArgBuilders.BuildAppliedEffectsFilter(effects, registry, clipDuration: 10);

        Assert.Equal("shake=strength=5,vignette=PI/4", result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_UnknownEffectId_IsSkipped()
    {
        var registry = new ClipEffectRegistry();
        registry.Register(new FakeEffect("shake", "shake=strength=5"));
        var effects = new List<AppliedEffect>
        {
            new() { EffectId = "does-not-exist" },
            new() { EffectId = "shake" },
        };

        var result = ExportArgBuilders.BuildAppliedEffectsFilter(effects, registry, clipDuration: 10);

        Assert.Equal("shake=strength=5", result);
    }

    [Fact]
    public void BuildAppliedEffectsFilter_NeutralFragment_IsSkipped()
    {
        var registry = new ClipEffectRegistry();
        registry.Register(new FakeEffect("noop", string.Empty));
        var effects = new List<AppliedEffect> { new() { EffectId = "noop" } };

        var result = ExportArgBuilders.BuildAppliedEffectsFilter(effects, registry, clipDuration: 10);

        Assert.Equal(string.Empty, result);
    }

    // ── BuildTrimArgs extraVf ─────────────────────────────────────────────────

    [Fact]
    public void BuildTrimArgs_WithExtraVf_AppendsToFilterChain()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 1.0, s, extraVf: "shake=strength=5");

        Assert.Contains("-filter:v", args);
        var filterIdx = Array.IndexOf(args, "-filter:v");
        Assert.Contains("shake=strength=5", args[filterIdx + 1]);
    }

    // ── BuildTrimArgs resolution scale/pad ────────────────────────────────────
    //
    // Video segments previously encoded at the source clip's native resolution regardless
    // of the export's selected Resolution setting — only image segments and overlay PNGs
    // honoured it. When a source clip's resolution differed from the export target, overlays
    // (rendered at the target resolution) landed at the wrong absolute pixel position against
    // the actual, differently-sized video frame — a callout's "10% from top" could end up
    // entirely off-canvas. These tests pin BuildTrimArgs' new scale+pad parity with
    // BuildImageSegmentArgs.

    [Fact]
    public void BuildTrimArgs_WithResolution_AddsScaleAndPadFilter()
    {
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 1.0, s, outputWidth: 1920, outputHeight: 1080);

        var idx = Array.IndexOf(args, "-filter:v");
        Assert.True(idx >= 0, "-filter:v not found");
        Assert.Contains("scale=1920:1080:force_original_aspect_ratio=decrease", args[idx + 1]);
        Assert.Contains("pad=1920:1080:(ow-iw)/2:(oh-ih)/2", args[idx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_ZeroResolution_NoScaleFilter()
    {
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 1.0, s, outputWidth: 0, outputHeight: 0);

        Assert.DoesNotContain("-filter:v", args);
    }

    [Fact]
    public void BuildTrimArgs_ResolutionAndSpeed_ScaleComesBeforeSetpts()
    {
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 2.0, s, outputWidth: 1280, outputHeight: 720);

        var idx = Array.IndexOf(args, "-filter:v");
        Assert.True(idx >= 0);
        var chain = args[idx + 1];
        Assert.True(chain.IndexOf("scale=", StringComparison.Ordinal) <
                     chain.IndexOf("setpts=", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTrimArgs_ResolutionAndEffects_BothPresentInFilterChain()
    {
        var s    = DefaultSettings();
        var fx   = new ClipEffects { Brightness = 0.2 };
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 1.0, s, effects: fx, outputWidth: 1920, outputHeight: 1080);

        var idx = Array.IndexOf(args, "-filter:v");
        Assert.True(idx >= 0);
        Assert.Contains("scale=1920:1080", args[idx + 1]);
        Assert.Contains("eq=", args[idx + 1]);
    }

    [Fact]
    public void BuildTrimArgs_OnlyWidthSet_NoScaleFilter()
    {
        // Both dimensions must be positive — a partially-specified resolution is treated
        // as "no scale" rather than guessing an aspect ratio.
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 1.0, s, outputWidth: 1920, outputHeight: 0);

        Assert.DoesNotContain("-filter:v", args);
    }

    [Fact]
    public void BuildTrimArgs_WithEffectsAndExtraVf_BothPresentInFilterChain()
    {
        var s    = new ExportSettings { UseCrf = true, Crf = 18, VideoCodec = "libx264" };
        var fx   = new ClipEffects { Brightness = 0.2 };
        var args = ExportArgBuilders.BuildTrimArgs(
            "in.mp4", "out.mp4", 0, 10, 1.0, s, effects: fx, extraVf: "shake=strength=5");

        var filterIdx = Array.IndexOf(args, "-filter:v");
        Assert.True(filterIdx >= 0);
        Assert.Contains("eq=", args[filterIdx + 1]);
        Assert.Contains("shake=strength=5", args[filterIdx + 1]);
    }

    // ── BuildStaticOverlayFilter (backlog #29 — single looped PNG composite) ──

    [Fact]
    public void BuildStaticOverlayFilter_NoFades_ScalesFormatsAndGatesVisibility()
    {
        var filter = ExportArgBuilders.BuildStaticOverlayFilter(1920, 1080, 2.0, 7.0);

        Assert.Equal(
            "[1:v]scale=1920:1080,format=rgba[ov];" +
            "[0:v][ov]overlay=0:0:enable='between(t,2.000,7.000)'[out]",
            filter);
    }

    [Fact]
    public void BuildStaticOverlayFilter_FadeIn_AddsAlphaFadeAtOverlayStart()
    {
        var filter = ExportArgBuilders.BuildStaticOverlayFilter(1920, 1080, 2.0, 7.0, fadeInSeconds: 0.5);

        Assert.Contains("fade=t=in:st=2.000:d=0.500:alpha=1", filter);
        Assert.DoesNotContain("fade=t=out", filter);
    }

    [Fact]
    public void BuildStaticOverlayFilter_FadeOut_AddsAlphaFadeEndingAtOverlayEnd()
    {
        var filter = ExportArgBuilders.BuildStaticOverlayFilter(1920, 1080, 2.0, 7.0, fadeOutSeconds: 1.0);

        // Fade-out must START fadeOut seconds before the overlay's end (7.0 - 1.0 = 6.0).
        Assert.Contains("fade=t=out:st=6.000:d=1.000:alpha=1", filter);
        Assert.DoesNotContain("fade=t=in", filter);
    }

    [Fact]
    public void BuildStaticOverlayFilter_BothFades_InBeforeOutInChain()
    {
        var filter = ExportArgBuilders.BuildStaticOverlayFilter(1280, 720, 0.0, 5.0, 0.3, 0.3);

        var inIdx  = filter.IndexOf("fade=t=in", StringComparison.Ordinal);
        var outIdx = filter.IndexOf("fade=t=out", StringComparison.Ordinal);
        Assert.True(inIdx >= 0 && outIdx > inIdx);
        Assert.Contains("scale=1280:720", filter);
    }

    // ── BuildFilteredVideoArgs (backlog #29 — the silent video-less export) ──
    //
    // The native-callout pass used to emit "-vf <chain>" next to AudioPassthroughArgs'
    // "-map 0:a?". An explicit -map disables ffmpeg's default stream selection, so that
    // pass produced an audio-only file — and exited 0, so nothing downstream noticed.
    // These tests pin the fixed shape: the chain lives in a filter_complex whose video
    // output is explicitly mapped alongside the audio map.

    [Fact]
    public void BuildFilteredVideoArgs_MapsFilteredVideoExplicitly()
    {
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildFilteredVideoArgs("in.mp4", "drawbox=x=0:y=0:w=10:h=10", s, "out.mp4");

        var fcIdx = Array.IndexOf(args, "-filter_complex");
        Assert.True(fcIdx >= 0, "-filter_complex not found");
        Assert.Equal("[0:v]drawbox=x=0:y=0:w=10:h=10[out]", args[fcIdx + 1]);

        var mapIdx = Array.IndexOf(args, "-map");
        Assert.True(mapIdx >= 0, "-map not found");
        Assert.Equal("[out]", args[mapIdx + 1]);
    }

    [Fact]
    public void BuildFilteredVideoArgs_NeverEmitsBareVf()
    {
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildFilteredVideoArgs("in.mp4", "drawbox=x=0:y=0:w=10:h=10", s, "out.mp4");

        Assert.DoesNotContain("-vf", args);
    }

    [Fact]
    public void BuildFilteredVideoArgs_WithAudio_MapsVideoBeforeAudioMap()
    {
        var s    = DefaultSettings(); // IncludeAudio = true
        var args = ExportArgBuilders.BuildFilteredVideoArgs("in.mp4", "drawbox", s, "out.mp4");

        // Both maps present: the filtered video and the passthrough audio.
        var mapIndices = args.Select((a, i) => (a, i)).Where(x => x.a == "-map").Select(x => x.i).ToList();
        Assert.Equal(2, mapIndices.Count);
        Assert.Equal("[out]", args[mapIndices[0] + 1]);
        Assert.Equal("0:a?",  args[mapIndices[1] + 1]);
        Assert.Contains("copy", args);
    }

    [Fact]
    public void BuildFilteredVideoArgs_NoAudio_SingleMapPlusAnFlag()
    {
        var s    = DefaultSettings();
        s.IncludeAudio = false;
        var args = ExportArgBuilders.BuildFilteredVideoArgs("in.mp4", "drawbox", s, "out.mp4");

        var mapIndices = args.Select((a, i) => (a, i)).Where(x => x.a == "-map").Select(x => x.i).ToList();
        Assert.Single(mapIndices);
        Assert.Equal("[out]", args[mapIndices[0] + 1]);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void BuildFilteredVideoArgs_FirstArgIsInput_LastArgIsOutput()
    {
        var s    = DefaultSettings();
        var args = ExportArgBuilders.BuildFilteredVideoArgs("input.mp4", "drawbox", s, "output.mp4");

        Assert.Equal("-i",         args[0]);
        Assert.Equal("input.mp4",  args[1]);
        Assert.Equal("output.mp4", args[^1]);
    }

    [Fact]
    public void BuildFilteredVideoArgs_IncludesQualityArgs()
    {
        var s    = DefaultSettings(); // UseCrf = true, Crf = 23, libx264, preset fast
        var args = ExportArgBuilders.BuildFilteredVideoArgs("in.mp4", "drawbox", s, "out.mp4");

        Assert.Contains("-c:v",    args);
        Assert.Contains("libx264", args);
        Assert.Contains("-crf",    args);
        Assert.Contains("23",      args);
        Assert.Contains("-preset", args);
    }

    // ── BuildCalloutFilter expression variables (backlog #29) ────────────────
    //
    // drawbox's expression language defines iw/ih for the input frame size; the capital
    // W/H this filter originally used belong to the overlay filter and fail drawbox with
    // exit code 1. The bug shipped invisibly: the pass that used this chain also dropped
    // its video stream (bare "-map 0:a?"), so ffmpeg never evaluated the expressions.

    [Fact]
    public void BuildCalloutFilter_UsesDrawboxVariables_NotOverlayOnes()
    {
        var c = new CalloutClip { Name = "box", Shape = ShapeType.Rectangle,
                                  X = 0.1, Y = 0.1, Width = 0.2, Height = 0.15 };
        var filter = ExportArgBuilders.BuildCalloutFilter(c, DefaultSettings());

        Assert.Contains("iw*", filter);
        Assert.Contains("ih*", filter);
        Assert.DoesNotContain("(W*", filter);
        Assert.DoesNotContain("(H*", filter);
    }

    [Fact]
    public void BuildCalloutFilter_ShadowFragment_AlsoUsesDrawboxVariables()
    {
        var c = new CalloutClip { Name = "box", Shape = ShapeType.Rectangle,
                                  ShadowOffsetX = 3, ShadowOffsetY = 3, ShadowBlur = 4 };
        var filter = ExportArgBuilders.BuildCalloutFilter(c, DefaultSettings());

        // Two drawbox fragments (shadow + shape), neither using overlay-only variables.
        Assert.Equal(2, filter.Split("drawbox").Length - 1);
        Assert.DoesNotContain("(W*", filter);
        Assert.DoesNotContain("(H*", filter);
    }

    // ── BuildBackgroundRenderVideoArgs / BuildBackgroundRenderImageArgs — item #36 phase C.
    // These always emit an audio stream (real or synthetic-silent) so background-rendered
    // segments share a consistent stream layout and can be stream-copy concatenated. ─────────

    [Fact]
    public void BuildBackgroundRenderVideoArgs_HasRealAudio_MapsItDirectly_NoSyntheticInput()
    {
        var s = new ExportSettings { IncludeAudio = true };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, s, muteAudio: false);

        Assert.DoesNotContain("anullsrc", string.Join(' ', args));
        Assert.Contains("-map", args);
        AssertSubsequence(args, ["-map", "0:v", "-map", "0:a"]);
    }

    [Fact]
    public void BuildBackgroundRenderVideoArgs_SourceWithoutAudio_AddsSyntheticSilentAudioInput()
    {
        // A source with no audio stream at all is a third case, distinct from "muted" and from
        // "audio turned off in settings": the settings say to include audio and the clip is not
        // muted, but there is nothing to include. Mapping 0:a here makes ffmpeg refuse the whole
        // command — "Stream map '0:a' matches no streams" — and in the wasm worker that surfaces
        // as a background render frozen at a percentage with Export disabled behind it, which is
        // exactly how it was found. Screen recordings, trail cameras and exported animations all
        // arrive this way.
        var s = new ExportSettings { IncludeAudio = true };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, s, muteAudio: false, sourceHasAudio: false);

        Assert.DoesNotContain("0:a", args);
        AssertSubsequence(args, ["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]);
        AssertSubsequence(args, ["-map", "0:v", "-map", "1:a"]);
    }

    [Fact]
    public void BuildBackgroundRenderVideoArgs_SourceWithAudio_StillMapsItDirectly()
    {
        // The default must stay "the source has audio": a project saved before clips recorded
        // this would otherwise come back silent, which is a worse failure than the one being
        // fixed because nothing about it looks broken.
        var s = new ExportSettings { IncludeAudio = true };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, s, muteAudio: false);

        AssertSubsequence(args, ["-map", "0:v", "-map", "0:a"]);
        Assert.DoesNotContain("anullsrc", string.Join(' ', args));
    }

    [Fact]
    public void BuildBackgroundRenderVideoArgs_Muted_AddsSyntheticSilentAudioInput()
    {
        var s = new ExportSettings { IncludeAudio = true };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, s, muteAudio: true);

        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000", args);
        AssertSubsequence(args, ["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]);
        AssertSubsequence(args, ["-map", "0:v", "-map", "1:a", "-shortest"]);
    }

    [Fact]
    public void BuildBackgroundRenderVideoArgs_IncludeAudioFalse_AddsSyntheticSilentAudioInput()
    {
        var s = new ExportSettings { IncludeAudio = false };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, s, muteAudio: false);

        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000", args);
    }

    [Fact]
    public void BuildBackgroundRenderVideoArgs_NeverEmitsBareAn()
    {
        // The real BuildTrimArgs emits a bare "-an" for muted/no-audio clips — the background
        // variant must never do that, since every segment needs a real (even if silent) audio
        // stream to concat cleanly against segments that do have audio.
        var s = new ExportSettings { IncludeAudio = false };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
            "in.mp4", "out.mp4", 0, 5, 1.0, s, muteAudio: true);

        Assert.DoesNotContain("-an", args);
    }

    [Fact]
    public void BuildBackgroundRenderVideoArgs_PinsFrameRate_WhenSet()
    {
        var s = new ExportSettings { Fps = 30 };
        var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs("in.mp4", "out.mp4", 0, 5, 1.0, s);

        AssertSubsequence(args, ["-r", "30"]);
    }

    [Fact]
    public void BuildBackgroundRenderImageArgs_AlwaysAddsSyntheticSilentAudioInput()
    {
        var s = new ExportSettings();
        var args = ExportArgBuilders.BuildBackgroundRenderImageArgs("img.jpg", "out.mp4", 5.0, s);

        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000", args);
        AssertSubsequence(args, ["-map", "0:v", "-map", "1:a", "-shortest"]);
        Assert.DoesNotContain("-an", args);
    }

    [Fact]
    public void BuildBackgroundRenderImageArgs_AppliesScaleAndPad_WhenDimensionsGiven()
    {
        var s = new ExportSettings();
        var args = ExportArgBuilders.BuildBackgroundRenderImageArgs(
            "img.jpg", "out.mp4", 5.0, s, outputWidth: 640, outputHeight: 360);

        var joined = string.Join(' ', args);
        Assert.Contains("scale=640:360", joined);
        Assert.Contains("pad=640:360", joined);
    }

    // ── ElapsedSeconds ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 24, 0.0)]
    [InlineData(1, 24, 1.0 / 24.0)]
    [InlineData(12, 24, 0.5)]
    [InlineData(23, 24, 23.0 / 24.0)]
    public void ElapsedSeconds_MatchesDoubleDivision(int frameIndex, int fps, double expected)
    {
        // Regression test: frameIndex/fps with both operands int truncates to 0 for every frame
        // in a <1s clip at typical fps values — this silently froze every animated overlay
        // (callout AND text) at its first keyframe for its entire duration, found live while
        // verifying a bezier motion + shadow-height callout animation actually renders.
        Assert.Equal(expected, ExportArgBuilders.ElapsedSeconds(frameIndex, fps), precision: 9);
    }

    [Fact]
    public void ElapsedSeconds_DoesNotTruncateToZero_ForFrameCountUnderFps()
    {
        // The exact failure mode: a 1-second clip at 24fps has 24 frames (indices 0-23), all
        // strictly less than fps — plain int division truncates every one of them to 0.
        for (var i = 1; i < 24; i++)
            Assert.NotEqual(0.0, ExportArgBuilders.ElapsedSeconds(i, 24));
    }

    // ── ApplyMotionFrame(CalloutClip, MotionFrame) ─────────────────────────────

    [Fact]
    public void ApplyMotionFrame_Callout_PreservesRuns()
    {
        var runs = new List<TextRun> { new() { Text = "Hi", Superscript = true } };
        var clip = new CalloutClip { Runs = runs };
        var frame = new MotionFrame(0.5, 0.5, 1.0, 1.0);

        var result = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Same(runs, result.Runs);
    }

    [Fact]
    public void ApplyMotionFrame_Callout_CopiesShadowFromFrame()
    {
        // Regression test: the CalloutClip overload originally copied X/Y/Scale/Alpha/Fill/Stroke
        // from the interpolated MotionFrame but silently dropped all 4 shadow fields, so an
        // animated callout's shadow never actually animated in export (always the clip's own
        // static shadow) — found live while verifying a bezier + shadow-height animation.
        var clip = new CalloutClip
        {
            ShadowColor   = ColorHelper.Pack(0, 0, 0, 120),
            ShadowOffsetX = 3.0,
            ShadowOffsetY = 3.0,
            ShadowBlur    = 4.0,
        };
        var frame = new MotionFrame(0.5, 0.5, 1.0, 1.0)
        {
            ShadowColor   = ColorHelper.Pack(0, 0, 0, 200),
            ShadowOffsetX = 8.0,
            ShadowOffsetY = 8.0,
            ShadowBlur    = 14.0,
        };

        var result = ExportArgBuilders.ApplyMotionFrame(clip, frame);

        Assert.Equal(frame.ShadowColor, result.ShadowColor);
        Assert.Equal(frame.ShadowOffsetX, result.ShadowOffsetX);
        Assert.Equal(frame.ShadowOffsetY, result.ShadowOffsetY);
        Assert.Equal(frame.ShadowBlur, result.ShadowBlur);
    }

    /// <summary>Asserts <paramref name="needle"/> appears as a contiguous, in-order subsequence
    /// of <paramref name="haystack"/> — used to pin exact flag/value adjacency (e.g. "-map" "0:v"
    /// immediately followed by "-map" "0:a") rather than just "both appear somewhere".</summary>
    private static void AssertSubsequence(string[] haystack, string[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Skip(i).Take(needle.Length).SequenceEqual(needle)) return;
        }
        Assert.Fail($"Expected subsequence [{string.Join(", ", needle)}] not found in [{string.Join(", ", haystack)}]");
    }
}
