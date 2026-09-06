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

/// <summary>
/// Defaults the two places that save a clip must agree on.
/// </summary>
/// <remarks>
/// A clip can be saved from the editor's Save-as-clip dialog or from inside the region explorer,
/// and the two disagreed about whether to normalize: the dialog started with it on and explained
/// why, the explorer never offered it and never sent it. So the same region saved from two places
/// two clicks apart produced audibly different files, and only one of them was the one the copy
/// described (2026-09-06 audio walk, finding N).
/// </remarks>
public static class AudioClipDefaults
{
    /// <summary>
    /// Whether a clip is normalized unless somebody says otherwise.
    /// </summary>
    /// <remarks>
    /// On, because of what these clips are for: an EVP is usually far quieter than everything
    /// around it, so a clip cut at the recording's own level is often close to inaudible. The
    /// original is never touched, and the box can always be cleared.
    /// </remarks>
    public const bool Normalize = true;
}
