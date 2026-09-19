using System.Globalization;

namespace Ben.Video.Editor.Effects;

/// <summary>
/// Builds a <c>zoompan</c> filter fragment that ffmpeg will actually run.
/// </summary>
/// <remarks>
/// <para>Seven effects — Zoom In, Zoom Out and Ken Burns for both video and images, plus Pulse —
/// each wrote their own zoompan, and every one of them shared two mistakes that meant none of them
/// worked on export (2026-09-05 audit, motion-8).</para>
///
/// <para>The first was the clock. Progress was written as <c>on/fps</c>, but <c>fps</c> is not a
/// variable zoompan defines; an expression naming it fails to evaluate, and the effect either does
/// nothing or takes the filter graph down with it. zoompan does publish the output timestamp in
/// seconds, which is the clock these easing curves were written against in the first place.</para>
///
/// <para>The second was the output size. <c>s</c> takes a literal <c>WxH</c>, not an expression, so
/// <c>s=iw+"x"+ih</c> was never a size ffmpeg could parse — and with no usable size zoompan quietly
/// falls back to 1280x720, which would have resized the frame in the middle of a pipeline whose
/// every other segment lands on the export canvas. Concat then joins segments of two different
/// sizes.</para>
///
/// <para>And <c>d</c>, the number of frames each input frame is held for, was set to the whole
/// effect's frame count. That is right for a single still image handed to zoompan on its own and
/// wrong here, where the input is already a stream of frames: it repeated every frame hundreds of
/// times. One output frame per input frame is what these effects mean.</para>
/// </remarks>
public static class ZoompanFragment
{
    /// <summary>The variable zoompan publishes for the current output time, in seconds.</summary>
    /// <remarks>
    /// Use this as the time variable when asking <see cref="EasingHelper"/> for a progress
    /// expression inside a zoompan, exactly as <c>t</c> is used inside <c>crop</c> or
    /// <c>rotate</c>.
    /// </remarks>
    public const string TimeVariable = "ot";

    /// <summary>Centres the visible window on the frame at the current zoom.</summary>
    public const string CentredX = "(iw-iw/zoom)/2";

    /// <summary>Centres the visible window vertically at the current zoom.</summary>
    public const string CentredY = "(ih-ih/zoom)/2";

    /// <summary>
    /// Assembles the fragment, or an empty string when the frame size is unknown.
    /// </summary>
    /// <param name="zoomExpression">The zoom factor over time, an expression in <c>ot</c>.</param>
    /// <param name="xExpression">Left edge of the visible window.</param>
    /// <param name="yExpression">Top edge of the visible window.</param>
    /// <param name="canvasWidth">Frame width in pixels.</param>
    /// <param name="canvasHeight">Frame height in pixels.</param>
    /// <returns>
    /// An empty string when the size is unknown, because a zoompan without one resizes the frame
    /// rather than failing, and a segment of the wrong size breaks the whole export rather than
    /// just this effect.
    /// </returns>
    public static string Build(
        string zoomExpression, string xExpression, string yExpression,
        int canvasWidth, int canvasHeight)
    {
        if (canvasWidth <= 0 || canvasHeight <= 0) return string.Empty;

        var ic = CultureInfo.InvariantCulture;
        return $"zoompan=z='{zoomExpression}':x='{xExpression}':y='{yExpression}'"
             + $":d=1:s={canvasWidth.ToString(ic)}x{canvasHeight.ToString(ic)}";
    }
}
