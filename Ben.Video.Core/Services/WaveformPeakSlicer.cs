namespace Ben.Video.Editor.Services;

/// <summary>
/// The part of a waveform that a trimmed clip actually plays.
/// </summary>
/// <remarks>
/// <para>The peaks are decoded once from the whole source file, and the chip drew all of them
/// however the clip was trimmed. So a thirty-second excerpt from a three-minute recording showed
/// the shape of the whole three minutes squeezed into its chip — and the two halves of a split
/// showed the same picture as each other and as the original (2026-09-05 audit, audio-13 and
/// media-11).</para>
///
/// <para>Pure, and the arithmetic is the whole feature: the peaks array maps linearly onto the
/// source's duration, so the trim's fractions are the array's fractions.</para>
/// </remarks>
public static class WaveformPeakSlicer
{
    /// <summary>
    /// The peaks between <paramref name="startSeconds"/> and <paramref name="endSeconds"/>.
    /// </summary>
    /// <param name="peaks">Peaks decoded from the whole source.</param>
    /// <param name="sourceDuration">How long that whole source is.</param>
    /// <param name="startSeconds">Where the clip starts in the source.</param>
    /// <param name="endSeconds">Where it ends.</param>
    /// <returns>
    /// The slice, or the original array when there is nothing to slice — an untrimmed clip, an
    /// unknown duration, or peaks that have not been decoded yet.
    /// </returns>
    /// <remarks>
    /// An end at or before the start means "not trimmed", which is exactly what
    /// <c>AudioClip.TrimmedDuration</c> makes of it: the clip is the whole source long. The picture
    /// and the length have to agree, or a chip drawn from a slice would be stretched across a
    /// full-length chip.
    /// </remarks>
    public static float[]? Slice(
        float[]? peaks, double sourceDuration, double startSeconds, double endSeconds)
    {
        if (peaks is null || peaks.Length == 0) return peaks;
        if (sourceDuration <= 0) return peaks;
        if (endSeconds <= startSeconds) return peaks;

        var start = Math.Clamp(startSeconds, 0, sourceDuration);
        var end   = Math.Clamp(endSeconds,   0, sourceDuration);
        if (end <= start) return peaks;

        // Untrimmed, near enough. Slicing would copy the array for nothing.
        if (start <= 0 && end >= sourceDuration - 1e-6) return peaks;

        var from = (int)Math.Floor(start / sourceDuration * peaks.Length);
        var to   = (int)Math.Ceiling(end / sourceDuration * peaks.Length);

        from = Math.Clamp(from, 0, peaks.Length - 1);
        to   = Math.Clamp(to,   from + 1, peaks.Length);

        return peaks[from..to];
    }
}
