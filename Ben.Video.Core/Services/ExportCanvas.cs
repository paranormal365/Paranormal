namespace Ben.Video.Editor.Services;

/// <summary>
/// The pixel size an export renders at.
/// </summary>
/// <remarks>
/// <para>"Source resolution" is the first entry in the export dialog's resolution list and it did
/// not mean anything: it stored an empty string, and every place that read it fell back to
/// 1920x1080. Footage from a phone, a DVR or a 4K camera was therefore silently rescaled to Full HD
/// by the one option that promised not to touch it (2026-09-05 audit, export-5).</para>
///
/// <para>Pure, so what the picture ends up being can be checked without rendering one.</para>
/// </remarks>
public static class ExportCanvas
{
    /// <summary>Used when nothing else is known.</summary>
    public const int FallbackWidth = 1920;

    /// <inheritdoc cref="FallbackWidth"/>
    public const int FallbackHeight = 1080;

    /// <summary>
    /// The canvas for <paramref name="resolution"/>, falling back to the source's own size.
    /// </summary>
    /// <param name="resolution">
    /// A <c>WxH</c> string, or empty for "source resolution".
    /// </param>
    /// <param name="sourceWidth">The first clip's own width, or 0 when it is not known yet.</param>
    /// <param name="sourceHeight">The first clip's own height.</param>
    public static (int Width, int Height) Resolve(
        string? resolution, int sourceWidth = 0, int sourceHeight = 0)
    {
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            var parts = resolution.Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h)
                && w > 0 && h > 0)
                return (MakeEven(w), MakeEven(h));
        }

        if (sourceWidth > 0 && sourceHeight > 0)
            return (MakeEven(sourceWidth), MakeEven(sourceHeight));

        return (FallbackWidth, FallbackHeight);
    }

    /// <summary>
    /// Rounds down to an even number, because H.264 and H.265 in 4:2:0 cannot encode an odd
    /// dimension — a 1079-pixel-tall source would otherwise fail the encode rather than the check.
    /// </summary>
    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;
}
