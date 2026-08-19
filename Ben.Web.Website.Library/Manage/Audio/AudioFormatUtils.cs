namespace Ben.Web.Website.Library.Manage.Audio;

/// <summary>
/// Static formatting and detection helpers shared by <see cref="AudioFilePreview"/>
/// and <see cref="WaveSurferPlayer"/>.
/// </summary>
public static class AudioFormatUtils
{
    // ── Audio content-type detection ──────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="contentType"/> indicates an audio
    /// file that can be loaded by WaveSurfer (i.e. starts with <c>audio/</c>).
    /// </summary>
    public static bool IsAudioContentType(string? contentType) =>
        contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

    // ── Time formatting ───────────────────────────────────────────────────────

    /// <summary>
    /// Formats a duration in seconds as <c>m:ss.f</c> or <c>h:mm:ss.f</c>.
    /// Used in the WaveSurfer controls bar and the AudioFilePreview full-view info panel.
    /// </summary>
    public static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss\.f")
            : ts.ToString(@"m\:ss\.f");
    }

    // ── File-size formatting ──────────────────────────────────────────────────

    /// <summary>
    /// Formats a byte count as the largest meaningful unit followed by the raw
    /// byte count in parentheses (e.g. <c>3.14 MB  (3,293,184 bytes)</c>).
    /// Used in the AudioFilePreview full-view info panel.
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F2} GB  ({bytes:N0} bytes)";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F2} MB  ({bytes:N0} bytes)";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:F2} KB  ({bytes:N0} bytes)";
        return $"{bytes} bytes";
    }

    /// <summary>
    /// Compact size string without the raw byte suffix — used in upload file
    /// dialogs where space is constrained.
    /// </summary>
    public static string FormatSizeCompact(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes} B";
    }
}
