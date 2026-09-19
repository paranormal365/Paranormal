namespace Ben.Video.Editor.Services;

/// <summary>What kind of thing a picked file is.</summary>
public enum MediaKind
{
    Video,
    Audio,
    Image,
}

/// <summary>
/// Decides whether a file is video, audio or a picture.
/// </summary>
/// <remarks>
/// <para>It used to be two extension lists, and anything they did not name took the video path: a
/// .heic from a phone, a .tiff from a scanner, a .caf or .aiff recording all became a video clip
/// with no dimensions and an empty filmstrip, which is a confusing way to be told "not supported"
/// (2026-09-05 audit, media-panel-8).</para>
///
/// <para>The browser's own <c>File.type</c> is asked first, because it is what the operating system
/// says the file is rather than what somebody named it. Extensions are the fallback, for the cases
/// where the browser offers nothing — which is common for the less usual formats, exactly where the
/// old lists fell short.</para>
///
/// <para>Pure and static, so the routing can be tested without a browser.</para>
/// </remarks>
public static class MediaKindRouter
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".aac", ".ogg", ".oga", ".flac", ".m4a", ".opus", ".wma",
        ".aiff", ".aif", ".aifc", ".caf", ".amr", ".mka", ".mp2", ".ac3", ".dts",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".webp", ".bmp", ".svg",
        ".avif", ".heic", ".heif", ".tif", ".tiff", ".ico",
    };

    /// <summary>
    /// The kind of <paramref name="fileName"/>, using <paramref name="contentType"/> when the
    /// browser supplied one.
    /// </summary>
    public static MediaKind Decide(string fileName, string? contentType = null)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Audio;
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Image;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MediaKind.Video;
        }

        var extension = Path.GetExtension(fileName ?? string.Empty);

        if (AudioExtensions.Contains(extension)) return MediaKind.Audio;
        if (ImageExtensions.Contains(extension)) return MediaKind.Image;

        // Video is the fallback because it is the only kind the editor can make sense of without
        // knowing anything: ffmpeg will report what it actually found, and an unreadable file
        // fails loudly at the probe rather than silently becoming a 0x0 clip.
        return MediaKind.Video;
    }

    /// <summary>The MIME type to hand a browser when it needs one for an image blob.</summary>
    public static string ImageMimeType(string fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => "image/jpeg",
            ".png"                                 => "image/png",
            ".gif"                                 => "image/gif",
            ".webp"                                => "image/webp",
            ".bmp"                                 => "image/bmp",
            ".svg"                                 => "image/svg+xml",
            ".avif"                                => "image/avif",
            ".heic" or ".heif"                     => "image/heic",
            ".tif" or ".tiff"                      => "image/tiff",
            ".ico"                                 => "image/x-icon",
            _                                      => "image/png",
        };
}
