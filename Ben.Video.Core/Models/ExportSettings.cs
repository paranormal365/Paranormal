namespace Ben.Video.Editor.Models;

/// <summary>
/// Settings that control the ffmpeg export pipeline.
/// </summary>
public sealed class ExportSettings
{
    // ── Format ───────────────────────────────────────────────────────────────

    /// <summary>Output container format: "mp4", "webm", "mov".</summary>
    public string OutputFormat { get; set; } = "mp4";

    /// <summary>FFmpeg video codec identifier, e.g. "libx264", "libx265", "libvpx-vp9".</summary>
    public string VideoCodec { get; set; } = "libx264";

    /// <summary>FFmpeg audio codec identifier, e.g. "aac", "libopus".</summary>
    public string AudioCodec { get; set; } = "aac";

    /// <summary>Output resolution as "WxH", e.g. "1920x1080". Empty = source resolution.</summary>
    public string Resolution { get; set; } = "1280x720";

    /// <summary>Target video bitrate in kbps. Used when <see cref="UseCrf"/> is false.</summary>
    public int Bitrate { get; set; } = 4000;

    // ── Quality ───────────────────────────────────────────────────────────────

    /// <summary>
    /// When true, use CRF (Constant Rate Factor) quality mode instead of a fixed bitrate.
    /// Recommended for libx264 / libx265.
    /// </summary>
    public bool UseCrf { get; set; } = true;

    /// <summary>
    /// CRF value: lower = higher quality / larger file.
    /// libx264: 0–51, sane range 18–28 (default 23).
    /// libx265: 0–51, sane range 24–30 (default 28).
    /// </summary>
    public int Crf { get; set; } = 23;

    // ── Audio ─────────────────────────────────────────────────────────────────

    /// <summary>Include audio streams in the output. Set false for silent/video-only export.</summary>
    public bool IncludeAudio { get; set; } = true;

    /// <summary>Output audio bitrate in kbps (e.g. 128, 192, 256).</summary>
    public int AudioBitrate { get; set; } = 192;

    // ── Advanced ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Pixel format. Defaults to "yuv420p" for broad compatibility.
    /// Required for most H.264 streams on Apple/browser players.
    /// </summary>
    public string PixelFormat { get; set; } = "yuv420p";

    /// <summary>
    /// FFmpeg preset for libx264/libx265 (ultrafast … veryslow).
    /// Slower presets produce smaller files at the same CRF.
    /// </summary>
    public string Preset { get; set; } = "medium";

    /// <summary>Output filename stem (no extension). Sanitised before use.
    /// </summary>
    public string OutputFilename { get; set; } = "output";

    // ── Frame rate ───────────────────────────────────────────────────────────

    /// <summary>
    /// Output frame rate (fps). The source clips are re-encoded at this rate
    /// regardless of their original frame rate.
    /// Common values: 15, 24, 25, 30. Default: 30.
    /// </summary>
    public int Fps { get; set; } = 30;

    // ── Chapters ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Embed timeline markers as chapter metadata in the output file.
    /// Supported for MP4 and MOV only; automatically skipped for WebM.
    /// Has no effect when there are no timeline markers.
    /// Default: true
    /// </summary>
    public bool EmbedChapters { get; set; } = true;
}
