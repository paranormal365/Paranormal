using System.Text;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Generates subtitle file content from <see cref="TextOverlay"/> items.
///
/// <para>Supported formats:</para>
/// <list type="bullet">
///   <item><b>SRT</b> — SubRip Text. Widely supported; embeddable in MP4 as a soft subtitle track.</item>
///   <item><b>ASS</b> — Advanced SubStation Alpha. Richer styling; supported by ffmpeg's <c>libass</c>.</item>
///   <item><b>WebVTT</b> — Web Video Text Tracks. Native browser support; ideal for web playback.</item>
/// </list>
///
/// <para>Each <see cref="TextOverlay"/> becomes one cue. Overlays are sorted by
/// <see cref="TrackItem.TimelinePosition"/> before output.</para>
/// </summary>
public static class SubtitleBuilder
{
    // ── SRT ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate SubRip Text (.srt) content from a set of text overlays.
    /// </summary>
    public static string BuildSrt(IEnumerable<TextOverlay> overlays)
    {
        var sb  = new StringBuilder();
        var idx = 1;

        foreach (var o in overlays.OrderBy(x => x.TimelinePosition))
        {
            var start = o.TimelinePosition;
            var end   = o.TimelinePosition + o.Duration;

            sb.AppendLine(idx.ToString());
            sb.AppendLine($"{SrtTime(start)} --> {SrtTime(end)}");
            sb.AppendLine(o.Text);
            sb.AppendLine();
            idx++;
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>SRT timestamp: hh:mm:ss,mmm</summary>
    private static string SrtTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }

    // ── WebVTT ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate WebVTT (.vtt) content from a set of text overlays.
    /// Includes basic region/position hints derived from <see cref="TextOverlay.VerticalAlign"/>.
    /// </summary>
    public static string BuildWebVtt(IEnumerable<TextOverlay> overlays)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();

        var idx = 1;
        foreach (var o in overlays.OrderBy(x => x.TimelinePosition))
        {
            var start = o.TimelinePosition;
            var end   = o.TimelinePosition + o.Duration;

            var position = o.VerticalAlign switch
            {
                TextVerticalAlign.Top    => " line:10%",
                TextVerticalAlign.Middle => " line:50%",
                _                        => " line:90%",
            };

            sb.AppendLine($"cue-{idx}");
            sb.AppendLine($"{VttTime(start)} --> {VttTime(end)}{position}");
            sb.AppendLine(o.Text);
            sb.AppendLine();
            idx++;
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>WebVTT timestamp: hh:mm:ss.mmm</summary>
    private static string VttTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }

    // ── ASS ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate Advanced SubStation Alpha (.ass) content.
    /// Includes a basic style derived from the first overlay's font settings.
    /// </summary>
    public static string BuildAss(IEnumerable<TextOverlay> overlays)
    {
        var list = overlays.OrderBy(x => x.TimelinePosition).ToList();

        // Derive default style from first overlay (fallback to defaults)
        var first      = list.FirstOrDefault();
        var fontName   = first?.FontFamily ?? "Arial";
        var fontSize   = first?.FontSize   ?? 48;
        var fontColor  = HexToAssBgr(first?.FontColor ?? "#FFFFFF");

        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("Collisions: Normal");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Default,{fontName},{fontSize},{fontColor},&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,10,1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var o in list)
        {
            var start = AssTime(o.TimelinePosition);
            var end   = AssTime(o.TimelinePosition + o.Duration);
            var text  = o.Text.Replace("\n", "\\N");
            sb.AppendLine($"Dialogue: 0,{start},{end},Default,,0,0,0,,{text}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>ASS timestamp: h:mm:ss.cc (centiseconds)</summary>
    private static string AssTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        var cs = ts.Milliseconds / 10;
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{cs:D2}";
    }

    /// <summary>Convert CSS hex colour (#RRGGBB) to ASS BGR hex (&amp;H00BBGGRR).</summary>
    private static string HexToAssBgr(string hex)
    {
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6)
            {
                var r = Convert.ToByte(h[0..2], 16);
                var g = Convert.ToByte(h[2..4], 16);
                var b = Convert.ToByte(h[4..6], 16);
                return $"&H00{b:X2}{g:X2}{r:X2}";
            }
        }
        catch { /* fall through to default */ }
        return "&H00FFFFFF";
    }
}
