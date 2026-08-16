namespace Ben.Video.Editor.Models;

/// <summary>
/// Factory methods for common export setting presets.
/// Each method returns a fully configured <see cref="ExportSettings"/> instance.
/// </summary>
public static class ExportPresets
{
    /// <summary>
    /// Web HD — H.264, 1080p, CRF 23, fast preset, AAC 192k.
    /// Good balance of quality and file size for web publishing.
    /// </summary>
    public static ExportSettings WebHd() => new()
    {
        OutputFormat   = "mp4",
        VideoCodec     = "libx264",
        AudioCodec     = "aac",
        Resolution     = "1920x1080",
        UseCrf         = true,
        Crf            = 23,
        Preset         = "fast",
        IncludeAudio   = true,
        AudioBitrate   = 192,
        OutputFilename = "output-web-hd"
    };

    /// <summary>
    /// 1080p High Quality — H.264, 1080p, CRF 18, slower preset.
    /// Best quality at 1080p; larger file size.
    /// </summary>
    public static ExportSettings HighQuality1080p() => new()
    {
        OutputFormat   = "mp4",
        VideoCodec     = "libx264",
        AudioCodec     = "aac",
        Resolution     = "1920x1080",
        UseCrf         = true,
        Crf            = 18,
        Preset         = "slower",
        IncludeAudio   = true,
        AudioBitrate   = 320,
        OutputFilename = "output-1080p-hq"
    };

    /// <summary>
    /// 720p — H.264, 720p, CRF 23, medium preset. Smaller file, good quality.
    /// </summary>
    public static ExportSettings Standard720p() => new()
    {
        OutputFormat   = "mp4",
        VideoCodec     = "libx264",
        AudioCodec     = "aac",
        Resolution     = "1280x720",
        UseCrf         = true,
        Crf            = 23,
        Preset         = "medium",
        IncludeAudio   = true,
        AudioBitrate   = 128,
        OutputFilename = "output-720p"
    };

    /// <summary>
    /// Mobile — H.264, 480p, CRF 28, fast preset. Small file optimised for mobile.
    /// </summary>
    public static ExportSettings Mobile() => new()
    {
        OutputFormat   = "mp4",
        VideoCodec     = "libx264",
        AudioCodec     = "aac",
        Resolution     = "854x480",
        UseCrf         = true,
        Crf            = 28,
        Preset         = "fast",
        IncludeAudio   = true,
        AudioBitrate   = 96,
        OutputFilename = "output-mobile"
    };

    /// <summary>
    /// WebM / VP9 — open format, good compression, 1080p.
    /// </summary>
    public static ExportSettings WebM() => new()
    {
        OutputFormat   = "webm",
        VideoCodec     = "libvpx-vp9",
        AudioCodec     = "libopus",
        Resolution     = "1920x1080",
        UseCrf         = true,
        Crf            = 33,
        Preset         = "medium",
        IncludeAudio   = true,
        AudioBitrate   = 128,
        OutputFilename = "output-webm"
    };

    /// <summary>
    /// All named presets in display order.
    /// </summary>
    public static IReadOnlyList<(string Label, Func<ExportSettings> Factory)> All =>
    [
        ("Web HD (1080p)",      WebHd),
        ("High Quality 1080p",  HighQuality1080p),
        ("720p Standard",       Standard720p),
        ("Mobile (480p)",       Mobile),
        ("WebM / VP9",          WebM),
    ];
}
