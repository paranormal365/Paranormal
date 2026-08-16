using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class SubtitleBuilderTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static TextOverlay Overlay(string text, double start, double duration,
        TextVerticalAlign vAlign = TextVerticalAlign.Bottom,
        string fontColor = "#FFFFFF", string fontFamily = "Arial", int fontSize = 48)
        => new()
        {
            Text             = text,
            TimelinePosition = start,
            Duration         = duration,
            VerticalAlign    = vAlign,
            FontColor        = fontColor,
            FontFamily       = fontFamily,
            FontSize         = fontSize,
        };

    // ── SRT ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSrt_EmptyOverlays_ReturnsEmpty()
    {
        var result = SubtitleBuilder.BuildSrt([]);
        Assert.Empty(result);
    }

    [Fact]
    public void BuildSrt_SingleOverlay_ContainsIndex1()
    {
        var result = SubtitleBuilder.BuildSrt([Overlay("Hello", 0, 3)]);
        Assert.Contains("1", result);
    }

    [Fact]
    public void BuildSrt_SingleOverlay_CorrectTimestamps()
    {
        var result = SubtitleBuilder.BuildSrt([Overlay("Hello", 1.5, 2.0)]);
        // start = 1.5 → 00:00:01,500   end = 3.5 → 00:00:03,500
        Assert.Contains("00:00:01,500 --> 00:00:03,500", result);
    }

    [Fact]
    public void BuildSrt_SingleOverlay_ContainsText()
    {
        var result = SubtitleBuilder.BuildSrt([Overlay("Test caption", 0, 2)]);
        Assert.Contains("Test caption", result);
    }

    [Fact]
    public void BuildSrt_MultipleOverlays_SortedByTime()
    {
        var overlays = new[]
        {
            Overlay("Second", 5, 2),
            Overlay("First",  1, 2),
        };
        var result = SubtitleBuilder.BuildSrt(overlays);
        var idx1  = result.IndexOf("First", StringComparison.Ordinal);
        var idx2  = result.IndexOf("Second", StringComparison.Ordinal);
        Assert.True(idx1 < idx2);
    }

    [Fact]
    public void BuildSrt_MultipleOverlays_CorrectIndexing()
    {
        var overlays = new[]
        {
            Overlay("A", 0, 1),
            Overlay("B", 2, 1),
            Overlay("C", 4, 1),
        };
        var result = SubtitleBuilder.BuildSrt(overlays);
        // All three sequential indices present
        Assert.Contains("\n1\n", "\n" + result + "\n");
        Assert.Contains("\n2\n", "\n" + result + "\n");
        Assert.Contains("\n3\n", "\n" + result + "\n");
    }

    [Fact]
    public void BuildSrt_Timestamp_HoursHandledCorrectly()
    {
        // 3661 s = 1h 1m 1s
        var result = SubtitleBuilder.BuildSrt([Overlay("Long", 3661, 1)]);
        Assert.Contains("01:01:01,000", result);
    }

    // ── WebVTT ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWebVtt_EmptyOverlays_ReturnsOnlyHeader()
    {
        var result = SubtitleBuilder.BuildWebVtt([]);
        Assert.StartsWith("WEBVTT", result);
        Assert.DoesNotContain("-->", result);
    }

    [Fact]
    public void BuildWebVtt_HasWebVttHeader()
    {
        var result = SubtitleBuilder.BuildWebVtt([Overlay("Hi", 0, 1)]);
        Assert.StartsWith("WEBVTT", result);
    }

    [Fact]
    public void BuildWebVtt_Timestamp_UsesDotNotComma()
    {
        // WebVTT uses '.' as millisecond separator (not ',' like SRT)
        var result = SubtitleBuilder.BuildWebVtt([Overlay("Hi", 1.5, 2.0)]);
        Assert.Contains("00:00:01.500 --> 00:00:03.500", result);
    }

    [Fact]
    public void BuildWebVtt_TopAlign_UsesLine10()
    {
        var result = SubtitleBuilder.BuildWebVtt([Overlay("Top", 0, 1, TextVerticalAlign.Top)]);
        Assert.Contains("line:10%", result);
    }

    [Fact]
    public void BuildWebVtt_MiddleAlign_UsesLine50()
    {
        var result = SubtitleBuilder.BuildWebVtt([Overlay("Mid", 0, 1, TextVerticalAlign.Middle)]);
        Assert.Contains("line:50%", result);
    }

    [Fact]
    public void BuildWebVtt_BottomAlign_UsesLine90()
    {
        var result = SubtitleBuilder.BuildWebVtt([Overlay("Bot", 0, 1, TextVerticalAlign.Bottom)]);
        Assert.Contains("line:90%", result);
    }

    [Fact]
    public void BuildWebVtt_ContainsCueId()
    {
        var result = SubtitleBuilder.BuildWebVtt([Overlay("Cue", 0, 1)]);
        Assert.Contains("cue-1", result);
    }

    [Fact]
    public void BuildWebVtt_MultipleOverlays_SortedByTime()
    {
        var overlays = new[]
        {
            Overlay("Second", 10, 2),
            Overlay("First",   0, 2),
        };
        var result = SubtitleBuilder.BuildWebVtt(overlays);
        var idx1  = result.IndexOf("First", StringComparison.Ordinal);
        var idx2  = result.IndexOf("Second", StringComparison.Ordinal);
        Assert.True(idx1 < idx2);
    }

    // ── ASS ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAss_EmptyOverlays_ContainsScriptInfo()
    {
        var result = SubtitleBuilder.BuildAss([]);
        Assert.Contains("[Script Info]", result);
    }

    [Fact]
    public void BuildAss_ContainsScriptInfoSection()
    {
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2)]);
        Assert.Contains("[Script Info]", result);
        Assert.Contains("ScriptType: v4.00+", result);
    }

    [Fact]
    public void BuildAss_ContainsStylesSection()
    {
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2)]);
        Assert.Contains("[V4+ Styles]", result);
        Assert.Contains("Style: Default", result);
    }

    [Fact]
    public void BuildAss_ContainsEventsSection()
    {
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2)]);
        Assert.Contains("[Events]", result);
        Assert.Contains("Dialogue:", result);
    }

    [Fact]
    public void BuildAss_DerivesStyleFromFirstOverlay()
    {
        var overlays = new[]
        {
            Overlay("A", 0, 1, fontFamily: "Helvetica", fontSize: 64),
            Overlay("B", 2, 1),
        };
        var result = SubtitleBuilder.BuildAss(overlays);
        Assert.Contains("Helvetica", result);
        Assert.Contains(",64,", result);
    }

    [Fact]
    public void BuildAss_ContainsOverlayText()
    {
        var result = SubtitleBuilder.BuildAss([Overlay("Look Ma!", 0, 2)]);
        Assert.Contains("Look Ma!", result);
    }

    [Fact]
    public void BuildAss_MultipleOverlays_SortedByTime()
    {
        var overlays = new[]
        {
            Overlay("ZZZLAST", 5, 1),
            Overlay("AAAFIRST", 0, 1),
        };
        var result = SubtitleBuilder.BuildAss(overlays);
        var idx1  = result.IndexOf("AAAFIRST", StringComparison.Ordinal);
        var idx2  = result.IndexOf("ZZZLAST",  StringComparison.Ordinal);
        Assert.True(idx1 < idx2);
    }

    [Fact]
    public void BuildAss_NewlinesConvertedToHardBreak()
    {
        var result = SubtitleBuilder.BuildAss([Overlay("Line1\nLine2", 0, 2)]);
        Assert.Contains(@"Line1\NLine2", result);
    }

    // ── HexToAssBgr ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildAss_WhiteFontColor_ConvertsToASSWhite()
    {
        // #FFFFFF → BGR = &H00FFFFFF (R=FF G=FF B=FF — happens to be same)
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2, fontColor: "#FFFFFF")]);
        Assert.Contains("&H00FFFFFF", result);
    }

    [Fact]
    public void BuildAss_RedFontColor_ConvertedToBGR()
    {
        // #FF0000 (red) → BGR &H000000FF
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2, fontColor: "#FF0000")]);
        Assert.Contains("&H000000FF", result);
    }

    [Fact]
    public void BuildAss_BlueFontColor_ConvertedToBGR()
    {
        // #0000FF (blue) → BGR &H00FF0000
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2, fontColor: "#0000FF")]);
        Assert.Contains("&H00FF0000", result);
    }

    [Fact]
    public void BuildAss_InvalidHexColor_FallsBackToWhite()
    {
        // Invalid hex → fallback &H00FFFFFF
        var result = SubtitleBuilder.BuildAss([Overlay("Hi", 0, 2, fontColor: "not-a-color")]);
        Assert.Contains("&H00FFFFFF", result);
    }
}
